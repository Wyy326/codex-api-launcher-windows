# Changelog

## 0.1.0

- Add PowerShell profile manager for isolated Codex CLI API profiles.
- Generate per-profile `CODEX_HOME`, `config.toml`, and launcher scripts.
- Store API keys outside TOML using the current Windows user's protected secure-string format.
- Add provider smoke tests for `/models` and `/responses`.
- Add lightweight Windows UI for selecting a profile and project folder before launching Codex.
- Redesign the UI into a profile list and project launch panel, with optional saved project defaults.
