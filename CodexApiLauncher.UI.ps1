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
    Write-Output ([pscustomobject]@{
        ModulePath = $modulePath
        ProfileCount = @(Get-CodexApiProfiles).Count
        FormsLoaded = [bool]([System.Windows.Forms.Form])
    })
    return
}

[System.Windows.Forms.Application]::EnableVisualStyles()

$script:Colors = @{
    Window = [System.Drawing.Color]::FromArgb(245, 247, 250)
    Surface = [System.Drawing.Color]::White
    Border = [System.Drawing.Color]::FromArgb(218, 224, 232)
    Text = [System.Drawing.Color]::FromArgb(32, 36, 42)
    Muted = [System.Drawing.Color]::FromArgb(91, 99, 111)
    Primary = [System.Drawing.Color]::FromArgb(36, 92, 161)
    PrimaryDark = [System.Drawing.Color]::FromArgb(24, 65, 119)
    SoftBlue = [System.Drawing.Color]::FromArgb(232, 240, 252)
    Warning = [System.Drawing.Color]::FromArgb(122, 83, 18)
}

function New-UiFont {
    param(
        [float]$Size = 9.0,
        [System.Drawing.FontStyle]$Style = [System.Drawing.FontStyle]::Regular
    )
    return New-Object System.Drawing.Font("Segoe UI", $Size, $Style)
}

function New-Panel {
    param(
        [int]$X,
        [int]$Y,
        [int]$Width,
        [int]$Height
    )
    $panel = New-Object System.Windows.Forms.Panel
    $panel.Location = New-Object System.Drawing.Point($X, $Y)
    $panel.Size = New-Object System.Drawing.Size($Width, $Height)
    $panel.BackColor = $script:Colors.Surface
    $panel.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
    return $panel
}

function New-Label {
    param(
        [string]$Text,
        [int]$X,
        [int]$Y,
        [int]$Width,
        [int]$Height = 23,
        [float]$Size = 9.0,
        [System.Drawing.FontStyle]$Style = [System.Drawing.FontStyle]::Regular,
        [System.Drawing.Color]$Color = $script:Colors.Text
    )
    $label = New-Object System.Windows.Forms.Label
    $label.Text = $Text
    $label.Location = New-Object System.Drawing.Point($X, $Y)
    $label.Size = New-Object System.Drawing.Size($Width, $Height)
    $label.AutoEllipsis = $true
    $label.Font = New-UiFont -Size $Size -Style $Style
    $label.ForeColor = $Color
    return $label
}

function New-Button {
    param(
        [string]$Text,
        [int]$X,
        [int]$Y,
        [int]$Width,
        [int]$Height = 34,
        [switch]$Primary
    )
    $button = New-Object System.Windows.Forms.Button
    $button.Text = $Text
    $button.Location = New-Object System.Drawing.Point($X, $Y)
    $button.Size = New-Object System.Drawing.Size($Width, $Height)
    $button.Font = New-UiFont -Size 9.0 -Style ($(if ($Primary) { [System.Drawing.FontStyle]::Bold } else { [System.Drawing.FontStyle]::Regular }))
    $button.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
    $button.FlatAppearance.BorderColor = $script:Colors.Border
    $button.FlatAppearance.MouseOverBackColor = $script:Colors.SoftBlue
    if ($Primary) {
        $button.BackColor = $script:Colors.Primary
        $button.ForeColor = [System.Drawing.Color]::White
        $button.FlatAppearance.BorderColor = $script:Colors.PrimaryDark
    }
    else {
        $button.BackColor = [System.Drawing.Color]::FromArgb(250, 251, 253)
        $button.ForeColor = $script:Colors.Text
    }
    return $button
}

