Set-StrictMode -Version 2.0

$script:LauncherVersion = "0.1.0"

function Get-CodexApiLauncherRoot {
    [CmdletBinding()]
    param()

    if ($env:CODEX_API_LAUNCHER_HOME) {
        return [System.IO.Path]::GetFullPath($env:CODEX_API_LAUNCHER_HOME)
    }

    $localAppData = $env:LOCALAPPDATA
    if (-not $localAppData) {
        $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    }
    if (-not $localAppData) {
        throw "Could not resolve LOCALAPPDATA. Set CODEX_API_LAUNCHER_HOME explicitly."
    }

    return (Join-Path $localAppData "CodexApiLauncher")
}

function Get-StatePath {
    Join-Path (Get-CodexApiLauncherRoot) "profiles.json"
}

function Get-SecretsDir {
    Join-Path (Get-CodexApiLauncherRoot) "secrets"
}

function Get-LaunchersDir {
    Join-Path (Get-CodexApiLauncherRoot) "launchers"
}

function Ensure-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
    }
}

function Write-Utf8File {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $parent = Split-Path -Parent $Path
    if ($parent) {
        Ensure-Directory $parent
    }
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [AllowNull()]$DefaultValue
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $DefaultValue
    }

    $raw = Get-Content -LiteralPath $Path -Raw
    if (-not $raw.Trim()) {
        return $DefaultValue
    }

    return ($raw | ConvertFrom-Json)
}

function New-EmptyState {
    [pscustomobject][ordered]@{
        version = 1
        launcherVersion = $script:LauncherVersion
        profiles = @()
    }
}

function Read-State {
    $statePath = Get-StatePath
    $state = Read-JsonFile -Path $statePath -DefaultValue (New-EmptyState)
    if (-not ($state.PSObject.Properties.Name -contains "profiles") -or $null -eq $state.profiles) {
        $state | Add-Member -NotePropertyName profiles -NotePropertyValue @() -Force
    }
    return $state
}

function Write-State {
    param([Parameter(Mandatory = $true)]$State)
    if (-not ($State.PSObject.Properties.Name -contains "launcherVersion")) {
        $State | Add-Member -NotePropertyName launcherVersion -NotePropertyValue $script:LauncherVersion -Force
    }
    else {
        $State.launcherVersion = $script:LauncherVersion
    }
    $json = $State | ConvertTo-Json -Depth 12
    Write-Utf8File -Path (Get-StatePath) -Content ($json + [Environment]::NewLine)
}

function ConvertTo-SafeProfileId {
    param([Parameter(Mandatory = $true)][string]$Id)

    $safe = $Id.Trim().ToLowerInvariant() -replace "[^a-z0-9_-]", "-"
    $safe = $safe.Trim("-_")
    if (-not $safe) {
        throw "Profile id must contain at least one letter or number."
    }
    if ($safe.Length -gt 40) {
        $safe = $safe.Substring(0, 40).Trim("-_")
    }
    if ($safe -notmatch "^[a-z0-9][a-z0-9_-]*$") {
        throw "Profile id '$Id' is invalid after normalization."
    }
    return $safe
}

function ConvertTo-ProviderId {
    param([Parameter(Mandatory = $true)][string]$Id)
    $providerSuffix = $Id.ToLowerInvariant() -replace "[^a-z0-9_]", "_"
    return "api_$providerSuffix"
}

function ConvertTo-EnvKeyName {
    param([Parameter(Mandatory = $true)][string]$Id)
    $envSuffix = $Id.ToUpperInvariant() -replace "[^A-Z0-9]", "_"
    return "CODEX_API_${envSuffix}_KEY"
}

function ConvertTo-TomlString {
    param([AllowNull()][string]$Value)
    if ($null -eq $Value) {
        $Value = ""
    }
    return ($Value | ConvertTo-Json -Compress)
}

