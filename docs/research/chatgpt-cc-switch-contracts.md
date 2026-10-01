# ChatGPT 与 CC Switch 配置启动契约

调查日期：2026-10-01

## 范围结论

- ChatGPT Desktop 是 Agent 软件。
- Codex 是 ChatGPT Desktop 的内置工具调用与 app-server 组件，不作为独立
  Agent 纳入本轮契约。
- CC Switch 是 Codex 等工具的供应商与配置管理器，不是 Agent 软件本体。
- 本文只记录路径、字段名、启动链和安全摘要，不记录令牌、密钥、完整配置
  内容或会话正文。

## 证据等级

- 本地事实：本机路径、文件、Junction、快捷方式、进程、版本信息和本机
  迁移脚本。
- 官方资料：CC Switch 官方 GitHub README。
- 未知项：官方未明确说明，且本机证据不足以确认的行为。

## ChatGPT Desktop

### 安装与版本

- 开始菜单入口：
  `C:\Users\Lumos\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\ChatGPT.lnk`
- 快捷方式目标：`E:\Codex\app\ChatGPT.exe`
- 快捷方式工作目录：`E:\Codex\app`
- 启动列表标识：`com.openai.codex`
- ChatGPT/Owl 应用版本：`26.908.40834`
- 桌面程序元数据版本：`152.0.7977.83`
- 当前 CLI Codex：`codex-cli 0.159.2`
- 桌面内置 Codex：`codex-cli 0.155.1`
- 当前运行中的 app-server daemon：
  `E:\Codex\.codex\packages\app-server-daemon\releases\0.157.1-x86_64-pc-windows-msvc\bin\codex.exe`

来源：

- ChatGPT 开始菜单快捷方式。
- `E:\Codex\app\resources\owl-app.ini`
- `E:\Codex\app\ChatGPT.exe` 文件版本信息。
- `E:\Codex\cli\codex.cmd --version`
- `E:\Codex\app\resources\codex.exe --version`
- `Get-CimInstance Win32_Process`

### 启动方式

当前实际启动链为：

1. 开始菜单启动 `E:\Codex\app\ChatGPT.exe`。
2. ChatGPT Desktop 启动
   `E:\Codex\app\resources\codex.exe app-server ...`。
3. app-server daemon 使用
   `E:\Codex\.codex\packages\app-server-daemon\releases\...` 下的
   `codex.exe`。
4. 进程环境中的 `CODEX_HOME` 为 `E:\Codex\.codex`。

来源：

- `Get-CimInstance Win32_Process` 的 ChatGPT 与 codex 进程链。
- 当前进程环境中的 `CODEX_HOME`。

### 配置目录

- 真实 Codex 配置环境：`E:\Codex\.codex`
- `%APPDATA%\Codex`：
  `C:\Users\Lumos\AppData\Roaming\Codex`，Junction
  指向 `E:\Codex\.codex`
- `%LOCALAPPDATA%\Codex`：
  `C:\Users\Lumos\AppData\Local\Codex`，Junction
  指向 `E:\Codex\.codex`
- `config.toml`：`E:\Codex\.codex\config.toml`
- 会话、技能、插件、运行时、SQLite 和历史状态均位于
  `E:\Codex\.codex` 或其子目录。

来源：

- `Get-Item` 的 Junction 元数据。
- `E:\Codex\.codex` 目录清单。

### 运行时依赖

- 桌面本体包含 Chromium/Electron 风格运行文件，例如 `chrome.dll`。
- 内置 Node.js：
  `E:\Codex\app\resources\cua_node\bin\node.exe`
- 内置文本搜索工具：
  `E:\Codex\app\resources\rg.exe`
- 内置 Codex：
  `E:\Codex\app\resources\codex.exe`
- 相关组件：
  `codex-code-mode-host.exe`、`codex-command-runner.exe`、
  `codex-windows-sandbox-setup.exe`
- `owl-shell-runtime.json` 声明 `msixPackageDependencies` 为空。
- 未发现 ChatGPT Desktop 直接依赖用户级 Python、Node.js 或 Git PATH。

来源：

- `E:\Codex\app` 与 `E:\Codex\app\resources` 文件清单。
- `E:\Codex\app\owl-shell-runtime.json`

