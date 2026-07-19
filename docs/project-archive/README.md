# Project Archive

This folder records how the project was consolidated from multiple Codex task continuations into one durable repository.

Public documentation in this folder avoids local machine paths and task IDs. Machine-specific task mapping is stored in `THREADS.local.md`, which is ignored by Git.

Current project shape:

- CLI-first provider isolation for Codex CLI.
- No ChatGPT login account isolation.
- One installed Codex CLI binary can be reused across many profile-specific `CODEX_HOME` directories.
- Each profile can use a different OpenAI-compatible base URL, API key, model, terminal window, and workspace.
- The desktop launcher and PowerShell UI are wrappers around the same profile management module.

Known local release packages are indexed in `releases/local/README.md`. The zip files themselves are intentionally not tracked in Git.