function Test-BaseUrl {
    param([Parameter(Mandatory = $true)][string]$BaseUrl)

    $uri = $null
    if (-not [Uri]::TryCreate($BaseUrl.Trim(), [UriKind]::Absolute, [ref]$uri)) {
        throw "BaseUrl must be an absolute http or https URL."
    }
    if ($uri.Scheme -ne "http" -and $uri.Scheme -ne "https") {
        throw "BaseUrl must use http or https."
    }
    return $uri.AbsoluteUri.TrimEnd("/")
}

function Get-ProfileHome {
    param([Parameter(Mandatory = $true)][string]$Id)
    Join-Path (Join-Path (Get-CodexApiLauncherRoot) "profiles") $Id
}

function Get-ProfileCodexHome {
    param([Parameter(Mandatory = $true)][string]$Id)
    Join-Path (Get-ProfileHome -Id $Id) "codex-home"
}

function Get-ProfileById {
    param(
        [Parameter(Mandatory = $true)]$State,
        [Parameter(Mandatory = $true)][string]$Id
    )

    $safeId = ConvertTo-SafeProfileId $Id
    foreach ($profile in @($State.profiles)) {
        if ([string]::Equals($profile.id, $safeId, [StringComparison]::OrdinalIgnoreCase)) {
            return $profile
        }
    }
    return $null
}

function Get-ProfileSecretPath {
    param([Parameter(Mandatory = $true)][string]$Id)
    Join-Path (Get-SecretsDir) "$Id.secret.json"
}

function ConvertFrom-SecureStringToPlainText {
    param([Parameter(Mandatory = $true)][securestring]$SecureString)

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureString)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        if ($bstr -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
}

