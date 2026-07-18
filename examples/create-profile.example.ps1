$launcherRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $launcherRoot "CodexApiLauncher.psm1") -Force

$apiKey = Read-Host -AsSecureString "API Key"

New-CodexApiProfile `
    -Id "shuaiapi" `
    -Name "ShuaiAPI" `
    -BaseUrl "https://api.shuaiapi.com/v1" `
    -Model "gpt-5.6-luna" `
    -ApiKey $apiKey

List-CodexApiProfiles
