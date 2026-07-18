# UI 启动器说明

## 产品方向

Codex API 多开启动器是一个 Windows 工具，用来把不同第三方 API provider 的 Codex CLI 会话隔离开。

日常流程应该很短：

1. 选择 API 配置。
2. 选择项目文件夹。
3. 启动新的 Codex CLI 终端。

API/provider 配置和项目文件夹选择必须分离。一个 API 配置可以反复用于不同项目；项目文件夹只在启动时选择，必要时才保存为该配置的默认项目。

## 当前 UX 决策

- UI 使用左右双栏：左侧是 API 配置列表，右侧是启动面板。
- “启动 Codex”是主操作；未选择真实项目文件夹前保持禁用。
- “CLI 检查”优先于原始 HTTP 检查，因为有些中转网关只接受 Codex CLI 的请求形态。
- 默认项目文件夹是可选信息，可以按 profile 保存或清除。
- 开发时使用过的演示项目目录不应该写进 `welfare-0xpsyche` 的默认配置。
- 默认界面语言为中文，按钮和状态提示都围绕实际操作命名。

## 持久化规则

- API Key 不得写入文档、TOML、生成的启动脚本或截图。
- API 配置状态存放在 `%LOCALAPPDATA%\CodexApiLauncher`。
- 每个 profile 拥有自己的 `CODEX_HOME`。
- 默认项目文件夹只是可选元数据；清除默认项目不得删除 API 配置或 Key。