function Save-ProfileSecret {
    param(
        [Parameter(Mandatory = $true)]$Profile,
        [Parameter(Mandatory = $true)][securestring]$ApiKey
    )

    Ensure-Directory (Get-SecretsDir)
    $payload = [ordered]@{
        version = 1
        profileId = $Profile.id
        envKeyName = $Profile.envKeyName
        protectedApiKey = (ConvertFrom-SecureString $ApiKey)
        updatedAt = (Get-Date).ToUniversalTime().ToString("o")
    }
    Write-Utf8File -Path (Get-ProfileSecretPath -Id $Profile.id) -Content (($payload | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
}

function Read-ProfileApiKey {
    param([Parameter(Mandatory = $true)]$Profile)

    $secretPath = Get-ProfileSecretPath -Id $Profile.id
    if (-not (Test-Path -LiteralPath $secretPath)) {
        throw "No API key is stored for profile '$($Profile.id)'. Run Set-CodexApiProfileApiKey -Id '$($Profile.id)' -ApiKey (Read-Host -AsSecureString 'API key')."
    }

    $secret = Read-JsonFile -Path $secretPath -DefaultValue $null
    if ($null -eq $secret -or -not $secret.protectedApiKey) {
        throw "Stored API key file is missing or invalid: $secretPath"
    }

    $secure = ConvertTo-SecureString $secret.protectedApiKey
    return ConvertFrom-SecureStringToPlainText -SecureString $secure
}

function Get-ConfigText {
    param([Parameter(Mandatory = $true)]$Profile)

    $lines = @(
        "model_provider = $(ConvertTo-TomlString $Profile.providerId)"
        "model = $(ConvertTo-TomlString $Profile.model)"
        "model_reasoning_effort = $(ConvertTo-TomlString $Profile.reasoningEffort)"
        ""
        "[model_providers.$($Profile.providerId)]"
        "name = $(ConvertTo-TomlString $Profile.providerName)"
        "base_url = $(ConvertTo-TomlString $Profile.baseUrl)"
        "env_key = $(ConvertTo-TomlString $Profile.envKeyName)"
        "temp_env_key = $(ConvertTo-TomlString $Profile.envKeyName)"
        "wire_api = `"responses`""
        "requires_openai_auth = false"
        ""
    )
    return ($lines -join [Environment]::NewLine)
}

function Write-ProfileConfig {
    param([Parameter(Mandatory = $true)]$Profile)

    Ensure-Directory $Profile.paths.codexHome
    Write-Utf8File -Path (Join-Path $Profile.paths.codexHome "config.toml") -Content (Get-ConfigText -Profile $Profile)
}

function Get-ModuleRunnerPath {
    Join-Path $PSScriptRoot "Run-CodexApiProfile.ps1"
}

function Write-ProfileLauncher {
    param([Parameter(Mandatory = $true)]$Profile)

    Ensure-Directory (Get-LaunchersDir)
    $runner = Get-ModuleRunnerPath
    $workspace = [string]$Profile.workspace
    $content = @"
param(
    [Parameter(ValueFromRemainingArguments = `$true)]
    [string[]]`$CodexArgs
)

`$runner = $(ConvertTo-TomlString $runner)
& `$runner -Id $(ConvertTo-TomlString $Profile.id) -Workspace $(ConvertTo-TomlString $workspace) @CodexArgs
exit `$LASTEXITCODE
"@
    Write-Utf8File -Path $Profile.paths.launcherPath -Content ($content + [Environment]::NewLine)
}

function New-CodexApiProfile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [Parameter(Mandatory = $true)][string]$Model,
        [string]$Workspace = (Get-Location).Path,
        [ValidateSet("low", "medium", "high", "xhigh", "max", "ultra")][string]$ReasoningEffort = "medium",
        [securestring]$ApiKey,
        [switch]$Force
    )

    $safeId = ConvertTo-SafeProfileId $Id
    $normalizedBaseUrl = Test-BaseUrl $BaseUrl
    $state = Read-State
    $existing = Get-ProfileById -State $state -Id $safeId
    if ($existing -and -not $Force) {
        throw "Profile '$safeId' already exists. Use -Force to replace it."
    }

    $now = (Get-Date).ToUniversalTime().ToString("o")
    $codexHome = Get-ProfileCodexHome -Id $safeId
    $launcherPath = Join-Path (Get-LaunchersDir) "$safeId.ps1"
    $providerId = ConvertTo-ProviderId -Id $safeId
    $envKeyName = ConvertTo-EnvKeyName -Id $safeId

    $profile = [ordered]@{
        id = $safeId
        name = $Name
        providerName = $Name
        providerId = $providerId
        baseUrl = $normalizedBaseUrl
        model = $Model
        reasoningEffort = $ReasoningEffort
        envKeyName = $envKeyName
        workspace = [System.IO.Path]::GetFullPath($Workspace)
        createdAt = if ($existing -and $existing.createdAt) { $existing.createdAt } else { $now }
        updatedAt = $now
        paths = [ordered]@{
            profileHome = Get-ProfileHome -Id $safeId
            codexHome = $codexHome
            launcherPath = $launcherPath
        }
        desktop = [ordered]@{
            reserved = $true
            userDataDir = (Join-Path (Get-ProfileHome -Id $safeId) "desktop-user-data")
        }
    }

    Ensure-Directory $profile.paths.profileHome
    Write-ProfileConfig -Profile $profile
    Write-ProfileLauncher -Profile $profile

    $profiles = @()
    foreach ($item in @($state.profiles)) {
        if (-not [string]::Equals($item.id, $safeId, [StringComparison]::OrdinalIgnoreCase)) {
            $profiles += $item
        }
    }
    $profiles += $profile
    $state.profiles = @($profiles | Sort-Object id)
    Write-State -State $state

    if ($ApiKey) {
        Save-ProfileSecret -Profile $profile -ApiKey $ApiKey
    }

    [pscustomobject]@{
        Id = $profile.id
        Name = $profile.name
        BaseUrl = $profile.baseUrl
        Model = $profile.model
        EnvKeyName = $profile.envKeyName
        CodexHome = $profile.paths.codexHome
        ConfigPath = (Join-Path $profile.paths.codexHome "config.toml")
        LauncherPath = $profile.paths.launcherPath
        HasApiKey = [bool](Test-Path -LiteralPath (Get-ProfileSecretPath -Id $profile.id))
    }
}