function New-ReadOnlyBox {
    param(
        [int]$X,
        [int]$Y,
        [int]$Width,
        [int]$Height
    )
    $box = New-Object System.Windows.Forms.TextBox
    $box.Location = New-Object System.Drawing.Point($X, $Y)
    $box.Size = New-Object System.Drawing.Size($Width, $Height)
    $box.Multiline = $true
    $box.ReadOnly = $true
    $box.ScrollBars = [System.Windows.Forms.ScrollBars]::Vertical
    $box.Font = New-UiFont
    $box.BackColor = [System.Drawing.Color]::FromArgb(250, 251, 253)
    $box.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
    return $box
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

function Format-TestResult {
    param($Result)
    return @(
        "HTTP smoke test"
        "Status: $($Result.Status)"
        "OK: $($Result.Ok)"
        "Models HTTP: $($Result.ModelsHttpStatus)"
        "Responses HTTP: $($Result.ResponsesHttpStatus)"
        "Model count: $($Result.ModelCount)"
        "Details: $($Result.Details)"
    ) -join [Environment]::NewLine
}

function Get-ProfileDisplay {
    param($Profile)
    if (-not $Profile) {
        return ""
    }
    return "$($Profile.Name)`r`n$($Profile.Model)"
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "Codex API Launcher"
$form.StartPosition = "CenterScreen"
$form.Size = New-Object System.Drawing.Size(980, 650)
$form.MinimumSize = New-Object System.Drawing.Size(920, 600)
$form.BackColor = $script:Colors.Window
$form.Font = New-UiFont
$form.AutoScaleMode = [System.Windows.Forms.AutoScaleMode]::Font

$headerTitle = New-Label -Text "Codex API Launcher" -X 24 -Y 18 -Width 360 -Height 34 -Size 16 -Style ([System.Drawing.FontStyle]::Bold)
$form.Controls.Add($headerTitle)

$headerText = New-Label -Text "Pick an API profile, choose a project folder, and open an isolated Codex CLI window." -X 24 -Y 54 -Width 820 -Height 24 -Color $script:Colors.Muted
$form.Controls.Add($headerText)

$leftPanel = New-Panel -X 24 -Y 94 -Width 292 -Height 500
$form.Controls.Add($leftPanel)

$rightPanel = New-Panel -X 334 -Y 94 -Width 622 -Height 500
$form.Controls.Add($rightPanel)

$profilesTitle = New-Label -Text "Profiles" -X 16 -Y 14 -Width 160 -Height 26 -Size 11 -Style ([System.Drawing.FontStyle]::Bold)
$leftPanel.Controls.Add($profilesTitle)

$refreshButton = New-Button -Text "Refresh" -X 186 -Y 12 -Width 84 -Height 30
$leftPanel.Controls.Add($refreshButton)

$profileList = New-Object System.Windows.Forms.ListBox
$profileList.Location = New-Object System.Drawing.Point(16, 52)
$profileList.Size = New-Object System.Drawing.Size(254, 296)
$profileList.Font = New-UiFont -Size 9.5
$profileList.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
$profileList.BackColor = [System.Drawing.Color]::FromArgb(250, 251, 253)
$leftPanel.Controls.Add($profileList)

$profileHint = New-Label -Text "Profiles keep API credentials and Codex state separate. Project folders are selected at launch time." -X 16 -Y 362 -Width 254 -Height 54 -Color $script:Colors.Muted
$leftPanel.Controls.Add($profileHint)

$openLaunchersButton = New-Button -Text "Open launcher scripts" -X 16 -Y 438 -Width 254 -Height 34
$leftPanel.Controls.Add($openLaunchersButton)

$selectedTitle = New-Label -Text "Selected profile" -X 20 -Y 16 -Width 200 -Height 28 -Size 12 -Style ([System.Drawing.FontStyle]::Bold)
$rightPanel.Controls.Add($selectedTitle)

$providerNameLabel = New-Label -Text "No profile selected" -X 20 -Y 48 -Width 372 -Height 24 -Size 10 -Style ([System.Drawing.FontStyle]::Bold)
$rightPanel.Controls.Add($providerNameLabel)

$providerMetaLabel = New-Label -Text "" -X 20 -Y 74 -Width 570 -Height 42 -Color $script:Colors.Muted
$rightPanel.Controls.Add($providerMetaLabel)

$homeButton = New-Button -Text "Open CODEX_HOME" -X 446 -Y 42 -Width 146 -Height 32
$rightPanel.Controls.Add($homeButton)

$separator1 = New-Object System.Windows.Forms.Label
$separator1.BorderStyle = [System.Windows.Forms.BorderStyle]::Fixed3D
$separator1.Location = New-Object System.Drawing.Point(20, 124)
$separator1.Size = New-Object System.Drawing.Size(572, 2)
$rightPanel.Controls.Add($separator1)

$projectTitle = New-Label -Text "Project folder" -X 20 -Y 146 -Width 180 -Height 26 -Size 11 -Style ([System.Drawing.FontStyle]::Bold)
$rightPanel.Controls.Add($projectTitle)

$savedProjectLabel = New-Label -Text "Saved default: none" -X 20 -Y 174 -Width 570 -Height 24 -Color $script:Colors.Muted
$rightPanel.Controls.Add($savedProjectLabel)

$workspaceBox = New-Object System.Windows.Forms.TextBox
$workspaceBox.Location = New-Object System.Drawing.Point(20, 204)
$workspaceBox.Size = New-Object System.Drawing.Size(456, 28)
$workspaceBox.Font = New-UiFont -Size 9.5
$rightPanel.Controls.Add($workspaceBox)

$browseButton = New-Button -Text "Browse" -X 488 -Y 200 -Width 104 -Height 34 -Primary
$rightPanel.Controls.Add($browseButton)

$rememberCheck = New-Object System.Windows.Forms.CheckBox
$rememberCheck.Text = "Remember this folder for the selected profile"
$rememberCheck.Location = New-Object System.Drawing.Point(20, 244)
$rememberCheck.Size = New-Object System.Drawing.Size(300, 24)
$rememberCheck.Font = New-UiFont
$rememberCheck.BackColor = $script:Colors.Surface
$rightPanel.Controls.Add($rememberCheck)

$saveProjectButton = New-Button -Text "Save default" -X 330 -Y 240 -Width 120 -Height 32
$rightPanel.Controls.Add($saveProjectButton)

$clearProjectButton = New-Button -Text "Clear default" -X 464 -Y 240 -Width 128 -Height 32
$rightPanel.Controls.Add($clearProjectButton)

$separator2 = New-Object System.Windows.Forms.Label
$separator2.BorderStyle = [System.Windows.Forms.BorderStyle]::Fixed3D
$separator2.Location = New-Object System.Drawing.Point(20, 292)
$separator2.Size = New-Object System.Drawing.Size(572, 2)
$rightPanel.Controls.Add($separator2)

$startButton = New-Button -Text "Start Codex" -X 20 -Y 316 -Width 170 -Height 42 -Primary
$startButton.Font = New-UiFont -Size 10.5 -Style ([System.Drawing.FontStyle]::Bold)
$rightPanel.Controls.Add($startButton)

$cliCheckButton = New-Button -Text "CLI Check" -X 206 -Y 320 -Width 112 -Height 36
$rightPanel.Controls.Add($cliCheckButton)

$httpTestButton = New-Button -Text "HTTP Test" -X 332 -Y 320 -Width 112 -Height 36
$rightPanel.Controls.Add($httpTestButton)

$statusText = New-ReadOnlyBox -X 20 -Y 382 -Width 572 -Height 92
$rightPanel.Controls.Add($statusText)

$profileMap = @{}

function Set-Status {
    param([string]$Text)
    $statusText.Text = $Text
}

function Get-SelectedProfile {
    if ($profileList.SelectedItem) {
        return $profileMap[[string]$profileList.SelectedItem]
    }
    return $null
}

function Test-WorkspaceReady {
    $path = $workspaceBox.Text.Trim()
    return ($path -and (Test-Path -LiteralPath $path -PathType Container))
}

function Update-Buttons {
    $hasProfile = $null -ne (Get-SelectedProfile)
    $hasWorkspace = Test-WorkspaceReady
    $startButton.Enabled = $hasProfile -and $hasWorkspace
    $cliCheckButton.Enabled = $hasProfile -and $hasWorkspace
    $httpTestButton.Enabled = $hasProfile
    $homeButton.Enabled = $hasProfile
    $saveProjectButton.Enabled = $hasProfile -and $hasWorkspace
    $clearProjectButton.Enabled = $hasProfile
}

function Update-SelectedProfile {
    $profile = Get-SelectedProfile
    if (-not $profile) {
        $providerNameLabel.Text = "No profile selected"
        $providerMetaLabel.Text = "Create a profile with New-CodexApiProfile, then refresh this list."
        $savedProjectLabel.Text = "Saved default: none"
        $workspaceBox.Text = ""
        Update-Buttons
        return
    }

    $providerNameLabel.Text = "$($profile.Name) [$($profile.Id)]"
    $providerMetaLabel.Text = "Model: $($profile.Model)`r`nBase URL: $($profile.BaseUrl)`r`nEnv: $($profile.EnvKeyName)"

    if ($profile.Workspace) {
        $savedProjectLabel.Text = "Saved default: $($profile.Workspace)"
        if (-not $workspaceBox.Text) {
            $workspaceBox.Text = [string]$profile.Workspace
        }
    }
    else {
        $savedProjectLabel.Text = "Saved default: none"
        $workspaceBox.Text = ""
    }
    Update-Buttons
}

function Refresh-Profiles {
    $profileList.Items.Clear()
    $profileMap.Clear()
    $profiles = @(Get-CodexApiProfiles | Sort-Object Name, Id)
    foreach ($profile in $profiles) {
        $display = "$($profile.Name) [$($profile.Id)]"
        $profileMap[$display] = $profile
        [void]$profileList.Items.Add($display)
    }

    if ($profileList.Items.Count -gt 0) {
        $profileList.SelectedIndex = 0
        Set-Status "Loaded $($profiles.Count) profile(s). Choose a project folder to start."
    }
    else {
        Set-Status "No profiles found yet."
    }
    Update-SelectedProfile
}

$profileList.Add_SelectedIndexChanged({
    $workspaceBox.Text = ""
    Update-SelectedProfile
})

$workspaceBox.Add_TextChanged({
    Update-Buttons
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
    try {
        $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
        $dialog.Description = "Choose the project folder for Codex"
        $dialog.ShowNewFolderButton = $true
        if ((Test-WorkspaceReady)) {
            $dialog.SelectedPath = $workspaceBox.Text.Trim()
        }
        elseif ([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)) {
            $dialog.SelectedPath = [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
        }
        if ($dialog.ShowDialog($form) -eq [System.Windows.Forms.DialogResult]::OK) {
            $workspaceBox.Text = $dialog.SelectedPath
            Set-Status "Project folder selected."
        }
    }
    catch {
        Set-Status $_.Exception.Message
    }
})

$saveProjectButton.Add_Click({
    try {
        $profile = Get-SelectedProfile
        if (-not $profile) {
            throw "Select a profile first."
        }
        if (-not (Test-WorkspaceReady)) {
            throw "Choose an existing project folder before saving."
        }
        $result = Set-CodexApiProfileWorkspace -Id $profile.Id -Workspace $workspaceBox.Text.Trim()
        Set-Status "Saved default project folder for $($profile.Id)."
        Refresh-Profiles
        foreach ($item in $profileList.Items) {
            if ([string]$item -like "*[$($profile.Id)]") {
                $profileList.SelectedItem = $item
                break
            }
        }
    }
    catch {
        Set-Status $_.Exception.Message
    }
})

$clearProjectButton.Add_Click({
    try {
        $profile = Get-SelectedProfile
        if (-not $profile) {
            throw "Select a profile first."
        }
        Set-CodexApiProfileWorkspace -Id $profile.Id -Clear | Out-Null
        $workspaceBox.Text = ""
        Set-Status "Cleared the saved project folder for $($profile.Id)."
        Refresh-Profiles
        foreach ($item in $profileList.Items) {
            if ([string]$item -like "*[$($profile.Id)]") {
                $profileList.SelectedItem = $item
                break
            }
        }
    }
    catch {
        Set-Status $_.Exception.Message
    }
})

$startButton.Add_Click({
    try {
        $profile = Get-SelectedProfile
        if (-not $profile) {
            throw "Select a profile first."
        }
        if (-not (Test-WorkspaceReady)) {
            throw "Choose an existing project folder."
        }

        $workspace = $workspaceBox.Text.Trim()
        if ($rememberCheck.Checked) {
            Set-CodexApiProfileWorkspace -Id $profile.Id -Workspace $workspace | Out-Null
        }
        $result = Start-CodexApiProfile -Id $profile.Id -Workspace $workspace
        Set-Status "Started $($profile.Id) in a new Codex terminal.`r`nProject: $workspace`r`nCODEX_HOME: $($result.CodexHome)"
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
        if (-not (Test-WorkspaceReady)) {
            throw "Choose an existing project folder."
        }
        Set-Status "Running a real Codex CLI check..."
        $form.Refresh()
        $result = Invoke-CliCheck -Profile $profile -Workspace $workspaceBox.Text.Trim()
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

$homeButton.Add_Click({
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

$openLaunchersButton.Add_Click({
    try {
        $launcherDir = Join-Path (Get-CodexApiLauncherRoot) "launchers"
        if (-not (Test-Path -LiteralPath $launcherDir)) {
            New-Item -ItemType Directory -Force -Path $launcherDir | Out-Null
        }
        Invoke-Item -LiteralPath $launcherDir
    }
    catch {
        Set-Status $_.Exception.Message
    }
})

Refresh-Profiles
[void]$form.ShowDialog()
