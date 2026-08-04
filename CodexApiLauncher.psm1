Set-StrictMode -Version 2.0

$script:LauncherVersion = "0.4.1"
$script:StateSchemaVersion = 2

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
        throw "无法解析 LOCALAPPDATA。请显式设置 CODEX_API_LAUNCHER_HOME。"
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

function Get-DefaultSharedCodexHome {
    Join-Path (Get-CodexApiLauncherRoot) "codex-home"
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

function Get-ObjectPropertyValue {
    param(
        [AllowNull()]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowNull()]$DefaultValue = $null
    )

    if ($null -eq $Object) {
        return $DefaultValue
    }

    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($Name)) {
            return $Object[$Name]
        }
        return $DefaultValue
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -ne $property) {
        return $property.Value
    }

    return $DefaultValue
}

function Test-ObjectPropertyExists {
    param(
        [AllowNull()]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Object) {
        return $false
    }
    if ($Object -is [System.Collections.IDictionary]) {
        return $Object.Contains($Name)
    }
    return $null -ne $Object.PSObject.Properties[$Name]
}

function Set-ObjectPropertyValue {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowNull()]$Value
    )

    if ($Object -is [System.Collections.IDictionary]) {
        $Object[$Name] = $Value
        return
    }

    if (Test-ObjectPropertyExists -Object $Object -Name $Name) {
        $Object.$Name = $Value
    }
    else {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force
    }
}

function New-EmptyState {
    [pscustomobject][ordered]@{
        version = $script:StateSchemaVersion
        launcherVersion = $script:LauncherVersion
        settings = [pscustomobject][ordered]@{
            sharedCodexHome = Get-DefaultSharedCodexHome
        }
        profiles = @()
    }
}

function Get-SharedCodexHomeFromState {
    param([AllowNull()]$State)

    $settings = Get-ObjectPropertyValue -Object $State -Name "settings" -DefaultValue $null
    $sharedCodexHome = Get-ObjectPropertyValue -Object $settings -Name "sharedCodexHome" -DefaultValue ""
    if ($sharedCodexHome -and [string]$sharedCodexHome -and ([string]$sharedCodexHome).Trim()) {
        return [System.IO.Path]::GetFullPath(([string]$sharedCodexHome).Trim())
    }

    return Get-DefaultSharedCodexHome
}

function Get-ProfileConfigPath {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [string]$SharedCodexHome
    )

    $safeId = ConvertTo-SafeProfileId $Id
    $home = if ($SharedCodexHome -and $SharedCodexHome.Trim()) {
        [System.IO.Path]::GetFullPath($SharedCodexHome.Trim())
    }
    else {
        Get-SharedCodexHomeFromState -State $null
    }
    return (Join-Path $home "$safeId.config.toml")
}

function New-DefaultRuntimeConfig {
    [pscustomobject][ordered]@{
        approvalPolicy = "never"
        sandboxMode = "danger-full-access"
        fullAuto = $true
        goalMode = "inherit"
        webSearch = "inherit"
        remoteCompaction = $false
        strictConfig = $false
        bypassHookTrust = $true
    }
}

function Get-ProfileRuntimeConfig {
    param([Parameter(Mandatory = $true)]$Profile)

    $defaults = New-DefaultRuntimeConfig
    $runtime = Get-ObjectPropertyValue -Object $Profile -Name "runtime" -DefaultValue $null
    if ($null -eq $runtime) {
        return $defaults
    }

    foreach ($property in @($defaults.PSObject.Properties)) {
        $propertyName = $property.Name
        $value = Get-ObjectPropertyValue -Object $runtime -Name $propertyName -DefaultValue (Get-ObjectPropertyValue -Object $defaults -Name $propertyName)
        Set-ObjectPropertyValue -Object $defaults -Name $propertyName -Value $value
    }
    return $defaults
}

function Set-ProfileRuntimeDefaults {
    param([Parameter(Mandatory = $true)]$Profile)

    $runtime = Get-ProfileRuntimeConfig -Profile $Profile
    Set-ObjectPropertyValue -Object $Profile -Name "runtime" -Value $runtime
    return $runtime
}

function Test-RuntimeValue {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowNull()][string]$Value,
        [Parameter(Mandatory = $true)][string[]]$Allowed
    )

    if ($null -eq $Value -or -not $Value.Trim()) {
        return
    }

    if ($Allowed -notcontains $Value) {
        throw "$Name 必须是以下值之一: $($Allowed -join ', ')。"
    }
}

function ConvertTo-BooleanValue {
    param(
        [AllowNull()]$Value,
        [bool]$DefaultValue = $false
    )

    if ($null -eq $Value) {
        return $DefaultValue
    }
    if ($Value -is [bool]) {
        return [bool]$Value
    }
    $text = ([string]$Value).Trim()
    if (-not $text) {
        return $DefaultValue
    }
    return [System.Convert]::ToBoolean($text)
}

