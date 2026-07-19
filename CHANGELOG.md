# Changelog

## 0.3.1-local

- 将桌面端主界面调整为参考仪表盘风格：顶部导航、左侧配置侧栏、黑白灰色板和黑底终端输出区。
- 主操作按钮改为黑底白字，辅助按钮保持白底灰边，减少脚本工具感。
- 备用 PowerShell UI 同步黑白灰色板和终端输出区样式。

## 0.3.0-local

- 本地桌面端新增“新增配置”窗口，可以填写中转地址、模型、API Key、配置存放目录和项目目录。
- 桌面端只保留右侧“当前供应商”作为修改入口；左侧只负责刷新、新增和选择。
- 右侧“保存修改”支持更新供应商 ID 和 API Key；API Key 留空时保留现有密钥。
- 修改供应商 ID 时会迁移旧供应商目录、密钥文件和快捷启动脚本，避免复制后留下影子配置。
- `New-CodexApiProfile` 支持 `-CodexHome`，每个 profile 可选择自己的配置存放目录。
- 新增 `Set-CodexApiProfileCodexHome`，可修改已有 profile 的 `CODEX_HOME`，并移动旧目录内容。
- 默认启动优先使用 Windows Terminal，减少传统 PowerShell 黑窗口。
- 创建配置等耗时操作继续在后台执行，避免主界面假死。

## 0.2.1

- 将桌面端和备用 PowerShell UI 的可见产品名改为 `CodexCLI API 多开启动器`。
- API 配置列表改为“显示名称 | 模型 | 供应商 ID”格式，减少只看 id 时的混淆。
- 当前配置详情增加“供应商 ID”，并将示例 profile 显示名改为更容易理解的中文名称。
- 新增 `Set-CodexApiProfileName`，用于重命名 profile 显示名称。

## 0.2.0

- 新增 C# WinForms 桌面端 `CodexApiLauncher.exe`。
- 添加 `build/Build-DesktopExe.ps1`，可生成 self-contained win-x64 发布包。
- 桌面端 exe 复用现有 PowerShell profile 模块，保留 API Key 加密存储、CODEX_HOME 隔离和快捷启动脚本生成逻辑。
- 桌面端支持选择 API 配置、选择项目文件夹、启动 Codex、保存/清除默认项目、CLI 检查和 HTTP 检查。

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