function Set-CodexApiProfileApiKey {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][securestring]$ApiKey
    )

    $state = Read-State
    $profile = Get-ProfileById -State $state -Id $Id
    if (-not $profile) {
        throw "Profile '$Id' was not found."
    }
    Save-ProfileSecret -Profile $profile -ApiKey $ApiKey

    [pscustomobject]@{
        Id = $profile.id
        EnvKeyName = $profile.envKeyName
        SecretPath = Get-ProfileSecretPath -Id $profile.id
        HasApiKey = $true
    }
}

function Get-CodexApiProfile {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Id)

    $state = Read-State
    $profile = Get-ProfileById -State $state -Id $Id
    if (-not $profile) {
        throw "Profile '$Id' was not found."
    }
    return $profile
}

function Get-CodexApiProfiles {
    [CmdletBinding()]
    param()

    $state = Read-State
    foreach ($profile in @($state.profiles)) {
        [pscustomobject]@{
            Id = $profile.id
            Name = $profile.name
            BaseUrl = $profile.baseUrl
            Model = $profile.model
            ReasoningEffort = $profile.reasoningEffort
            EnvKeyName = $profile.envKeyName
            Workspace = $profile.workspace
            CodexHome = $profile.paths.codexHome
            LauncherPath = $profile.paths.launcherPath
            HasApiKey = [bool](Test-Path -LiteralPath (Get-ProfileSecretPath -Id $profile.id))
        }
    }
}

Set-Alias -Name List-CodexApiProfiles -Value Get-CodexApiProfiles

function Join-ProviderEndpoint {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [Parameter(Mandatory = $true)][string]$Suffix
    )
    return ($BaseUrl.TrimEnd("/") + $Suffix)
}

function Invoke-ProviderHttp {
    param(
        [Parameter(Mandatory = $true)][ValidateSet("GET", "POST")][string]$Method,
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$ApiKey,
        [string]$Body,
        [int]$TimeoutSeconds = 15
    )

    Add-Type -AssemblyName System.Net.Http | Out-Null
    $client = New-Object System.Net.Http.HttpClient
    $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
    try {
        $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::$Method, $Url)
        $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", $ApiKey)
        if ($Method -eq "POST") {
            $request.Content = New-Object System.Net.Http.StringContent($Body, [System.Text.Encoding]::UTF8, "application/json")
        }
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        return [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            IsSuccessStatusCode = [bool]$response.IsSuccessStatusCode
            Body = $text
        }
    }
    finally {
        $client.Dispose()
    }
}

