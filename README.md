# CodexCLI API 多开启动器 for Windows

一个轻量 Windows 桌面工具，用来启动多个彼此隔离的 Codex CLI 实例。它面向第三方 OpenAI-compatible API provider，不做 ChatGPT 登录账号隔离。

所有 API 配置共用一份 launcher 管理的 `CODEX_HOME`，每个 profile 只生成一个轻量 overlay：`<id>.config.toml`。启动时会设置共享 `CODEX_HOME`、注入该 profile 的 API Key 环境变量，并通过 `codex --profile <id>` 载入对应 overlay。API Key 不会写入 TOML、脚本或仓库文件。界面里的配置列表使用“显示名称 | 模型 | 供应商 ID”格式，方便区分多个中转。

## 文件

- `CodexApiLauncher.psm1` - API 配置管理模块。
- `Run-CodexApiProfile.ps1` - 生成的 profile 启动脚本会调用它。
- `desktop/CodexApiLauncher.Desktop` - C# WinForms 桌面端 exe 项目。
- `build/Build-DesktopExe.ps1` - 生成 `CodexApiLauncher.exe` 的构建脚本。
- `CodexApiLauncher.UI.ps1` - 备用 PowerShell UI，可选择 API 配置和项目文件夹。
- `Launch UI.cmd` - 双击打开备用 PowerShell UI 的入口。
- `templates/profile.config.toml` - 生成的 Codex 配置模板。
- `examples/create-profile.example.ps1` - 创建 profile 的示例脚本。

## 日常使用

推荐下载或构建桌面端包，然后双击：

```text
CodexApiLauncher.exe
```

如果只想使用脚本版 UI，可以双击仓库里的：

```text
Launch UI.cmd
```

也可以在 PowerShell 里运行：

```powershell
.\CodexApiLauncher.UI.ps1
```

UI 支持：

- 选择已有 API 配置
- 新增自定义 API 配置
- 在右侧“当前供应商”里修改显示名称、供应商 ID、中转地址、模型和 API Key
- 在“配置”页调整 Codex 运行参数：审批级别、sandbox、目标模式、web search、全自动模式等
- 在“设置”页调整共享 `CODEX_HOME`，并检查旧 per-profile home
- 选择项目文件夹
- 在新的终端窗口里启动隔离的 Codex CLI
- 可选保存或清除某个配置的默认项目文件夹
- 打开共享 `CODEX_HOME` 或当前 profile overlay
- 运行 HTTP 连通性检查
- 运行真实 Codex CLI 检查，适合只允许 CLI 请求形态的中转网关

## 构建桌面端 exe

需要 .NET 8 SDK。仓库目录运行：

```powershell
.\build\Build-DesktopExe.ps1
```

默认会生成：

```text
dist\CodexApiLauncherDesktop-0.4.1-win-x64\CodexApiLauncher.exe
dist\CodexApiLauncherDesktop-0.4.1-win-x64.zip
```

发布包是 self-contained win-x64 构建，不需要目标机器额外安装 .NET 运行时。运行时仍会调用同目录的 PowerShell 模块，以复用已有的 profile、API Key 加密存储、共享 `CODEX_HOME` 和 overlay 配置逻辑。默认启动优先走 Windows Terminal，减少传统 PowerShell 黑窗口。

## 导入模块

在仓库目录运行：

```powershell
Import-Module ".\CodexApiLauncher.psm1" -Force
```

默认状态目录：

```text
%LOCALAPPDATA%\CodexApiLauncher
```

如需放到其他位置，可以先设置：

```powershell
$env:CODEX_API_LAUNCHER_HOME = "D:\CodexApiLauncher"
```

## 创建 API 配置

建议用 `Read-Host -AsSecureString` 输入 Key，避免进入命令历史。

