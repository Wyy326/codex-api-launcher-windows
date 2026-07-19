# Project Organization

This repository is the durable source tree for the Windows CodexCLI API launcher.

## Canonical Locations

- Source checkout: `D:\workplace\codex-api-launcher-windows`
- GitHub repository: `https://github.com/Wyy326/codex-api-launcher-windows`
- Runtime profile root: `%LOCALAPPDATA%\CodexApiLauncher`
- Local release archive: `releases\local`

The original Codex task workspace is kept as a staging and historical source. It should not be deleted until the migrated checkout and GitHub repository have been verified.

## What Belongs In Git

- PowerShell profile management module
- PowerShell UI fallback
- C# desktop launcher source
- Build scripts
- Templates, examples, and public documentation

## What Stays Local

- API keys and encrypted secrets
- Generated profile directories
- Generated `CODEX_HOME` contents
- Local release zip files
- Machine-specific task IDs, Codex task paths, and migration notes

Local-only task details are recorded in `docs\project-archive\THREADS.local.md` and ignored by Git.

## Runtime Boundary

The launcher must not modify the default Codex Desktop or Codex CLI configuration at:

```text
%USERPROFILE%\.codex\config.toml
```

Each launcher profile owns its own child-process environment:

```text
CODEX_HOME=<profile codex-home>
CODEX_API_<ID>_KEY=<runtime key>
```

The API key is stored through the current Windows user's protected secret storage under `%LOCALAPPDATA%\CodexApiLauncher\secrets`, not in TOML, scripts, logs, or source files.

## External Reference Boundary

The beta `JqyModi/codex-multi-launcher` project was used only as a reference for behavior and architecture. Its `jqy/win` branch did not include a clear license at the time it was reviewed, so this repository does not copy or merge its source code.