function Test-CodexApiProfile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [int]$TimeoutSeconds = 15
    )

    $profile = Get-CodexApiProfile -Id $Id
    $apiKey = Read-ProfileApiKey -Profile $profile
    $modelsUrl = Join-ProviderEndpoint -BaseUrl $profile.baseUrl -Suffix "/models"
    $responsesUrl = Join-ProviderEndpoint -BaseUrl $profile.baseUrl -Suffix "/responses"

    $modelsResult = $null
    $responsesResult = $null
    $modelCount = $null
    $details = New-Object System.Collections.Generic.List[string]

    try {
        $modelsResult = Invoke-ProviderHttp -Method GET -Url $modelsUrl -ApiKey $apiKey -TimeoutSeconds $TimeoutSeconds
        if ($modelsResult.StatusCode -eq 401 -or $modelsResult.StatusCode -eq 403) {
            $details.Add("The provider rejected the API key at /models.")
            return [pscustomobject]@{
                Ok = $false
                Status = "auth_failed"
                Id = $profile.id
                BaseUrl = $profile.baseUrl
                Model = $profile.model
                ModelsHttpStatus = $modelsResult.StatusCode
                ResponsesHttpStatus = $null
                ModelCount = $null
                Details = ($details -join " ")
            }
        }

        try {
            $payload = $modelsResult.Body | ConvertFrom-Json
            if ($payload.PSObject.Properties.Name -contains "data") {
                $modelCount = @($payload.data).Count
            }
            elseif ($payload.PSObject.Properties.Name -contains "models") {
                $modelCount = @($payload.models).Count
            }
        }
        catch {
            $details.Add("Could not parse /models as JSON.")
        }
    }
    catch {
        return [pscustomobject]@{
            Ok = $false
            Status = "unreachable"
            Id = $profile.id
            BaseUrl = $profile.baseUrl
            Model = $profile.model
            ModelsHttpStatus = $null
            ResponsesHttpStatus = $null
            ModelCount = $null
            Details = $_.Exception.Message
        }
    }

    $body = @{
        model = $profile.model
        input = "Respond with ok."
    } | ConvertTo-Json -Compress

    try {
        $responsesResult = Invoke-ProviderHttp -Method POST -Url $responsesUrl -ApiKey $apiKey -Body $body -TimeoutSeconds $TimeoutSeconds
    }
    catch {
        return [pscustomobject]@{
            Ok = $false
            Status = "responses_unreachable"
            Id = $profile.id
            BaseUrl = $profile.baseUrl
            Model = $profile.model
            ModelsHttpStatus = if ($modelsResult) { $modelsResult.StatusCode } else { $null }
            ResponsesHttpStatus = $null
            ModelCount = $modelCount
            Details = $_.Exception.Message
        }
    }

    if ($responsesResult.IsSuccessStatusCode) {
        return [pscustomobject]@{
            Ok = $true
            Status = "passed"
            Id = $profile.id
            BaseUrl = $profile.baseUrl
            Model = $profile.model
            ModelsHttpStatus = $modelsResult.StatusCode
            ResponsesHttpStatus = $responsesResult.StatusCode
            ModelCount = $modelCount
            Details = "The provider accepted a minimal /responses request."
        }
    }

    $status = "responses_failed"
    if ($responsesResult.StatusCode -eq 401 -or $responsesResult.StatusCode -eq 403) {
        $status = "auth_failed"
    }
    elseif ($responsesResult.StatusCode -eq 404 -or $responsesResult.StatusCode -eq 405) {
        $status = "responses_unsupported"
    }

    $responseBody = $responsesResult.Body
    if ($responseBody -and $responseBody.Length -gt 800) {
        $responseBody = $responseBody.Substring(0, 800)
    }

    [pscustomobject]@{
        Ok = $false
        Status = $status
        Id = $profile.id
        BaseUrl = $profile.baseUrl
        Model = $profile.model
        ModelsHttpStatus = $modelsResult.StatusCode
        ResponsesHttpStatus = $responsesResult.StatusCode
        ModelCount = $modelCount
        Details = $responseBody
    }
}

function Resolve-PowerShellExecutable {
    if ($PSHOME) {
        $candidate = Join-Path $PSHOME "pwsh.exe"
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
        $candidate = Join-Path $PSHOME "powershell.exe"
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    $preferred = "D:\develop\PowerShell\7\pwsh.exe"
    if (Test-Path -LiteralPath $preferred) {
        return $preferred
    }

    $cmd = Get-Command pwsh.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }
    $cmd = Get-Command powershell.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }
    throw "Could not find pwsh.exe or powershell.exe."
}

