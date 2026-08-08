# Changelog

## 0.5.0

- 新增进程内快速 Responses 探针：复用 `HttpClient`，使用当前选定模型，不再把 `/models` 作为检查前置条件。
- “快速 CLI 检查”发送 Codex CLI 的 User-Agent、`originator`、`x-codex-*` 指纹和流式 Responses 请求，可识别只接受 CLI 请求形态的中转站。
- 检查在收到响应头或首个有效 SSE 事件后更新阶段；结果窗支持取消，并显示 HTTP 状态码、首事件、耗时和结构化错误分类。
- 保留“完整 CLI 诊断”作为真实 `codex exec` 慢路径；快速检查不会创建 Codex session、SQLite 记录、测试工作目录或额外日志文件。
- Provider 错误详情统一脱敏 Bearer token、`sk-...` 和当前 profile API Key；桌面版本、PowerShell 模块和构建脚本统一升级到 `0.5.0`。

## 0.4.4

- `CLI 检查` 和 `HTTP 检查` 改为点击后立即打开检查窗，窗口内用三点脉冲动画显示进行中状态，完成后原地更新结果。
- HTTP 检查不再请求 `/models`，只使用当前选中的模型发起最小 `/responses` 探针，并展示 HTTP 状态码、耗时和错误摘要。
- 构建流程支持保留仓库本地 `.dotnet` SDK 缓存，避免本机重复下载 SDK。

## 0.4.3

- 仪表盘移除大段“运行输出”日志框，改为只显示最近状态摘要；详细文本仍保留在“日志”页用于复制。
- `CLI 检查` 和 `HTTP 检查` 改为与 `启动 Codex` 同尺寸、同黑底白字主按钮样式。
- 检查完成后弹出结构化结果窗，直接展示通过/失败、HTTP 状态码、CLI 退出码、预期返回和错误详情。

## 0.4.2

- 修复 `resume` / `fork` / `exec` / `review` 子命令启动时的运行参数顺序；全自动、审批、sandbox、web search 等参数现在会放在子命令后面，避免恢复旧会话时继续沿用旧的审批上下文。
- 新建 profile 默认改为无审批全自动：`approvalPolicy=never`、`sandboxMode=danger-full-access`、`fullAuto=true`、`bypassHookTrust=true`。
- `fullAuto=true` 现在会同时追加 `--dangerously-bypass-hook-trust`，确保 UI 勾选“无需审批全自动”时启动命令不再因为 hook trust 单独弹确认。

## 0.4.1

- 改进桌面端标题栏布局，导航按钮贴右排列，避免产品标题被截断。
- 模型和运行参数下拉框改为带稳定描边的自定义 ComboBox，白底状态下边界更清楚。
- 配置页实验开关改为更短的中文标签，新增说明文本和工具提示，降低误开危险参数的风险。
- 构建脚本默认输出版本化发布目录，便于本地只保留最新桌面端产物。

## 0.4.0-local

- 将运行架构改为一个共享 `CODEX_HOME` 加每个 profile 一个 `<id>.config.toml` overlay；启动时使用 `codex --profile <id>`。
- API Key 继续只保存在 Windows protected secret 中，启动时临时注入进程环境变量，不写入 TOML 或脚本。
- 新增 profile runtime 设置：审批级别、sandbox、全自动、目标模式、web search、remote compaction、strict config 和 hook trust。
- 桌面端“配置”页升级为运行配置中心，支持保存运行参数、预览 overlay、打开共享 home/overlay。
- 新增共享 home 设置、legacy home 检查和 v1 `profiles.json` 自动迁移备份。

## 0.3.5-local

- 刷新配置改为轻量刷新，不再临时禁用整组按钮和输入框，避免点击刷新时界面被整体洗灰。
- 刷新图标按钮取消按下/悬停时的灰色填充，保持白底黑图标和清晰边框。
- 右侧仪表盘改为响应式布局，输入框、项目路径和运行输出区会随窗口变宽而扩展，操作按钮贴右排列。

## 0.3.4-local

- 移除顶部“快速开始”按钮，避免它一直呈现主按钮选中感。
- 刷新图标按钮改为固定高对比绘制，刷新/按下/失焦时不再掉色。
- 扩展右侧主面板和输入、日志、输出区域，减少窗口右侧空白。

## 0.3.3-local

- 修复 Windows 禁用按钮黑底黑字的问题，所有主按钮和辅助按钮改为自绘圆角控件。
- 左侧供应商列表改为单列完整显示，避免末尾空白列占位。
- 刷新改为图标按钮，并新增 `assets/icons/refresh-cw.svg` 源文件。
- 顶部“仪表盘 / 配置 / 日志 / 设置”改为真实可切换页面。
- 模型输入改为可输入下拉框，支持从当前供应商 `/models` 自动获取模型列表。

## 0.3.2-local

- 新增黑白终端/API 节点风格应用 logo，提供 SVG、PNG 和 ICO 源文件。
- 桌面端 exe 嵌入应用图标，窗口和桌面快捷方式会使用同一套标识。

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
