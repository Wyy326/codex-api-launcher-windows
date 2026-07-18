# Codex API 多开启动器 for Windows

一个轻量 PowerShell 工具，用来在 Windows 上启动多个彼此隔离的 Codex CLI 实例。它面向第三方 OpenAI-compatible API provider，不做 ChatGPT 登录账号隔离。

每个 API 配置都会拥有独立的 `CODEX_HOME`、`config.toml`、API Key 环境变量、日志、sessions 和快捷启动脚本。API Key 不会写入 TOML、脚本或仓库文件。

## 文件

- `CodexApiLauncher.psm1` - API 配置管理模块。
- `Run-CodexApiProfile.ps1` - 生成的 profile 启动脚本会调用它。
- `CodexApiLauncher.UI.ps1` - 中文 Windows UI，可选择 API 配置和项目文件夹。
- `Launch UI.cmd` - 双击打开 UI 的入口。
- `templates/profile.config.toml` - 生成的 Codex 配置模板。
- `examples/create-profile.example.ps1` - 创建 profile 的示例脚本。

## 日常使用

双击仓库里的：

```text
Launch UI.cmd
```

也可以在 PowerShell 里运行：

```powershell
.\CodexApiLauncher.UI.ps1
```

UI 支持：

- 选择已有 API 配置
- 选择项目文件夹
- 在新的终端窗口里启动隔离的 Codex CLI
- 可选保存或清除某个配置的默认项目文件夹
- 打开该配置的 `CODEX_HOME`
- 运行 HTTP 连通性检查
- 运行真实 Codex CLI 检查，适合只允许 CLI 请求形态的中转网关

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
  -ApiKey $key
```

生成的 `config.toml` 形态：

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
