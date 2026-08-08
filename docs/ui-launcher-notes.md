# UI 启动器说明

## 产品方向

CodexCLI API 多开启动器是一个 Windows 桌面工具，用来把不同第三方 API provider 的 Codex CLI 会话隔离开。

日常流程应该很短：

1. 选择 API 配置。
2. 选择项目文件夹。
3. 启动新的 Codex CLI 终端。

API/provider 配置、共享 `CODEX_HOME` 和项目文件夹选择必须清晰分离。一个 API 配置可以反复用于不同项目；项目文件夹只在启动时选择，必要时才保存为该配置的默认项目。每个 profile 只拥有一个 `<id>.config.toml` overlay，启动时通过 `codex --profile <id>` 叠加到共享 home。

## 当前 UX 决策

- UI 使用左右双栏：左侧是 API 配置列表，右侧是启动面板。
- API 配置列表显示为“显示名称 | 模型 | 供应商 ID”，避免只看到 `welfare-0xpsyche` 这类内部 id 时难以区分。
- 新增配置时必须能填写中转地址、模型、API Key 和项目目录；legacy HOME 只作为旧目录记录。
- 已有 provider 信息仍在右侧“当前供应商”修改；“配置”页承载运行参数和 overlay 预览。
- 右侧保存修改可以更新供应商 ID、显示名称、中转地址、模型和 API Key。API Key 留空表示保留原密钥。
- 修改供应商 ID 是重命名：密钥文件、快捷启动脚本和 overlay 应该跟随新 ID；旧 per-profile `CODEX_HOME` 不自动删除。
- 日常入口优先使用 `CodexApiLauncher.exe`，PowerShell UI 脚本作为备用入口保留。
- “启动 Codex”是主操作；未选择真实项目文件夹前保持禁用。
- “快速 CLI 检查”优先于原始 HTTP 检查，因为有些中转网关只接受 Codex CLI 的请求形态；“完整 CLI 诊断”只作为较慢的会话级兜底。
- 快速检查直接在桌面进程内发送当前模型的流式 `/responses` 请求，收到响应头和首个 SSE 事件都会更新模态窗阶段；它支持取消，不创建 Codex session 或测试工作目录。
- 默认项目文件夹是可选信息，可以按 profile 保存或清除。
- 开发时使用过的演示项目目录不应该写进 `welfare-0xpsyche` 的默认配置。
- 默认界面语言为中文，按钮和状态提示都围绕实际操作命名。

## 检查分层

- 网络检查：验证地址是否能建立 HTTP 连接，主要用于快速定位 DNS、TLS 或网关不可达。
- 快速 CLI 检查：附加 Codex CLI 身份头和请求指纹，使用当前模型验证 CLI-only provider；通过首个有效输出事件即可结束。
- 完整 CLI 诊断：调用真实 `codex exec`，用于验证配置加载、审批参数、sandbox、会话和实际 CLI 行为，耗时和存储开销都更高。

## 持久化规则

- API Key 不得写入文档、TOML、生成的启动脚本或截图。
- API 配置状态存放在 `%LOCALAPPDATA%\CodexApiLauncher`。
- 所有 profile 共用 `%LOCALAPPDATA%\CodexApiLauncher\codex-home`，每个 profile 一个 `<id>.config.toml`。
- 旧 per-profile `CODEX_HOME` 只记录为 `LegacyCodexHome`，由设置页列出和检查，不自动删除。
- 默认项目文件夹只是可选元数据；清除默认项目不得删除 API 配置或 Key。
- 快速检查结果只保留在当前 UI 状态和结果模态窗，不写入 profile、TOML、日志或 Codex state DB。
