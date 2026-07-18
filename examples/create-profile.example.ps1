$launcherRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $launcherRoot "CodexApiLauncher.psm1") -Force

$apiKey = Read-Host -AsSecureString "API key"

New-CodexApiProfile `
    -Id "shuaiapi" `
    -Name "ShuaiAPI" `
    -BaseUrl "https://api.shuaiapi.com/v1" `
    -Model "gpt-5.6-luna" `
    -Workspace "D:\workplace\Test" `
    -ApiKey $apiKey

List-CodexApiProfiles
