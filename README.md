# Codex API Launcher for Windows

Lightweight PowerShell launcher for running multiple Codex CLI instances with isolated third-party API provider configs.

This package does not implement ChatGPT account isolation. Each profile gets its own `CODEX_HOME`, `config.toml`, API key environment variable, logs, sessions, and launcher script.

## Files

- `CodexApiLauncher.psm1` - profile manager module.
- `Run-CodexApiProfile.ps1` - helper used by generated profile launchers.
- `CodexApiLauncher.UI.ps1` - lightweight Windows UI for selecting a profile and project folder.
- `Launch UI.cmd` - double-click helper for opening the UI.
- `templates/profile.config.toml` - minimal generated config shape.
- `examples/create-profile.example.ps1` - example profile creation command.

## UI Launcher

The launcher now has two forms:

- PowerShell commands for automation and scripting.
- A lightweight Windows UI for everyday use.

Open the UI from the repository folder:

```powershell
.\CodexApiLauncher.UI.ps1
```

Or double-click:

```text
Launch UI.cmd
```

The UI lets you:

- choose an existing API profile
- browse for a project folder
- open a new isolated Codex CLI terminal for that folder
- open the profile's `CODEX_HOME`
- run an HTTP smoke test
- run a real Codex CLI check for gateways that only allow Codex request shapes

## Import

From the repository folder:

```powershell
Import-Module ".\CodexApiLauncher.psm1" -Force
```

By default, profile state is stored under:

```text
%LOCALAPPDATA%\CodexApiLauncher
```

For testing or portable use, override it before importing or running commands:

```powershell
$env:CODEX_API_LAUNCHER_HOME = "D:\CodexApiLauncher"
```

## Create a Profile

Use `Read-Host -AsSecureString` so the key does not land in shell history.

```powershell
$key = Read-Host -AsSecureString "API key"

New-CodexApiProfile `
  -Id "shuaiapi" `
  -Name "ShuaiAPI" `
  -BaseUrl "https://api.shuaiapi.com/v1" `
  -Model "gpt-5.6-luna" `
  -Workspace "D:\workplace\Test" `
  -ApiKey $key
```

The generated profile config will look like:

```toml
model_provider = "api_shuaiapi"
model = "gpt-5.6-luna"
model_reasoning_effort = "medium"

[model_providers.api_shuaiapi]
name = "ShuaiAPI"
base_url = "https://api.shuaiapi.com/v1"
env_key = "CODEX_API_SHUAIAPI_KEY"
temp_env_key = "CODEX_API_SHUAIAPI_KEY"
wire_api = "responses"
requires_openai_auth = false
```

API keys are stored separately using the current Windows user's protected secure-string format. They are not written to `config.toml` or generated launch scripts.

## List and Test

```powershell
List-CodexApiProfiles
Test-CodexApiProfile -Id "shuaiapi"
```

`Test-CodexApiProfile` probes:

- `GET <base_url>/models`
- `POST <base_url>/responses`

## Start Codex

Open a new terminal window:

```powershell
Start-CodexApiProfile -Id "shuaiapi"
```

Run in the current terminal:

```powershell
Start-CodexApiProfile -Id "shuaiapi" -InCurrentWindow
```

Pass Codex CLI arguments:

```powershell
Start-CodexApiProfile -Id "shuaiapi" -InCurrentWindow -CodexArgs @("exec", "--skip-git-repo-check", "Reply with OK")
```

Each profile also gets a generated launcher script:

```text
%LOCALAPPDATA%\CodexApiLauncher\launchers\<id>.ps1
```

## Remove

Remove registry entry and encrypted key only:

```powershell
Remove-CodexApiProfile -Id "shuaiapi"
```

Remove profile files too:

```powershell
Remove-CodexApiProfile -Id "shuaiapi" -DeleteFiles
```

## Reference Repo

The external beta project was reviewed only as a reference. It currently has no repository license metadata, so this package is a fresh PowerShell implementation rather than a source merge.
