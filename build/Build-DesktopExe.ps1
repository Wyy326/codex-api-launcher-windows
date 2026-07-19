[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "0.3.5-local",
    [string]$DotnetPath,
    [switch]$NoZip
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "desktop\CodexApiLauncher.Desktop\CodexApiLauncher.Desktop.csproj"
$distRoot = Join-Path $repoRoot "dist"
$publishDir = Join-Path $distRoot "CodexApiLauncherDesktop-$Runtime"
$zipPath = Join-Path $distRoot "CodexApiLauncherDesktop-$Version-$Runtime.zip"

function Resolve-Dotnet {
    if ($DotnetPath) {
        if (-not (Test-Path -LiteralPath $DotnetPath)) {
            throw "指定的 dotnet 不存在: $DotnetPath"
        }
        return [System.IO.Path]::GetFullPath($DotnetPath)
    }

    $candidates = @(
        (Join-Path $repoRoot ".dotnet\dotnet.exe"),
        (Join-Path $repoRoot "..\..\work\dotnet-sdk\dotnet.exe")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return [System.IO.Path]::GetFullPath($candidate)
        }
    }

    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $command) {
        throw "没有找到 dotnet。请安装 .NET 8 SDK，或用 -DotnetPath 指向 dotnet.exe。"
    }

    $sdkList = & $command.Source --list-sdks
    if (-not $sdkList) {
        throw "当前 dotnet 只有运行时，没有 SDK。请安装 .NET 8 SDK，或用 -DotnetPath 指向 SDK 目录下的 dotnet.exe。"
    }

    return $command.Source
}

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "没有找到桌面端项目: $projectPath"
}

New-Item -ItemType Directory -Force -Path $distRoot | Out-Null
$dotnet = Resolve-Dotnet

& $dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -o $publishDir

if (-not $NoZip) {
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
}

[pscustomobject]@{
    PublishDir = $publishDir
    ExePath = Join-Path $publishDir "CodexApiLauncher.exe"
    ZipPath = if ($NoZip) { $null } else { $zipPath }
}