function Normalize-State {
    param(
        [Parameter(Mandatory = $true)]$State,
        [switch]$PersistMigration
    )

    $statePath = Get-StatePath
    $oldVersion = Get-ObjectPropertyValue -Object $State -Name "version" -DefaultValue 1
    $needsMigration = ([int]$oldVersion -lt $script:StateSchemaVersion) -or
        (-not (Get-ObjectPropertyValue -Object $State -Name "settings" -DefaultValue $null))

    if ($needsMigration -and $PersistMigration -and (Test-Path -LiteralPath $statePath)) {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        Copy-Item -LiteralPath $statePath -Destination "$statePath.bak-$timestamp" -Force
    }

    Set-ObjectPropertyValue -Object $State -Name "version" -Value $script:StateSchemaVersion
    Set-ObjectPropertyValue -Object $State -Name "launcherVersion" -Value $script:LauncherVersion

    $settings = Get-ObjectPropertyValue -Object $State -Name "settings" -DefaultValue $null
    if ($null -eq $settings) {
        $settings = [pscustomobject][ordered]@{}
        Set-ObjectPropertyValue -Object $State -Name "settings" -Value $settings
    }

    $sharedCodexHome = Get-SharedCodexHomeFromState -State $State
    Set-ObjectPropertyValue -Object $settings -Name "sharedCodexHome" -Value $sharedCodexHome

    if (-not (Get-ObjectPropertyValue -Object $State -Name "profiles" -DefaultValue $null)) {
        Set-ObjectPropertyValue -Object $State -Name "profiles" -Value @()
    }

    foreach ($profile in @($State.profiles)) {
        $id = [string](Get-ObjectPropertyValue -Object $profile -Name "id" -DefaultValue "")
        if (-not $id.Trim()) {
            continue
        }

        $safeId = ConvertTo-SafeProfileId $id
        Set-ObjectPropertyValue -Object $profile -Name "id" -Value $safeId

        $name = [string](Get-ObjectPropertyValue -Object $profile -Name "name" -DefaultValue $safeId)
        Set-ObjectPropertyValue -Object $profile -Name "name" -Value $name
        Set-ObjectPropertyValue -Object $profile -Name "providerName" -Value ([string](Get-ObjectPropertyValue -Object $profile -Name "providerName" -DefaultValue $name))
        Set-ObjectPropertyValue -Object $profile -Name "providerId" -Value (ConvertTo-ProviderId -Id $safeId)
        Set-ObjectPropertyValue -Object $profile -Name "envKeyName" -Value (ConvertTo-EnvKeyName -Id $safeId)

        $paths = Get-ObjectPropertyValue -Object $profile -Name "paths" -DefaultValue $null
        if ($null -eq $paths) {
            $paths = [pscustomobject][ordered]@{}
            Set-ObjectPropertyValue -Object $profile -Name "paths" -Value $paths
        }

        $oldCodexHome = [string](Get-ObjectPropertyValue -Object $paths -Name "codexHome" -DefaultValue "")
        $legacyCodexHome = [string](Get-ObjectPropertyValue -Object $profile -Name "legacyCodexHome" -DefaultValue "")
        if (-not $legacyCodexHome -and $oldCodexHome) {
            $oldFull = [System.IO.Path]::GetFullPath($oldCodexHome)
            $sharedFull = [System.IO.Path]::GetFullPath($sharedCodexHome)
            if (-not [string]::Equals($oldFull, $sharedFull, [StringComparison]::OrdinalIgnoreCase) -and
                (Test-Path -LiteralPath $oldFull -PathType Container)) {
                $legacyCodexHome = $oldFull
            }
        }

        Set-ObjectPropertyValue -Object $profile -Name "legacyCodexHome" -Value $legacyCodexHome
        Set-ObjectPropertyValue -Object $paths -Name "profileHome" -Value (Get-ProfileHome -Id $safeId)
        Set-ObjectPropertyValue -Object $paths -Name "codexHome" -Value $sharedCodexHome
        Set-ObjectPropertyValue -Object $paths -Name "profileConfigPath" -Value (Get-ProfileConfigPath -Id $safeId -SharedCodexHome $sharedCodexHome)
        Set-ObjectPropertyValue -Object $paths -Name "launcherPath" -Value (Join-Path (Get-LaunchersDir) "$safeId.ps1")

        $desktop = Get-ObjectPropertyValue -Object $profile -Name "desktop" -DefaultValue $null
        if ($null -eq $desktop) {
            $desktop = [pscustomobject][ordered]@{}
            Set-ObjectPropertyValue -Object $profile -Name "desktop" -Value $desktop
        }
        Set-ObjectPropertyValue -Object $desktop -Name "reserved" -Value $true
        Set-ObjectPropertyValue -Object $desktop -Name "userDataDir" -Value (Join-Path (Get-ProfileHome -Id $safeId) "desktop-user-data")

        Set-ProfileRuntimeDefaults -Profile $profile | Out-Null
    }

    if ($needsMigration -and $PersistMigration) {
        Ensure-Directory $sharedCodexHome
        foreach ($profile in @($State.profiles)) {
            Write-ProfileConfig -Profile $profile
            Write-ProfileLauncher -Profile $profile
        }
        Write-State -State $State
    }

    return $State
}

function Read-State {
    $statePath = Get-StatePath
    $state = Read-JsonFile -Path $statePath -DefaultValue (New-EmptyState)
    return (Normalize-State -State $state -PersistMigration)
}

function Write-State {
    param([Parameter(Mandatory = $true)]$State)
    Normalize-State -State $State | Out-Null
    $json = $State | ConvertTo-Json -Depth 12
    Write-Utf8File -Path (Get-StatePath) -Content ($json + [Environment]::NewLine)
}

function ConvertTo-SafeProfileId {
    param([Parameter(Mandatory = $true)][string]$Id)

    $safe = $Id.Trim().ToLowerInvariant() -replace "[^a-z0-9_-]", "-"
    $safe = $safe.Trim("-_")
    if (-not $safe) {
        throw "供应商 ID 至少需要包含一个字母或数字。"
    }
    if ($safe.Length -gt 40) {
        $safe = $safe.Substring(0, 40).Trim("-_")
    }
    if ($safe -notmatch "^[a-z0-9][a-z0-9_-]*$") {
        throw "供应商 ID '$Id' 规范化后仍然无效。"
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
        throw "BaseUrl 必须是完整的 http 或 https URL。"
    }
    if ($uri.Scheme -ne "http" -and $uri.Scheme -ne "https") {
        throw "BaseUrl 必须使用 http 或 https。"
    }
    return $uri.AbsoluteUri.TrimEnd("/")
}

function Get-ProfileHome {
    param([Parameter(Mandatory = $true)][string]$Id)
    Join-Path (Join-Path (Get-CodexApiLauncherRoot) "profiles") $Id
}

function Get-ProfileCodexHome {
    param([Parameter(Mandatory = $true)][string]$Id)
    Get-SharedCodexHomeFromState -State (Read-State)
}

function Resolve-CodexHomePath {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [string]$CodexHome
    )

    if ($CodexHome -and $CodexHome.Trim()) {
        return [System.IO.Path]::GetFullPath($CodexHome.Trim())
    }

    return Join-Path (Get-ProfileHome -Id $Id) "codex-home"
}

function Get-CodexApiProfileConfigPath {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Id)

    $state = Read-State
    return (Get-ProfileConfigPath -Id $Id -SharedCodexHome (Get-SharedCodexHomeFromState -State $state))
}