### 迁移后必须更新

- 开始菜单快捷方式的目标和起始目录。
- 用户环境变量 `CODEX_HOME`。
- `config.toml` 中所有包含绝对路径的字段：
  - `notify`
  - `marketplaces.openai-bundled.source`
  - `mcp_servers.node_repl.command`
  - `mcp_servers.node_repl.env.NODE_REPL_NODE_MODULE_DIRS`
  - `mcp_servers.node_repl.env.NODE_REPL_NODE_PATH`
  - `mcp_servers.node_repl.env.NODE_REPL_TRUSTED_CODE_PATHS`
  - `mcp_servers.node_repl.env.CODEX_HOME`
  - `mcp_servers.node_repl.env.CODEX_CLI_PATH`
  - `mcp_servers.cua_repl.command`
- `%APPDATA%\Codex` 和 `%LOCALAPPDATA%\Codex` 两个兼容 Junction。
- 任何指向旧 `E:\Codex\.codex`、`E:\Codex\app` 或旧 AppData 路径的
  启动脚本和备份恢复配置。
- 如果移动 `E:\Codex\app`，ChatGPT 快捷方式与内置 Codex 的相对关系仍需
  保持为 `<app>\resources\codex.exe`。

来源：

- `config.toml` 字段扫描。
- ChatGPT 开始菜单快捷方式。
- `%APPDATA%\Codex`、`%LOCALAPPDATA%\Codex` Junction。

### 本机已存在的风险

- `config.toml` 仍明确包含 `E:\Codex\.codex` 和 `E:\Codex\app` 的绝对
  路径；如果再次移动目录而不更新这些字段，工具调用会再次失效。
- 当前桌面内置 Codex、daemon Codex 和 PATH Codex 是三个不同版本，健康
  检查必须证明实际启动链使用的是预期组件，不能只检查某一个 `codex.exe`
  是否存在。

## CC Switch

### 安装与识别

- 角色：多工具供应商与配置管理器。
- 可执行文件：`D:\Software\CCSwitch\cc-switch.exe`
- 版本：`3.20.4`
- 开始菜单入口：
  `C:\Users\Lumos\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\CC Switch\CC Switch.lnk`
- 快捷方式目标：`D:\Software\CCSwitch\cc-switch.exe`
- 快捷方式工作目录：`D:\Software\CCSwitch\`
- 启动列表标识：`com.ccswitch.desktop`
- 官方项目：https://github.com/farion1231/cc-switch

来源：

- CC Switch 开始菜单快捷方式。
- `cc-switch.exe` 文件版本信息。
- 官方 README 的项目描述与支持工具清单。

### 配置与数据目录

- 官方默认配置根：`C:\Users\<user>\.cc-switch`
- 本机兼容路径：`C:\Users\Lumos\.cc-switch`
- 本机实际存储：`E:\Codex\.cc-switch`
- 兼容路径类型：Junction。
- WebView/Tauri 用户数据兼容路径：
  - `C:\Users\Lumos\AppData\Local\com.ccswitch.desktop`
  - `C:\Users\Lumos\AppData\Roaming\com.ccswitch.desktop`
- 两个用户数据路径均为 Junction，实际指向：
  - `E:\Codex\.cc-switch\appdata\Local\com.ccswitch.desktop`
  - `E:\Codex\.cc-switch\appdata\Roaming\com.ccswitch.desktop`

官方 README 记录的主要文件：

- `cc-switch.db`：SQLite 数据库，保存供应商、MCP、提示词、技能、项目和
  用量记录等。
- `settings.json`：设备级设置和工具配置目录。
- OAuth 登录凭据文件。
- `live-state.json`：连接模式和上次写入状态。
- `codex-login-stash.json`：切换供应商时暂存的官方 Codex 登录。
- `~/.cc-switch/backups/live-first-write/`：首次改写工具配置前的恢复点。

来源：

- 官方 README 的 “Where is my data stored?”。
- `C:\Users\Lumos\.cc-switch` 与
  `C:\Users\Lumos\AppData\...\com.ccswitch.desktop` 的 Junction 元数据。
- `E:\Codex\.cc-switch` 目录清单。

### settings.json 契约

当前文件为 `E:\Codex\.cc-switch\settings.json`。

与本项目相关的字段名：

- `codexConfigDir`
- `skillStorageLocation`
- `currentProviderCodex`
- `visibleApps.codex`
- `enableLocalProxy`
- `localMigrations`

当前安全摘要：

- `codexConfigDir` 指向 `E:\Codex\.codex`。
- `visibleApps.codex=true`。
- `skillStorageLocation=cc_switch`。
- 未记录供应商密钥、认证或完整 provider 内容。

来源：

- `E:\Codex\.cc-switch\settings.json` 字段名和路径类值。

### 与 Codex 的关系

- 官方 README 说明 CC Switch 会对 Codex 的 JSON/TOML/YAML 配置进行
  provider 切换。
- 切换时主要替换端点、密钥、模型、协议和兼容选项；用户自行添加的插件、
  hooks、权限、MCP、环境变量和注释应保留。
- Codex 切换后通常需要重启终端或 CLI 工具。
- 开启本地路由后，Codex 配置可能指向
  `http://127.0.0.1:15721`，真实供应商信息保存在 CC Switch 中。
