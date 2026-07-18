# UI Launcher Notes

## Product Direction

Codex API Launcher is a Windows utility for opening isolated Codex CLI sessions against different third-party API providers.

The daily workflow should be:

1. Select an API profile.
2. Choose a project folder.
3. Start a new Codex CLI terminal.

Provider configuration and project folder choice are intentionally separate. A profile stores API/provider state, while the project folder is selected at launch time so the same API profile can be reused across many workspaces.

## Current UX Decisions

- The UI uses a two-pane layout: profiles on the left, launch controls on the right.
- `Start Codex` is the primary action and stays disabled until a real project folder is selected.
- `CLI Check` is preferred over raw HTTP tests for gateways that only allow real Codex request shapes.
- A saved default project folder is optional. Users can save or clear it per profile.
- The demo workspace used during development should not be stored as the default for `welfare-0xpsyche`.

## Persistence Rules

- API keys must never be written to docs, TOML, generated launcher scripts, or screenshots.
- API profile state lives under `%LOCALAPPDATA%\CodexApiLauncher`.
- Each profile owns its own `CODEX_HOME`.
- Project folder defaults are optional metadata only; clearing a project default must not remove the API profile or key.