```powershell
$key = Read-Host -AsSecureString "API Key"

New-CodexApiProfile `
  -Id "shuaiapi" `
  -Name "ShuaiAPI" `
  -BaseUrl "https://api.shuaiapi.com/v1" `
  -Model "gpt-5.6-luna" `
  -Workspace "D:\workplace\your-project" `
  -ApiKey $key
```

默认共享 home：

```text
%LOCALAPPDATA%\CodexApiLauncher\codex-home
```

生成的 overlay 文件：

```text
%LOCALAPPDATA%\CodexApiLauncher\codex-home\shuaiapi.config.toml
```

overlay TOML 形态：

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

API Key 会使用当前 Windows 用户的 protected secure-string 形式单独保存，不会写进 `config.toml` 或生成的启动脚本。

## 运行参数

每个 profile 可以配置 Codex 启动参数：

```powershell
Set-CodexApiProfileRuntime `
  -Id "shuaiapi" `
  -ApprovalPolicy "never" `
  -SandboxMode "workspace-write" `
  -GoalMode "enabled" `
  -WebSearch "disabled"
```

`-FullAuto $true` 会使用 `--dangerously-bypass-approvals-and-sandbox`，并避免再追加冲突的审批或 sandbox 参数。

## 查看和检查

```powershell
List-CodexApiProfiles
Test-CodexApiProfile -Id "shuaiapi"
```

`Test-CodexApiProfile` 会请求：

- `GET <base_url>/models`
- `POST <base_url>/responses`

如果你的中转只支持 Codex CLI 请求形态，优先在 UI 里用“CLI 检查”，或运行：

```powershell
Start-CodexApiProfile -Id "shuaiapi" -InCurrentWindow -CodexArgs @("exec", "--skip-git-repo-check", "Reply with OK")
```

## 启动 Codex

打开新的终端窗口：

```powershell
Start-CodexApiProfile -Id "shuaiapi" -Workspace "D:\workplace\your-project"
```

在当前终端运行：

```powershell
Start-CodexApiProfile -Id "shuaiapi" -Workspace "D:\workplace\your-project" -InCurrentWindow
```

每个 profile 也会生成一个快捷启动脚本：

```text
%LOCALAPPDATA%\CodexApiLauncher\launchers\<id>.ps1
```

项目文件夹是可选默认值，可以随时保存或清除：

```powershell
Set-CodexApiProfileWorkspace -Id "shuaiapi" -Workspace "D:\workplace\your-project"
Set-CodexApiProfileWorkspace -Id "shuaiapi" -Clear
```

修改共享 `CODEX_HOME`：

```powershell
Set-CodexApiLauncherSharedHome -SharedCodexHome "D:\CodexApiLauncher\codex-home"
```

检查旧 per-profile home：

```powershell
Invoke-CodexApiLauncherMigration -DryRun
Get-CodexApiLegacyHomes
```

修改供应商 ID 或其他 provider 信息：

```powershell
Set-CodexApiProfile `
  -Id "shuaiapi" `
  -NewId "shuaiapi-main" `
  -Name "ShuaiAPI 主力中转" `
  -BaseUrl "https://api.shuaiapi.com/v1" `
  -Model "gpt-5.6-luna"
```

修改供应商 ID 会重命名密钥文件、快捷启动脚本和 overlay 文件。旧 per-profile `CODEX_HOME` 不会自动删除，只会作为 `LegacyCodexHome` 供检查和后续手动清理。

重命名配置显示名称：

```powershell
Set-CodexApiProfileName -Id "shuaiapi" -Name "ShuaiAPI 主力中转"
```

## 删除配置

只删除登记项和加密 Key：

```powershell
Remove-CodexApiProfile -Id "shuaiapi"
```

连 profile 文件一起删除：

```powershell
Remove-CodexApiProfile -Id "shuaiapi" -DeleteFiles
```

## 参考实现说明

外部 beta 项目只作为架构参考阅读。因为它当前没有明确仓库许可证，本项目没有复制或合并其源码，而是重新实现了最小可用版本。