- 本机 `codexConfigDir` 明确指向 `E:\Codex\.codex`，因此 CC Switch 与
  ChatGPT Desktop 使用的是同一份 Codex 配置环境。

来源：

- 官方 README 的 Codex 切换、本地路由、共享配置和重启说明。
- 本机 `settings.json` 的 `codexConfigDir`。

### 迁移后必须更新

- 如果 `.cc-switch` 不再位于用户主目录默认位置，必须保留兼容
  Junction，或确认当前版本提供官方支持的数据根覆盖方式。
- `codexConfigDir` 必须指向迁移后的 Codex 配置环境。
- `%LOCALAPPDATA%\com.ccswitch.desktop` 和
  `%APPDATA%\com.ccswitch.desktop` 必须保留 Junction，或由程序重新指向
  迁移后的 AppData 子目录。
- 迁移前必须停止 CC Switch；数据库或 WebView 文件被占用时不可直接移动。
- 迁移后需要重启 CC Switch，并在切换 provider 后重启 Codex/终端。
- 备份和恢复流程必须覆盖 SQLite 主文件及可能的 `-wal`、`-shm` 文件。

来源：

- 本机迁移脚本：
  `E:\Codex\.cc-switch\backups\20260923-appdata-consolidate\_scripts\appdata-consolidate.ps1`
  和 `local-migrate.ps1`。
- 官方 README 的切换和重启说明。

## 组件交互与迁移顺序

从当前机器可确认的最小安全顺序为：

1. 停止 ChatGPT、Codex app-server 和 CC Switch。
2. 复制 Codex 配置根、应用目录和 CC Switch 配置根。
3. 校验版本、SQLite 文件和关键配置字段。
4. 更新 `CODEX_HOME`、开始菜单快捷方式和 `codexConfigDir`。
5. 重建 `%APPDATA%\Codex`、`%LOCALAPPDATA%\Codex` 及两个
   `com.ccswitch.desktop` 兼容 Junction，或在应用内部提供等价设置。
6. 先启动 CC Switch，再启动 ChatGPT Desktop。
7. 通过实际工具调用验证内置 Codex app-server，而不是只检查 exe 是否存在。

依据：

- 当前 ChatGPT、Codex daemon 和 CC Switch 同时运行。
- 两个 CC Switch AppData 目录曾被运行中的进程占用，之后通过停止进程、
  复制、改名和 Junction 完成迁移。
- 本机 CC Switch 迁移脚本记录。

## 仍未知

- 当前 CC Switch 是否正式支持通过环境变量重定向 `.cc-switch`；二进制中
  出现的 `CC_SWITCH_TEST_HOME` 更像测试入口，不能作为正式契约。
- ChatGPT Desktop 是否会在未来版本自动修复已移动的 `CODEX_HOME` 或
  快捷方式。
- ChatGPT Desktop 的内部配置格式没有稳定的公开迁移契约；本轮只依据本机
  文件和实际进程链。
- 当 `E:\Codex\app` 与 `E:\Codex\.codex` 被移动到不同父目录时，官方
  安装器是否仍能正确升级，尚未验证。
