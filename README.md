# AgentEnvManager

面向 Windows 11 x64 的本地 Agent 环境管理器。用于扫描、纳管、切换、迁移、隔离删除工具运行时和 Agent 配置环境，并将已纳管工具运行时绑定给 Codex、ChatGPT 等 Agent 软件。

## 功能概览

- 只读环境体检：识别 PATH、系统路径、常见安装目录、注册表 App Paths、Codex、ChatGPT、WorkBuddy、CC Switch、Marvis 配置目录和 Conda 环境。
- 已纳管环境：环境身份、manifest、恢复点、稳定激活路径和受管入口。
- 运行时提供者：
  - Python：通过 `uv` 安装便携 CPython，支持官方镜像和离线导入。
  - Node.js：官方 ZIP 安装，独立 npm global/cache 前缀。
  - Git：官方 MinGit ZIP，凭据和 SSH 私钥不迁移、不读取。
  - PowerShell 7：官方便携 ZIP，版本级模块路径和 profile 状态。
- 制品缓存：官方 SHA-256 校验、镜像校验、离线导入和重复安装。
- Agent 适配器：Codex 和 ChatGPT 配置绑定、真实健康检查与回滚。
- 环境变量编辑器：只编辑管理器拥有的 `AGENT_ENV_MANAGER_*` 变量，并安全切换已纳管环境的受管 PATH 入口。
- 诊断包：本地生成、脱敏、可审阅，默认无遥测、无自动上传。
- 自包含发布：win-x64 便携版和当前用户安装脚本。

## 开发环境

- Windows 11 x64
- .NET 8 SDK
- PowerShell 7 或 Windows PowerShell 5.1

构建与测试：

```powershell
dotnet build AgentEnvManager.sln
dotnet test AgentEnvManager.sln
dotnet format AgentEnvManager.sln --verify-no-changes
```

## 使用

启动 WPF：

```powershell
dotnet run --project .\src\AgentEnvManager.Wpf
```

使用 CLI：

```powershell
dotnet run --project .\src\AgentEnvManager.Cli -- inspect
dotnet run --project .\src\AgentEnvManager.Cli -- adopt <fingerprint> --confirm
dotnet run --project .\src\AgentEnvManager.Cli -- runtimes
dotnet run --project .\src\AgentEnvManager.Cli -- runtime-install-preview python 3.13.7 --install-dir D:\AgentRuntimes\python-3.13.7
dotnet run --project .\src\AgentEnvManager.Cli -- runtime-install python 3.13.7 --install-dir D:\AgentRuntimes\python-3.13.7 --confirm
dotnet run --project .\src\AgentEnvManager.Cli -- runtime-import python 3.13.7 .\python.tar.gz --confirm
dotnet run --project .\src\AgentEnvManager.Cli -- migrate-preview <fingerprint> D:\Runtimes\python
dotnet run --project .\src\AgentEnvManager.Cli -- migrate <fingerprint> D:\Runtimes\python --confirm
dotnet run --project .\src\AgentEnvManager.Cli -- rollback-preview <operation-id>
dotnet run --project .\src\AgentEnvManager.Cli -- rollback <operation-id> --confirm
dotnet run --project .\src\AgentEnvManager.Cli -- agent-discover codex
dotnet run --project .\src\AgentEnvManager.Cli -- diagnostics-preview
```

安装、离线导入、迁移、回滚和 Agent 绑定等写操作均要求显式
`--confirm`。使用 `--help` 可查看完整命令与 Agent 绑定参数。

## 发布

生成自包含 win-x64 便携包和当前用户安装包：

```powershell
pwsh -NoProfile -File .\scripts\publish.ps1 `
  -Configuration Release `
  -Version 1.0.0 `
  -OutputRoot .\artifacts
```

产物：

- `artifacts/AgentEnvManager-1.0.0-win-x64-portable.zip`
- `artifacts/AgentEnvManager-1.0.0-win-x64-setup.zip`
- `artifacts/cli/AgentEnvManager.Cli.exe`

发布脚本会校验自包含运行时文件，发布后的程序不要求预装 .NET。

## 安装与卸载

安装包解压后执行：

```powershell
pwsh -NoProfile -File .\install.ps1 -Version 1.0.0
```

默认安装位置：

```text
%LOCALAPPDATA%\Programs\AgentEnvManager\app
```

默认卸载只移除程序文件，保留状态、工具运行时和恢复点：

```powershell
pwsh -NoProfile -File .\uninstall.ps1
```

清除管理器数据必须单独确认：

```powershell
pwsh -NoProfile -File .\clear-data.ps1 -ConfirmClearData
```

生命周期验收：

```powershell
pwsh -NoProfile -File .\scripts\verify-lifecycle.ps1
```

## 数据位置

默认管理器状态：

```text
%LOCALAPPDATA%\AgentEnvManager
```

默认环境数据根目录：

```text
%LOCALAPPDATA%\AgentEnvManager\data
```

可用环境变量覆盖：

```text
AGENT_ENV_MANAGER_HOME
AGENT_ENV_MANAGER_DATA_ROOT
```

管理器状态与运行时数据分开保存。升级和默认卸载不会自动移动、覆盖或删除工具运行时、Agent 绑定和恢复点。

## 安全边界

- 所有写操作先生成计划，并记录操作状态、影响范围和恢复信息。
- 系统组件、Windows PowerShell 5.1、cmd 和仅观测环境不会被迁移或删除。
- Git 凭据、令牌和 SSH 私钥不进入迁移、诊断包或自动备份。
- 诊断包默认关闭遥测和自动上传，导出目标必须是本地路径。
- 删除默认进入隔离区；永久清除需要二次确认。

## 验证清单

```powershell
dotnet test AgentEnvManager.sln -c Release
dotnet format AgentEnvManager.sln --verify-no-changes
pwsh -NoProfile -File .\scripts\publish.ps1 -Version verify -OutputRoot .\artifacts
pwsh -NoProfile -File .\scripts\verify-lifecycle.ps1
.\artifacts\cli\AgentEnvManager.Cli.exe inspect
```