function Get-CodexApiLauncherSettings {
    [CmdletBinding()]
    param()

    $state = Read-State
    $sharedCodexHome = Get-SharedCodexHomeFromState -State $state
    [pscustomobject]@{
        Version = $state.version
        LauncherVersion = $state.launcherVersion
        Root = Get-CodexApiLauncherRoot
        SharedCodexHome = $sharedCodexHome
        ProfilesPath = Get-StatePath
        SecretsDir = Get-SecretsDir
        LaunchersDir = Get-LaunchersDir
    }
}

function Set-CodexApiLauncherSharedHome {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$SharedCodexHome)

    if (-not $SharedCodexHome.Trim()) {
        throw "共享 CODEX_HOME 不能为空。"
    }

    $state = Read-State
    $home = [System.IO.Path]::GetFullPath($SharedCodexHome.Trim())
    Set-ObjectPropertyValue -Object $state.settings -Name "sharedCodexHome" -Value $home
    Ensure-Directory $home
    foreach ($profile in @($state.profiles)) {
        Set-ObjectPropertyValue -Object $profile.paths -Name "codexHome" -Value $home
        Set-ObjectPropertyValue -Object $profile.paths -Name "profileConfigPath" -Value (Get-ProfileConfigPath -Id $profile.id -SharedCodexHome $home)
        Write-ProfileConfig -Profile $profile
        Write-ProfileLauncher -Profile $profile
    }
    Write-State -State $state

    Get-CodexApiLauncherSettings
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
        throw "Profile '$($Profile.id)' 还没有保存 API Key。请运行 Set-CodexApiProfileApiKey -Id '$($Profile.id)' -ApiKey (Read-Host -AsSecureString 'API Key')。"
    }

    $secret = Read-JsonFile -Path $secretPath -DefaultValue $null
    if ($null -eq $secret -or -not $secret.protectedApiKey) {
        throw "已保存的 API Key 文件缺失或无效: $secretPath"
    }

    $secure = ConvertTo-SecureString $secret.protectedApiKey
    return ConvertFrom-SecureStringToPlainText -SecureString $secure
}

function Get-ConfigText {
    param([Parameter(Mandatory = $true)]$Profile)

    $runtime = Get-ProfileRuntimeConfig -Profile $Profile
    $providerName = if (ConvertTo-BooleanValue (Get-ObjectPropertyValue -Object $runtime -Name "remoteCompaction" -DefaultValue $false)) {
        "OpenAI"
    }
    else {
        [string]$Profile.providerName
    }

    $lines = @(
        "model_provider = $(ConvertTo-TomlString $Profile.providerId)"
        "model = $(ConvertTo-TomlString $Profile.model)"
        "model_reasoning_effort = $(ConvertTo-TomlString $Profile.reasoningEffort)"
        ""
        "[model_providers.$($Profile.providerId)]"
        "name = $(ConvertTo-TomlString $providerName)"
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

    $sharedCodexHome = [string]$Profile.paths.codexHome
    if (-not $sharedCodexHome.Trim()) {
        $sharedCodexHome = Get-SharedCodexHomeFromState -State (Read-State)
    }
    Ensure-Directory $sharedCodexHome
    $configPath = Get-ProfileConfigPath -Id $Profile.id -SharedCodexHome $sharedCodexHome
    Set-ObjectPropertyValue -Object $Profile.paths -Name "profileConfigPath" -Value $configPath
    Write-Utf8File -Path $configPath -Content (Get-ConfigText -Profile $Profile)
}

function Get-CodexRuntimeArgs {
    param([Parameter(Mandatory = $true)]$Profile)

    $runtime = Get-ProfileRuntimeConfig -Profile $Profile
    $args = @()

    $fullAuto = ConvertTo-BooleanValue (Get-ObjectPropertyValue -Object $runtime -Name "fullAuto" -DefaultValue $true)
    if ($fullAuto) {
        $args += "--dangerously-bypass-approvals-and-sandbox"
    }
    else {
        $approvalPolicy = [string](Get-ObjectPropertyValue -Object $runtime -Name "approvalPolicy" -DefaultValue "inherit")
        if ($approvalPolicy -and $approvalPolicy -ne "inherit") {
            $args += @("--ask-for-approval", $approvalPolicy)
        }

        $sandboxMode = [string](Get-ObjectPropertyValue -Object $runtime -Name "sandboxMode" -DefaultValue "inherit")
        if ($sandboxMode -and $sandboxMode -ne "inherit") {
            $args += @("--sandbox", $sandboxMode)
        }
    }

    $goalMode = [string](Get-ObjectPropertyValue -Object $runtime -Name "goalMode" -DefaultValue "inherit")
    if ($goalMode -eq "enabled") {
        $args += @("--enable", "goals")
    }
    elseif ($goalMode -eq "disabled") {
        $args += @("--disable", "goals")
    }

    $webSearch = [string](Get-ObjectPropertyValue -Object $runtime -Name "webSearch" -DefaultValue "inherit")
    if ($webSearch -eq "enabled") {
        $args += "--search"
    }
    elseif ($webSearch -eq "disabled") {
        $args += @("-c", 'web_search="disabled"')
    }

    if (ConvertTo-BooleanValue (Get-ObjectPropertyValue -Object $runtime -Name "strictConfig" -DefaultValue $false)) {
        $args += "--strict-config"
    }

    if ($fullAuto -or (ConvertTo-BooleanValue (Get-ObjectPropertyValue -Object $runtime -Name "bypassHookTrust" -DefaultValue $true))) {
        $args += "--dangerously-bypass-hook-trust"
    }

    return $args
}

function Test-CodexSubcommandAcceptsRuntimeArgs {
    param([string]$Command)

    return $Command -in @("exec", "review", "resume", "fork")
}

function Join-CodexInvocationArgs {
    param(
        [string[]]$BaseArgs = @(),
        [string[]]$RuntimeArgs = @(),
        [string[]]$CodexArgs = @()
    )

    $args = @()
    if ($BaseArgs) {
        $args += $BaseArgs
    }

    if ($CodexArgs -and $CodexArgs.Count -gt 0 -and (Test-CodexSubcommandAcceptsRuntimeArgs -Command $CodexArgs[0])) {
        $args += $CodexArgs[0]
        if ($RuntimeArgs) {
            $args += $RuntimeArgs
        }
        if ($CodexArgs.Count -gt 1) {
            $args += $CodexArgs[1..($CodexArgs.Count - 1)]
        }
        return $args
    }

    if ($RuntimeArgs) {
        $args += $RuntimeArgs
    }
    if ($CodexArgs) {
        $args += $CodexArgs
    }
    return $args
}

function ConvertTo-CodexApiProfileInfo {
    param([Parameter(Mandatory = $true)]$Profile)

    $runtime = Get-ProfileRuntimeConfig -Profile $Profile
    $sharedCodexHome = [string]$Profile.paths.codexHome
    if (-not $sharedCodexHome.Trim()) {
        $sharedCodexHome = Get-SharedCodexHomeFromState -State (Read-State)
    }
    $configPath = Get-ProfileConfigPath -Id $Profile.id -SharedCodexHome $sharedCodexHome

    [pscustomobject]@{
        Id = $Profile.id
        Name = $Profile.name
        BaseUrl = $Profile.baseUrl
        Model = $Profile.model
        ReasoningEffort = $Profile.reasoningEffort
        EnvKeyName = $Profile.envKeyName
        Workspace = $Profile.workspace
        CodexHome = $sharedCodexHome
        SharedCodexHome = $sharedCodexHome
        LegacyCodexHome = (Get-ObjectPropertyValue -Object $Profile -Name "legacyCodexHome" -DefaultValue "")
        ProfileConfigPath = $configPath
        ConfigPath = $configPath
        LauncherPath = $Profile.paths.launcherPath
        ApprovalPolicy = $runtime.approvalPolicy
        SandboxMode = $runtime.sandboxMode
        FullAuto = [bool](ConvertTo-BooleanValue $runtime.fullAuto)
        GoalMode = $runtime.goalMode
        WebSearch = $runtime.webSearch
        RemoteCompaction = [bool](ConvertTo-BooleanValue $runtime.remoteCompaction)
        StrictConfig = [bool](ConvertTo-BooleanValue $runtime.strictConfig)
        BypassHookTrust = [bool](ConvertTo-BooleanValue $runtime.bypassHookTrust)
        HasApiKey = [bool](Test-Path -LiteralPath (Get-ProfileSecretPath -Id $Profile.id))
    }
}

function Get-NormalizedDirectoryPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetFullPath($Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    )
}

