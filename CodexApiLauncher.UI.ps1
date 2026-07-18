param(
    [switch]$SmokeTest
)

Set-StrictMode -Version 2.0

$ErrorActionPreference = "Stop"
$modulePath = Join-Path $PSScriptRoot "CodexApiLauncher.psm1"
Import-Module $modulePath -Force

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

if ($SmokeTest) {
    $profiles = @(Get-CodexApiProfiles)
    [pscustomobject]@{
        ModulePath = $modulePath
        ProfileCount = $profiles.Count
        FormsLoaded = [bool]([System.Windows.Forms.Form])
    }
    exit 0
}

[System.Windows.Forms.Application]::EnableVisualStyles()

function New-UiFont {
    param(
        [float]$Size = 9.0,
        [System.Drawing.FontStyle]$Style = [System.Drawing.FontStyle]::Regular
    )
    return New-Object System.Drawing.Font("Segoe UI", $Size, $Style)
}

function New-Label {
    param(
        [string]$Text,
        [int]$X,
        [int]$Y,
        [int]$Width,
        [int]$Height = 23
    )
    $label = New-Object System.Windows.Forms.Label
    $label.Text = $Text
    $label.Location = New-Object System.Drawing.Point($X, $Y)
    $label.Size = New-Object System.Drawing.Size($Width, $Height)
    $label.AutoEllipsis = $true
    $label.Font = New-UiFont
    return $label
}

function New-Button {
    param(
        [string]$Text,
        [int]$X,
        [int]$Y,
        [int]$Width,
        [int]$Height = 32
    )
    $button = New-Object System.Windows.Forms.Button
    $button.Text = $Text
    $button.Location = New-Object System.Drawing.Point($X, $Y)
    $button.Size = New-Object System.Drawing.Size($Width, $Height)
    $button.Font = New-UiFont
    return $button
}

function Resolve-ShellExecutable {
    $preferred = "D:\develop\PowerShell\7\pwsh.exe"
    if (Test-Path -LiteralPath $preferred) {
        return $preferred
    }
    $pwsh = Get-Command pwsh.exe -ErrorAction SilentlyContinue
    if ($pwsh) {
        return $pwsh.Source
    }
    $powershell = Get-Command powershell.exe -ErrorAction SilentlyContinue
    if ($powershell) {
        return $powershell.Source
    }
    throw "Could not find pwsh.exe or powershell.exe."
}

function Format-TestResult {
    param($Result)
    if (-not $Result) {
        return "No result."
    }
    return @(
        "Status: $($Result.Status)"
        "OK: $($Result.Ok)"
        "Models HTTP: $($Result.ModelsHttpStatus)"
        "Responses HTTP: $($Result.ResponsesHttpStatus)"
        "Model count: $($Result.ModelCount)"
        "Details: $($Result.Details)"
    ) -join [Environment]::NewLine
}

function Invoke-CliCheck {
    param(
        [Parameter(Mandatory = $true)]$Profile,
        [Parameter(Mandatory = $true)][string]$Workspace
    )

    $shell = Resolve-ShellExecutable
    $runner = Join-Path $PSScriptRoot "Run-CodexApiProfile.ps1"
    $stamp = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $outFile = Join-Path $env:TEMP "codex-api-launcher-cli-check-$stamp.out.txt"
    $errFile = Join-Path $env:TEMP "codex-api-launcher-cli-check-$stamp.err.txt"
    $args = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $runner,
        "-Id", $Profile.Id,
        "-Workspace", $Workspace,
        "-CodexArgs",
        "exec",
        "--skip-git-repo-check",
        "Reply exactly CLI_OK"
    )

    try {
        $process = Start-Process -FilePath $shell -ArgumentList $args -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput $outFile -RedirectStandardError $errFile
        $stdout = if (Test-Path -LiteralPath $outFile) { Get-Content -LiteralPath $outFile -Raw } else { "" }
        $stderr = if (Test-Path -LiteralPath $errFile) { Get-Content -LiteralPath $errFile -Raw } else { "" }
        if ($stdout.Length -gt 1200) {
            $stdout = $stdout.Substring(0, 1200)
        }
        if ($stderr.Length -gt 800) {
            $stderr = $stderr.Substring(0, 800)
        }
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Stdout = $stdout.Trim()
            Stderr = $stderr.Trim()
        }
    }
    finally {
        Remove-Item -LiteralPath $outFile -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $errFile -Force -ErrorAction SilentlyContinue
    }
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "Codex API Launcher"
$form.StartPosition = "CenterScreen"
$form.Size = New-Object System.Drawing.Size(820, 540)
$form.MinimumSize = New-Object System.Drawing.Size(760, 500)
$form.Font = New-UiFont

