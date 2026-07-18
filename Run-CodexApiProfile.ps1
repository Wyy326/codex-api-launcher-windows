[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Id,

    [string]$Workspace,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$CodexArgs
)

$modulePath = Join-Path $PSScriptRoot "CodexApiLauncher.psm1"
Import-Module $modulePath -Force

$params = @{
    Id = $Id
    InCurrentWindow = $true
}

if ($PSBoundParameters.ContainsKey("Workspace") -and $Workspace) {
    $params.Workspace = $Workspace
}
if ($CodexArgs) {
    $params.CodexArgs = $CodexArgs
}

Start-CodexApiProfile @params
if ($null -ne $global:LASTEXITCODE) {
    exit ([int]$global:LASTEXITCODE)
}
exit 0