function Invoke-CodexApiProfileInCurrentWindow {
    param(
        [Parameter(Mandatory = $true)]$Profile,
        [string]$Workspace,
        [string[]]$CodexArgs = @()
    )

    $apiKey = Read-ProfileApiKey -Profile $Profile
    $oldCodexHome = $env:CODEX_HOME
    $oldApiKey = [Environment]::GetEnvironmentVariable($Profile.envKeyName, "Process")
    try {
        Ensure-Directory $Profile.paths.codexHome
        $env:CODEX_HOME = $Profile.paths.codexHome
        [Environment]::SetEnvironmentVariable($Profile.envKeyName, $apiKey, "Process")

        $workspaceToUse = if ($Workspace) { [System.IO.Path]::GetFullPath($Workspace) } else { [string]$Profile.workspace }
        $args = @("-C", $workspaceToUse)
        if ($CodexArgs) {
            $args += $CodexArgs
        }

        $codexCommand = Get-Command codex -ErrorAction SilentlyContinue
        if (-not $codexCommand) {
            throw "codex was not found on PATH."
        }
        & $codexCommand.Source @args
    }
    finally {
        if ($null -eq $oldCodexHome) {
            Remove-Item Env:\CODEX_HOME -ErrorAction SilentlyContinue
        }
        else {
            $env:CODEX_HOME = $oldCodexHome
        }

        [Environment]::SetEnvironmentVariable($Profile.envKeyName, $oldApiKey, "Process")
    }
}

function Start-CodexApiProfile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [string]$Workspace,
        [string[]]$CodexArgs = @(),
        [switch]$InCurrentWindow
    )

    $profile = Get-CodexApiProfile -Id $Id
    Write-ProfileConfig -Profile $profile
    Write-ProfileLauncher -Profile $profile

    if ($InCurrentWindow) {
        return Invoke-CodexApiProfileInCurrentWindow -Profile $profile -Workspace $Workspace -CodexArgs $CodexArgs
    }

    $runner = Get-ModuleRunnerPath
    $shell = Resolve-PowerShellExecutable
    $args = @("-NoExit", "-ExecutionPolicy", "Bypass", "-File", $runner, "-Id", $profile.id)
    if ($Workspace) {
        $args += @("-Workspace", [System.IO.Path]::GetFullPath($Workspace))
    }
    elseif ($profile.workspace) {
        $args += @("-Workspace", [string]$profile.workspace)
    }
    if ($CodexArgs) {
        $args += $CodexArgs
    }

    Start-Process -FilePath $shell -ArgumentList $args | Out-Null
    [pscustomobject]@{
        Id = $profile.id
        Started = $true
        Shell = $shell
        CodexHome = $profile.paths.codexHome
        LauncherPath = $profile.paths.launcherPath
    }
}

function Remove-CodexApiProfile {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [switch]$DeleteFiles
    )

    $state = Read-State
    $profile = Get-ProfileById -State $state -Id $Id
    if (-not $profile) {
        throw "Profile '$Id' was not found."
    }

    if ($PSCmdlet.ShouldProcess($profile.id, "Remove Codex API profile")) {
        $state.profiles = @($state.profiles | Where-Object { -not [string]::Equals($_.id, $profile.id, [StringComparison]::OrdinalIgnoreCase) })
        Write-State -State $state

        $secretPath = Get-ProfileSecretPath -Id $profile.id
        if (Test-Path -LiteralPath $secretPath) {
            Remove-Item -LiteralPath $secretPath -Force
        }
        if ($DeleteFiles -and (Test-Path -LiteralPath $profile.paths.profileHome)) {
            Remove-Item -LiteralPath $profile.paths.profileHome -Recurse -Force
        }

        [pscustomobject]@{
            Id = $profile.id
            Removed = $true
            DeletedFiles = [bool]$DeleteFiles
        }
    }
}

Export-ModuleMember -Function @(
    "Get-CodexApiLauncherRoot",
    "New-CodexApiProfile",
    "Set-CodexApiProfileApiKey",
    "Get-CodexApiProfile",
    "Get-CodexApiProfiles",
    "Test-CodexApiProfile",
    "Start-CodexApiProfile",
    "Remove-CodexApiProfile"
)
Export-ModuleMember -Alias "List-CodexApiProfiles"