$title = New-Label -Text "Codex API Launcher" -X 18 -Y 16 -Width 460 -Height 30
$title.Font = New-UiFont -Size 14 -Style ([System.Drawing.FontStyle]::Bold)
$form.Controls.Add($title)

$subtitle = New-Label -Text "Choose an isolated API profile and a project folder, then open a new Codex CLI window." -X 18 -Y 48 -Width 740 -Height 23
$form.Controls.Add($subtitle)

$profileLabel = New-Label -Text "Profile" -X 18 -Y 88 -Width 120
$form.Controls.Add($profileLabel)

$profileBox = New-Object System.Windows.Forms.ComboBox
$profileBox.Location = New-Object System.Drawing.Point(140, 84)
$profileBox.Size = New-Object System.Drawing.Size(460, 28)
$profileBox.DropDownStyle = [System.Windows.Forms.ComboBoxStyle]::DropDownList
$profileBox.Font = New-UiFont
$form.Controls.Add($profileBox)

$refreshButton = New-Button -Text "Refresh" -X 616 -Y 82 -Width 88
$form.Controls.Add($refreshButton)

$openLauncherButton = New-Button -Text "Open Launchers" -X 710 -Y 82 -Width 92
$form.Controls.Add($openLauncherButton)

$workspaceLabel = New-Label -Text "Project folder" -X 18 -Y 130 -Width 120
$form.Controls.Add($workspaceLabel)

$workspaceBox = New-Object System.Windows.Forms.TextBox
$workspaceBox.Location = New-Object System.Drawing.Point(140, 126)
$workspaceBox.Size = New-Object System.Drawing.Size(564, 27)
$workspaceBox.Font = New-UiFont
$form.Controls.Add($workspaceBox)

$browseButton = New-Button -Text "Browse..." -X 716 -Y 123 -Width 86
$form.Controls.Add($browseButton)

$detailsBox = New-Object System.Windows.Forms.TextBox
$detailsBox.Location = New-Object System.Drawing.Point(140, 166)
$detailsBox.Size = New-Object System.Drawing.Size(662, 112)
$detailsBox.Multiline = $true
$detailsBox.ReadOnly = $true
$detailsBox.ScrollBars = [System.Windows.Forms.ScrollBars]::Vertical
$detailsBox.Font = New-UiFont
$form.Controls.Add($detailsBox)

$detailsLabel = New-Label -Text "Details" -X 18 -Y 168 -Width 120
$form.Controls.Add($detailsLabel)

$startButton = New-Button -Text "Start Codex" -X 140 -Y 298 -Width 132 -Height 38
$startButton.Font = New-UiFont -Size 10 -Style ([System.Drawing.FontStyle]::Bold)
$form.Controls.Add($startButton)

$cliCheckButton = New-Button -Text "CLI Check" -X 286 -Y 300 -Width 108
$form.Controls.Add($cliCheckButton)

$httpTestButton = New-Button -Text "HTTP Test" -X 406 -Y 300 -Width 108
$form.Controls.Add($httpTestButton)

$openHomeButton = New-Button -Text "Open CODEX_HOME" -X 526 -Y 300 -Width 136
$form.Controls.Add($openHomeButton)

$exitButton = New-Button -Text "Exit" -X 674 -Y 300 -Width 86
$form.Controls.Add($exitButton)

$noteLabel = New-Label -Text "Note: HTTP Test may fail for gateways that only allow real Codex CLI request shapes. Use CLI Check for those providers." -X 18 -Y 352 -Width 784 -Height 24
$form.Controls.Add($noteLabel)

$statusBox = New-Object System.Windows.Forms.TextBox
$statusBox.Location = New-Object System.Drawing.Point(18, 382)
$statusBox.Size = New-Object System.Drawing.Size(784, 104)
$statusBox.Multiline = $true
$statusBox.ReadOnly = $true
$statusBox.ScrollBars = [System.Windows.Forms.ScrollBars]::Vertical
$statusBox.Font = New-UiFont
$form.Controls.Add($statusBox)

$profileMap = @{}

function Set-Status {
    param([string]$Text)
    $statusBox.Text = $Text
}

function Get-SelectedProfile {
    if (-not $profileBox.SelectedItem) {
        return $null
    }
    return $profileMap[[string]$profileBox.SelectedItem]
}

function Update-Details {
    $profile = Get-SelectedProfile
    if (-not $profile) {
        $detailsBox.Text = "No profiles found. Create one with New-CodexApiProfile first."
        return
    }

    if ($profile.Workspace -and -not $workspaceBox.Text) {
        $workspaceBox.Text = [string]$profile.Workspace
    }

    $detailsBox.Text = @(
        "ID: $($profile.Id)"
        "Name: $($profile.Name)"
        "Base URL: $($profile.BaseUrl)"
        "Model: $($profile.Model)"
        "Env key: $($profile.EnvKeyName)"
        "CODEX_HOME: $($profile.CodexHome)"
        "Launcher: $($profile.LauncherPath)"
        "API key stored: $($profile.HasApiKey)"
    ) -join [Environment]::NewLine
}

