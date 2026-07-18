@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
if exist "D:\develop\PowerShell\7\pwsh.exe" (
  "D:\develop\PowerShell\7\pwsh.exe" -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%CodexApiLauncher.UI.ps1"
) else (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%CodexApiLauncher.UI.ps1"
)
