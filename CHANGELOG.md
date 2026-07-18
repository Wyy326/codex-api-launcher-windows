# Changelog

## 0.1.1

- 将 Windows UI 改为中文默认体验。
- 优先使用 `Microsoft YaHei UI`，改善中文显示效果。
- 调整配色，让启动器更像日常工具而不是临时脚本窗口。
- 中文化 README 和 UI 设计说明。
- 从示例 profile 创建命令里移除固定项目目录，保持 API 配置和项目文件夹分离。

## 0.1.0

- 添加 PowerShell profile 管理模块，用于隔离 Codex CLI 的第三方 API 配置。
- 为每个 profile 生成独立的 `CODEX_HOME`、`config.toml` 和启动脚本。
- API Key 使用当前 Windows 用户的 protected secure-string 形式保存，不写入 TOML。
- 添加 `/models` 和 `/responses` provider smoke test。
- 添加轻量 Windows UI，用于选择 profile 和项目文件夹后启动 Codex。
- 将 UI 调整为左侧 profile 列表、右侧项目启动面板，并支持可选默认项目文件夹。