function Refresh-Profiles {
    $profileBox.Items.Clear()
    $profileMap.Clear()
    $profiles = @(Get-CodexApiProfiles | Sort-Object Id)
    foreach ($profile in $profiles) {
        $display = "$($profile.Name) [$($profile.Id)]"
        $profileMap[$display] = $profile
        [void]$profileBox.Items.Add($display)
    }
    if ($profileBox.Items.Count -gt 0) {
        $profileBox.SelectedIndex = 0
    }
    else {
        $workspaceBox.Text = ""
    }
    Update-Details
    Set-Status "Loaded $($profiles.Count) profile(s)."
}

$profileBox.Add_SelectedIndexChanged({
    $workspaceBox.Text = ""
    $profile = Get-SelectedProfile
    if ($profile -and $profile.Workspace) {
        $workspaceBox.Text = [string]$profile.Workspace
    }
    Update-Details
})

$refreshButton.Add_Click({
    try {
        Refresh-Profiles
    }
    catch {
        Set-Status $_.Exception.Message
    }
})

$browseButton.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Choose the project folder for Codex"
    $dialog.ShowNewFolderButton = $true
    if ($workspaceBox.Text -and (Test-Path -LiteralPath $workspaceBox.Text -PathType Container)) {
        $dialog.SelectedPath = $workspaceBox.Text
    }
    if ($dialog.ShowDialog($form) -eq [System.Windows.Forms.DialogResult]::OK) {
        $workspaceBox.Text = $dialog.SelectedPath
    }
})

$startButton.Add_Click({
    try {
        $profile = Get-SelectedProfile
        if (-not $profile) {
            throw "Select a profile first."
        }
        $workspace = $workspaceBox.Text.Trim()
        if (-not $workspace -or -not (Test-Path -LiteralPath $workspace -PathType Container)) {
            throw "Choose an existing project folder."
        }
        $result = Start-CodexApiProfile -Id $profile.Id -Workspace $workspace
        Set-Status ("Started profile '{0}' in a new terminal.`r`nCODEX_HOME: {1}`r`nWorkspace: {2}" -f $profile.Id, $result.CodexHome, $workspace)
    }
    catch {
        Set-Status $_.Exception.Message
        [System.Windows.Forms.MessageBox]::Show($form, $_.Exception.Message, "Start failed", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    }
})

$cliCheckButton.Add_Click({
    try {
        $profile = Get-SelectedProfile
        if (-not $profile) {
            throw "Select a profile first."
        }
        $workspace = $workspaceBox.Text.Trim()
        if (-not $workspace -or -not (Test-Path -LiteralPath $workspace -PathType Container)) {
            throw "Choose an existing project folder."
        }
        Set-Status "Running Codex CLI check. This can take a little while..."
        $form.Refresh()
        $result = Invoke-CliCheck -Profile $profile -Workspace $workspace
        Set-Status (@(
            "CLI check exit code: $($result.ExitCode)"
            "STDOUT:"
            $result.Stdout
            ""
            "STDERR:"
            $result.Stderr
        ) -join [Environment]::NewLine)
    }
    catch {
        Set-Status $_.Exception.Message
    }
})

$httpTestButton.Add_Click({
    try {
        $profile = Get-SelectedProfile
        if (-not $profile) {
            throw "Select a profile first."
        }
        Set-Status "Running HTTP smoke test..."
        $form.Refresh()
        $result = Test-CodexApiProfile -Id $profile.Id
        Set-Status (Format-TestResult -Result $result)
    }
    catch {
        Set-Status $_.Exception.Message
    }
})

$openHomeButton.Add_Click({
    try {
        $profile = Get-SelectedProfile
        if (-not $profile) {
            throw "Select a profile first."
        }
        if (-not (Test-Path -LiteralPath $profile.CodexHome)) {
            New-Item -ItemType Directory -Force -Path $profile.CodexHome | Out-Null
        }
        Invoke-Item -LiteralPath $profile.CodexHome
    }
    catch {
        Set-Status $_.Exception.Message
    }
})

$openLauncherButton.Add_Click({
    try {
        $root = Get-CodexApiLauncherRoot
        $launcherDir = Join-Path $root "launchers"
        if (-not (Test-Path -LiteralPath $launcherDir)) {
            New-Item -ItemType Directory -Force -Path $launcherDir | Out-Null
        }
        Invoke-Item -LiteralPath $launcherDir
    }
    catch {
        Set-Status $_.Exception.Message
    }
})

$exitButton.Add_Click({
    $form.Close()
})

Refresh-Profiles
[void]$form.ShowDialog()