function Move-DirectoryToDestination {
    param(
        [string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not $Source -or -not (Test-Path -LiteralPath $Source -PathType Container)) {
        Ensure-Directory $Destination
        return
    }

    $sourceFull = Get-NormalizedDirectoryPath $Source
    $destinationFull = Get-NormalizedDirectoryPath $Destination
    if ([string]::Equals($sourceFull, $destinationFull, [StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    if ($destinationFull.StartsWith($sourceFull + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        $destinationFull.StartsWith($sourceFull + [System.IO.Path]::AltDirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "目标目录不能位于当前目录内部: $destinationFull"
    }

    $destinationParent = Split-Path -Parent $destinationFull
    if ($destinationParent) {
        Ensure-Directory $destinationParent
    }

    if (-not (Test-Path -LiteralPath $destinationFull)) {
        Move-Item -LiteralPath $sourceFull -Destination $destinationFull -Force
        return
    }

    if (-not (Test-Path -LiteralPath $destinationFull -PathType Container)) {
        throw "目标路径已存在但不是目录: $destinationFull"
    }

    foreach ($item in Get-ChildItem -LiteralPath $sourceFull -Force) {
        $targetPath = Join-Path $destinationFull $item.Name
        if (Test-Path -LiteralPath $targetPath) {
            throw "目标目录已存在同名项目，不能安全迁移: $targetPath"
        }
        Move-Item -LiteralPath $item.FullName -Destination $destinationFull -Force
    }

    Remove-Item -LiteralPath $sourceFull -Force
}

function Get-ModuleRunnerPath {
    Join-Path $PSScriptRoot "Run-CodexApiProfile.ps1"
}

function Write-ProfileLauncher {
    param([Parameter(Mandatory = $true)]$Profile)

    Ensure-Directory (Get-LaunchersDir)
    $runner = Get-ModuleRunnerPath
    $workspace = [string]$Profile.workspace
    $workspaceArg = if ($workspace) { "-Workspace $(ConvertTo-TomlString $workspace) " } else { "" }
    $content = @"
param(
    [Parameter(ValueFromRemainingArguments = `$true)]
    [string[]]`$CodexArgs
)

`$runner = $(ConvertTo-TomlString $runner)
& `$runner -Id $(ConvertTo-TomlString $Profile.id) $workspaceArg@CodexArgs
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
        [string]$Workspace = "",
        [string]$CodexHome = "",
        [ValidateSet("low", "medium", "high", "xhigh", "max", "ultra")][string]$ReasoningEffort = "medium",
        [ValidateSet("inherit", "untrusted", "on-request", "never")][string]$ApprovalPolicy = "never",
        [ValidateSet("inherit", "read-only", "workspace-write", "danger-full-access")][string]$SandboxMode = "danger-full-access",
        [bool]$FullAuto = $true,
        [ValidateSet("inherit", "enabled", "disabled")][string]$GoalMode = "inherit",
        [ValidateSet("inherit", "enabled", "disabled")][string]$WebSearch = "inherit",
        [bool]$RemoteCompaction = $false,
        [bool]$StrictConfig = $false,
        [bool]$BypassHookTrust = $true,
        [securestring]$ApiKey,
        [switch]$Force
    )

    $safeId = ConvertTo-SafeProfileId $Id
    $normalizedBaseUrl = Test-BaseUrl $BaseUrl
    $state = Read-State
    $existing = Get-ProfileById -State $state -Id $safeId
    if ($existing -and -not $Force) {
        throw "Profile '$safeId' 已存在。请使用 -Force 覆盖。"
    }

    $now = (Get-Date).ToUniversalTime().ToString("o")
    $sharedCodexHome = Get-SharedCodexHomeFromState -State $state
    $legacyCodexHome = if ($CodexHome -and $CodexHome.Trim()) {
        [System.IO.Path]::GetFullPath($CodexHome.Trim())
    }
    elseif ($existing) {
        [string](Get-ObjectPropertyValue -Object $existing -Name "legacyCodexHome" -DefaultValue "")
    }
    else {
        ""
    }
    $launcherPath = Join-Path (Get-LaunchersDir) "$safeId.ps1"
    $providerId = ConvertTo-ProviderId -Id $safeId
    $envKeyName = ConvertTo-EnvKeyName -Id $safeId

    $profile = [pscustomobject][ordered]@{
        id = $safeId
        name = $Name
        providerName = $Name
        providerId = $providerId
        baseUrl = $normalizedBaseUrl
        model = $Model
        reasoningEffort = $ReasoningEffort
        envKeyName = $envKeyName
        workspace = if ($Workspace) { [System.IO.Path]::GetFullPath($Workspace) } else { $null }
        legacyCodexHome = $legacyCodexHome
        createdAt = if ($existing) { (Get-ObjectPropertyValue -Object $existing -Name "createdAt" -DefaultValue $now) } else { $now }
        updatedAt = $now
        runtime = [pscustomobject][ordered]@{
            approvalPolicy = $ApprovalPolicy
            sandboxMode = $SandboxMode
            fullAuto = $FullAuto
            goalMode = $GoalMode
            webSearch = $WebSearch
            remoteCompaction = $RemoteCompaction
            strictConfig = $StrictConfig
            bypassHookTrust = $BypassHookTrust
        }
        paths = [pscustomobject][ordered]@{
            profileHome = Get-ProfileHome -Id $safeId
            codexHome = $sharedCodexHome
            profileConfigPath = Get-ProfileConfigPath -Id $safeId -SharedCodexHome $sharedCodexHome
            launcherPath = $launcherPath
        }
        desktop = [pscustomobject][ordered]@{
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

    ConvertTo-CodexApiProfileInfo -Profile $profile
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
        throw "没有找到 Profile '$Id'。"
    }
    Save-ProfileSecret -Profile $profile -ApiKey $ApiKey

    [pscustomobject]@{
        Id = $profile.id
        EnvKeyName = $profile.envKeyName
        SecretPath = Get-ProfileSecretPath -Id $profile.id
        HasApiKey = $true
    }
}

function Set-CodexApiProfileWorkspace {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [string]$Workspace,
        [switch]$Clear
    )

    $state = Read-State
    $profile = Get-ProfileById -State $state -Id $Id
    if (-not $profile) {
        throw "没有找到 Profile '$Id'。"
    }

    if ($Clear) {
        $profile.workspace = $null
    }
    else {
        if (-not $Workspace) {
            throw "请提供 -Workspace，或使用 -Clear。"
        }
        if (-not (Test-Path -LiteralPath $Workspace -PathType Container)) {
            throw "项目文件夹不存在: $Workspace"
        }
        $profile.workspace = [System.IO.Path]::GetFullPath($Workspace)
    }

    $profile.updatedAt = (Get-Date).ToUniversalTime().ToString("o")
    Write-State -State $state
    Write-ProfileLauncher -Profile $profile

    [pscustomobject]@{
        Id = $profile.id
        Workspace = $profile.workspace
        LauncherPath = $profile.paths.launcherPath
    }
}

function Set-CodexApiProfileName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if (-not $Name.Trim()) {
        throw "配置名称不能为空。"
    }

    $state = Read-State
    $profile = Get-ProfileById -State $state -Id $Id
    if (-not $profile) {
        throw "没有找到 Profile '$Id'。"
    }

    $profile.name = $Name.Trim()
    $profile.providerName = $Name.Trim()
    $profile.updatedAt = (Get-Date).ToUniversalTime().ToString("o")
    Write-State -State $state
    Write-ProfileConfig -Profile $profile
    Write-ProfileLauncher -Profile $profile

    ConvertTo-CodexApiProfileInfo -Profile $profile
}

function Set-CodexApiProfileCodexHome {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$CodexHome
    )

    if (-not $CodexHome.Trim()) {
        throw "配置存放目录不能为空。"
    }

    $state = Read-State
    $profile = Get-ProfileById -State $state -Id $Id
    if (-not $profile) {
        throw "没有找到 Profile '$Id'。"
    }

    $newCodexHome = [System.IO.Path]::GetFullPath($CodexHome.Trim())
    Set-ObjectPropertyValue -Object $profile -Name "legacyCodexHome" -Value $newCodexHome
    $profile.updatedAt = (Get-Date).ToUniversalTime().ToString("o")
    Write-State -State $state
    Write-ProfileConfig -Profile $profile
    Write-ProfileLauncher -Profile $profile

    Write-Warning "Set-CodexApiProfileCodexHome 现在只记录 legacyCodexHome；实际启动统一使用共享 CODEX_HOME。请使用 Set-CodexApiLauncherSharedHome 修改共享目录。"
    ConvertTo-CodexApiProfileInfo -Profile $profile
}

function Set-CodexApiProfile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [string]$NewId,
        [string]$Name,
        [string]$BaseUrl,
        [string]$Model,
        [AllowEmptyString()][string]$Workspace,
        [string]$CodexHome,
        [ValidateSet("low", "medium", "high", "xhigh", "max", "ultra")][string]$ReasoningEffort,
        [ValidateSet("inherit", "untrusted", "on-request", "never")][string]$ApprovalPolicy,
        [ValidateSet("inherit", "read-only", "workspace-write", "danger-full-access")][string]$SandboxMode,
        [bool]$FullAuto,
        [ValidateSet("inherit", "enabled", "disabled")][string]$GoalMode,
        [ValidateSet("inherit", "enabled", "disabled")][string]$WebSearch,
        [bool]$RemoteCompaction,
        [bool]$StrictConfig,
        [bool]$BypassHookTrust
    )

    $state = Read-State
    $profile = Get-ProfileById -State $state -Id $Id
    if (-not $profile) {
        throw "没有找到 Profile '$Id'。"
    }

    $oldId = [string]$profile.id
    $targetId = if ($PSBoundParameters.ContainsKey("NewId") -and $NewId.Trim()) {
        ConvertTo-SafeProfileId $NewId
    }
    else {
        $oldId
    }

    if (-not [string]::Equals($oldId, $targetId, [StringComparison]::OrdinalIgnoreCase)) {
        $existing = Get-ProfileById -State $state -Id $targetId
        if ($existing) {
            throw "供应商 ID '$targetId' 已存在。"
        }
    }

    if ($PSBoundParameters.ContainsKey("Name")) {
        if (-not $Name.Trim()) {
            throw "供应商名称不能为空。"
        }
        $profile.name = $Name.Trim()
        $profile.providerName = $Name.Trim()
    }

    if ($PSBoundParameters.ContainsKey("BaseUrl")) {
        if (-not $BaseUrl.Trim()) {
            throw "中转地址不能为空。"
        }
        $profile.baseUrl = Test-BaseUrl $BaseUrl
    }

    if ($PSBoundParameters.ContainsKey("Model")) {
        if (-not $Model.Trim()) {
            throw "模型不能为空。"
        }
        $profile.model = $Model.Trim()
    }

    if ($PSBoundParameters.ContainsKey("ReasoningEffort")) {
        $profile.reasoningEffort = $ReasoningEffort
    }

    $runtime = Set-ProfileRuntimeDefaults -Profile $profile
    if ($PSBoundParameters.ContainsKey("ApprovalPolicy")) {
        $runtime.approvalPolicy = $ApprovalPolicy
    }
    if ($PSBoundParameters.ContainsKey("SandboxMode")) {
        $runtime.sandboxMode = $SandboxMode
    }
    if ($PSBoundParameters.ContainsKey("FullAuto")) {
        $runtime.fullAuto = $FullAuto
    }
    if ($PSBoundParameters.ContainsKey("GoalMode")) {
        $runtime.goalMode = $GoalMode
    }
    if ($PSBoundParameters.ContainsKey("WebSearch")) {
        $runtime.webSearch = $WebSearch
    }
    if ($PSBoundParameters.ContainsKey("RemoteCompaction")) {
        $runtime.remoteCompaction = $RemoteCompaction
    }
    if ($PSBoundParameters.ContainsKey("StrictConfig")) {
        $runtime.strictConfig = $StrictConfig
    }
    if ($PSBoundParameters.ContainsKey("BypassHookTrust")) {
        $runtime.bypassHookTrust = $BypassHookTrust
    }

    if ($PSBoundParameters.ContainsKey("Workspace")) {
        if ($Workspace -and $Workspace.Trim()) {
            if (-not (Test-Path -LiteralPath $Workspace -PathType Container)) {
                throw "项目文件夹不存在: $Workspace"
            }
            $profile.workspace = [System.IO.Path]::GetFullPath($Workspace)
        }
        else {
            $profile.workspace = $null
        }
    }

    if ($PSBoundParameters.ContainsKey("CodexHome")) {
        if (-not $CodexHome.Trim()) {
            throw "配置存放目录不能为空。"
        }
        Set-ObjectPropertyValue -Object $profile -Name "legacyCodexHome" -Value ([System.IO.Path]::GetFullPath($CodexHome.Trim()))
    }

    $oldProfileHome = [string]$profile.paths.profileHome
    $oldLauncherPath = [string]$profile.paths.launcherPath
    $idChanged = -not [string]::Equals($oldId, $targetId, [StringComparison]::OrdinalIgnoreCase)
    $sharedCodexHome = Get-SharedCodexHomeFromState -State $state
    $oldConfigPath = Get-ProfileConfigPath -Id $oldId -SharedCodexHome $sharedCodexHome

    $targetProfileHome = Get-ProfileHome -Id $targetId

    if ($idChanged) {
        $profile.id = $targetId
        $profile.providerId = ConvertTo-ProviderId -Id $targetId
        $profile.envKeyName = ConvertTo-EnvKeyName -Id $targetId
        $profile.paths.profileHome = $targetProfileHome
        $profile.paths.codexHome = $sharedCodexHome
        Set-ObjectPropertyValue -Object $profile.paths -Name "profileConfigPath" -Value (Get-ProfileConfigPath -Id $targetId -SharedCodexHome $sharedCodexHome)
        $profile.paths.launcherPath = Join-Path (Get-LaunchersDir) "$targetId.ps1"

        if (Test-ObjectPropertyExists -Object $profile -Name "desktop" -and $null -ne $profile.desktop) {
            $profile.desktop.userDataDir = Join-Path (Get-ProfileHome -Id $targetId) "desktop-user-data"
        }

        $oldSecretPath = Get-ProfileSecretPath -Id $oldId
        $newSecretPath = Get-ProfileSecretPath -Id $targetId
        if (Test-Path -LiteralPath $oldSecretPath) {
            if ((Test-Path -LiteralPath $newSecretPath) -and -not [string]::Equals($oldSecretPath, $newSecretPath, [StringComparison]::OrdinalIgnoreCase)) {
                throw "目标供应商 ID '$targetId' 已有密钥文件。请先清理该 ID 的旧密钥。"
            }

            $secret = Read-JsonFile -Path $oldSecretPath -DefaultValue $null
            if ($null -ne $secret) {
                $secret.profileId = $targetId
                $secret.envKeyName = $profile.envKeyName
                $secret.updatedAt = (Get-Date).ToUniversalTime().ToString("o")
                Write-Utf8File -Path $newSecretPath -Content (($secret | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
                if (-not [string]::Equals($oldSecretPath, $newSecretPath, [StringComparison]::OrdinalIgnoreCase)) {
                    Remove-Item -LiteralPath $oldSecretPath -Force
                }
            }
        }
    }

    $profile.paths.codexHome = $sharedCodexHome
    Set-ObjectPropertyValue -Object $profile.paths -Name "profileConfigPath" -Value (Get-ProfileConfigPath -Id $profile.id -SharedCodexHome $sharedCodexHome)

    if ($idChanged -and $oldProfileHome -and (Test-Path -LiteralPath $oldProfileHome -PathType Container)) {
        Move-DirectoryToDestination -Source $oldProfileHome -Destination $profile.paths.profileHome
    }

    $profile.updatedAt = (Get-Date).ToUniversalTime().ToString("o")
    Ensure-Directory $profile.paths.profileHome
    Write-ProfileConfig -Profile $profile
    Write-ProfileLauncher -Profile $profile

    if ($idChanged -and $oldLauncherPath -and (Test-Path -LiteralPath $oldLauncherPath) -and -not [string]::Equals($oldLauncherPath, $profile.paths.launcherPath, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $oldLauncherPath -Force
    }
    if ($idChanged -and $oldConfigPath -and (Test-Path -LiteralPath $oldConfigPath) -and -not [string]::Equals($oldConfigPath, $profile.paths.profileConfigPath, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $oldConfigPath -Force
    }

    $profiles = @()
    foreach ($item in @($state.profiles)) {
        $isCurrentProfile = [object]::ReferenceEquals($item, $profile)
        $isOldId = [string]::Equals($item.id, $oldId, [StringComparison]::OrdinalIgnoreCase)
        $isTargetId = [string]::Equals($item.id, $profile.id, [StringComparison]::OrdinalIgnoreCase)
        if (-not $isCurrentProfile -and -not $isOldId -and -not $isTargetId) {
            $profiles += $item
        }
    }
    $profiles += $profile
    $state.profiles = @($profiles | Sort-Object id)
    Write-State -State $state

    ConvertTo-CodexApiProfileInfo -Profile $profile
}

function Get-CodexApiProfile {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Id)

    $state = Read-State
    $profile = Get-ProfileById -State $state -Id $Id
    if (-not $profile) {
        throw "没有找到 Profile '$Id'。"
    }
    return $profile
}

function Get-CodexApiProfiles {
    [CmdletBinding()]
    param()

    $state = Read-State
    foreach ($profile in @($state.profiles)) {
        ConvertTo-CodexApiProfileInfo -Profile $profile
    }
}

Set-Alias -Name List-CodexApiProfiles -Value Get-CodexApiProfiles

function Set-CodexApiProfileRuntime {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [ValidateSet("inherit", "untrusted", "on-request", "never")][string]$ApprovalPolicy,
        [ValidateSet("inherit", "read-only", "workspace-write", "danger-full-access")][string]$SandboxMode,
        [bool]$FullAuto,
        [ValidateSet("inherit", "enabled", "disabled")][string]$GoalMode,
        [ValidateSet("inherit", "enabled", "disabled")][string]$WebSearch,
        [bool]$RemoteCompaction,
        [bool]$StrictConfig,
        [bool]$BypassHookTrust
    )

    $state = Read-State
    $profile = Get-ProfileById -State $state -Id $Id
    if (-not $profile) {
        throw "没有找到 Profile '$Id'。"
    }

    $runtime = Set-ProfileRuntimeDefaults -Profile $profile
    if ($PSBoundParameters.ContainsKey("ApprovalPolicy")) {
        $runtime.approvalPolicy = $ApprovalPolicy
    }
    if ($PSBoundParameters.ContainsKey("SandboxMode")) {
        $runtime.sandboxMode = $SandboxMode
    }
    if ($PSBoundParameters.ContainsKey("FullAuto")) {
        $runtime.fullAuto = $FullAuto
    }
    if ($PSBoundParameters.ContainsKey("GoalMode")) {
        $runtime.goalMode = $GoalMode
    }
    if ($PSBoundParameters.ContainsKey("WebSearch")) {
        $runtime.webSearch = $WebSearch
    }
    if ($PSBoundParameters.ContainsKey("RemoteCompaction")) {
        $runtime.remoteCompaction = $RemoteCompaction
    }
    if ($PSBoundParameters.ContainsKey("StrictConfig")) {
        $runtime.strictConfig = $StrictConfig
    }
    if ($PSBoundParameters.ContainsKey("BypassHookTrust")) {
        $runtime.bypassHookTrust = $BypassHookTrust
    }

    $profile.updatedAt = (Get-Date).ToUniversalTime().ToString("o")
    Write-State -State $state
    Write-ProfileConfig -Profile $profile
    Write-ProfileLauncher -Profile $profile
    ConvertTo-CodexApiProfileInfo -Profile $profile
}

function Invoke-CodexApiLauncherMigration {
    [CmdletBinding()]
    param([switch]$DryRun)

    $statePath = Get-StatePath
    $before = Read-JsonFile -Path $statePath -DefaultValue (New-EmptyState)
    $beforeVersion = Get-ObjectPropertyValue -Object $before -Name "version" -DefaultValue 1
    $profileCount = @((Get-ObjectPropertyValue -Object $before -Name "profiles" -DefaultValue @())).Count
    $state = Normalize-State -State $before
    $sharedCodexHome = Get-SharedCodexHomeFromState -State $state

    $legacyHomes = @()
    foreach ($profile in @($state.profiles)) {
        $legacy = [string](Get-ObjectPropertyValue -Object $profile -Name "legacyCodexHome" -DefaultValue "")
        if ($legacy) {
            $legacyHomes += $legacy
        }
    }

    if (-not $DryRun) {
        Ensure-Directory $sharedCodexHome
        foreach ($profile in @($state.profiles)) {
            Write-ProfileConfig -Profile $profile
            Write-ProfileLauncher -Profile $profile
        }
        Write-State -State $state
    }

    [pscustomobject]@{
        DryRun = [bool]$DryRun
        WasVersion = $beforeVersion
        Version = $script:StateSchemaVersion
        ProfileCount = $profileCount
        SharedCodexHome = $sharedCodexHome
        LegacyHomeCount = @($legacyHomes).Count
        LegacyHomes = @($legacyHomes | Sort-Object -Unique)
        StatePath = $statePath
    }
}

function Get-CodexApiLegacyHomes {
    [CmdletBinding()]
    param()

    $state = Read-State
    foreach ($profile in @($state.profiles)) {
        $legacy = [string](Get-ObjectPropertyValue -Object $profile -Name "legacyCodexHome" -DefaultValue "")
        if ($legacy) {
            [pscustomobject]@{
                Id = $profile.id
                Name = $profile.name
                LegacyCodexHome = $legacy
                Exists = [bool](Test-Path -LiteralPath $legacy -PathType Container)
                SharedCodexHome = Get-SharedCodexHomeFromState -State $state
            }
        }
    }
}

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
            $details.Add("Provider 在 /models 拒绝了这个 API Key。")
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
        if (Test-ObjectPropertyExists -Object $payload -Name "data") {
            $modelCount = @($payload.data).Count
        }
        elseif (Test-ObjectPropertyExists -Object $payload -Name "models") {
            $modelCount = @($payload.models).Count
        }
        }
        catch {
            $details.Add("无法将 /models 响应解析为 JSON。")
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
            Details = "Provider 接受了最小 /responses 请求。"
        }
    }

    $status = "responses_failed"
    if ($responsesResult.StatusCode -eq 401) {
        $status = "auth_failed"
    }
    elseif ($responsesResult.StatusCode -eq 403) {
        $status = if ($modelsResult.StatusCode -ge 200 -and $modelsResult.StatusCode -lt 300) { "responses_forbidden" } else { "auth_failed" }
    }
    elseif ($responsesResult.StatusCode -eq 404 -or $responsesResult.StatusCode -eq 405) {
        $status = "responses_unsupported"
    }
    elseif ($responsesResult.StatusCode -eq 502 -or $responsesResult.StatusCode -eq 503 -or $responsesResult.StatusCode -eq 504) {
        $status = "provider_unavailable"
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

function Get-CodexApiProfileModels {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [int]$TimeoutSeconds = 30
    )

    $profile = Get-CodexApiProfile -Id $Id
    $apiKey = Read-ProfileApiKey -Profile $profile
    $modelsUrl = Join-ProviderEndpoint -BaseUrl $profile.baseUrl -Suffix "/models"
    $modelsResult = Invoke-ProviderHttp -Method GET -Url $modelsUrl -ApiKey $apiKey -TimeoutSeconds $TimeoutSeconds

    if (-not $modelsResult.IsSuccessStatusCode) {
        throw "/models 请求失败，HTTP $($modelsResult.StatusCode): $($modelsResult.Body)"
    }

    $payload = $modelsResult.Body | ConvertFrom-Json
    $items = @()
    if (Test-ObjectPropertyExists -Object $payload -Name "data") {
        $items = @($payload.data)
    }
    elseif (Test-ObjectPropertyExists -Object $payload -Name "models") {
        $items = @($payload.models)
    }

    $models = New-Object System.Collections.Generic.List[string]
    foreach ($item in $items) {
        $modelId = $null
        if ($item -is [string]) {
            $modelId = $item
        }
        elseif (Test-ObjectPropertyExists -Object $item -Name "id") {
            $modelId = [string]$item.id
        }
        elseif (Test-ObjectPropertyExists -Object $item -Name "model") {
            $modelId = [string]$item.model
        }
        elseif (Test-ObjectPropertyExists -Object $item -Name "name") {
            $modelId = [string]$item.name
        }

        if (-not [string]::IsNullOrWhiteSpace($modelId) -and -not $models.Contains($modelId)) {
            $models.Add($modelId)
        }
    }

    $models | Sort-Object
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
    throw "没有找到 pwsh.exe 或 powershell.exe。"
}

function Resolve-WindowsTerminalExecutable {
    $cmd = Get-Command wt.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }
    return $null
}

function Start-DetachedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.UseShellExecute = $false
    foreach ($argument in $ArgumentList) {
        [void]$psi.ArgumentList.Add($argument)
    }
    [System.Diagnostics.Process]::Start($psi) | Out-Null
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
        $sharedCodexHome = [string]$Profile.paths.codexHome
        Ensure-Directory $sharedCodexHome
        Write-ProfileConfig -Profile $Profile
        $env:CODEX_HOME = $sharedCodexHome
        [Environment]::SetEnvironmentVariable($Profile.envKeyName, $apiKey, "Process")

        $workspaceToUse = if ($Workspace) {
            [System.IO.Path]::GetFullPath($Workspace)
        }
        elseif ($Profile.workspace) {
            [string]$Profile.workspace
        }
        else {
            (Get-Location).Path
        }
        $baseArgs = @("--profile", [string]$Profile.id, "-C", $workspaceToUse)
        $runtimeArgs = @(Get-CodexRuntimeArgs -Profile $Profile)
        $args = @(Join-CodexInvocationArgs -BaseArgs $baseArgs -RuntimeArgs $runtimeArgs -CodexArgs $CodexArgs)

        $codexCommand = Get-Command codex -ErrorAction SilentlyContinue
        if (-not $codexCommand) {
            throw "PATH 中没有找到 codex 命令。"
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

    $terminal = Resolve-WindowsTerminalExecutable
    if ($terminal) {
        $title = "Codex $($profile.id)"
        $terminalArgs = @("new-tab", "--title", $title, $shell) + $args
        Start-DetachedProcess -FilePath $terminal -ArgumentList $terminalArgs
        $shellDisplay = "Windows Terminal"
    }
    else {
        Start-DetachedProcess -FilePath $shell -ArgumentList $args
        $shellDisplay = $shell
    }

    [pscustomobject]@{
        Id = $profile.id
        Started = $true
        Shell = $shellDisplay
        CodexHome = $profile.paths.codexHome
        SharedCodexHome = $profile.paths.codexHome
        ProfileConfigPath = $profile.paths.profileConfigPath
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
        throw "没有找到 Profile '$Id'。"
    }

    if ($PSCmdlet.ShouldProcess($profile.id, "删除 Codex API profile")) {
        $state.profiles = @($state.profiles | Where-Object { -not [string]::Equals($_.id, $profile.id, [StringComparison]::OrdinalIgnoreCase) })
        Write-State -State $state

        $secretPath = Get-ProfileSecretPath -Id $profile.id
        if (Test-Path -LiteralPath $secretPath) {
            Remove-Item -LiteralPath $secretPath -Force
        }
        if ($DeleteFiles -and (Test-Path -LiteralPath $profile.paths.profileHome)) {
            Remove-Item -LiteralPath $profile.paths.profileHome -Recurse -Force
        }
        if ($DeleteFiles) {
            $configPath = Get-ProfileConfigPath -Id $profile.id -SharedCodexHome $profile.paths.codexHome
            if (Test-Path -LiteralPath $configPath) {
                Remove-Item -LiteralPath $configPath -Force
            }
            if ($profile.paths.launcherPath -and (Test-Path -LiteralPath $profile.paths.launcherPath)) {
                Remove-Item -LiteralPath $profile.paths.launcherPath -Force
            }
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
    "Get-CodexApiLauncherSettings",
    "Set-CodexApiLauncherSharedHome",
    "Get-CodexApiProfileConfigPath",
    "Set-CodexApiProfileRuntime",
    "Invoke-CodexApiLauncherMigration",
    "Get-CodexApiLegacyHomes",
    "New-CodexApiProfile",
    "Set-CodexApiProfileApiKey",
    "Set-CodexApiProfileWorkspace",
    "Set-CodexApiProfileName",
    "Set-CodexApiProfileCodexHome",
    "Set-CodexApiProfile",
    "Get-CodexApiProfile",
    "Get-CodexApiProfiles",
    "Get-CodexApiProfileModels",
    "Test-CodexApiProfile",
    "Start-CodexApiProfile",
    "Remove-CodexApiProfile"
)
Export-ModuleMember -Alias "List-CodexApiProfiles"
