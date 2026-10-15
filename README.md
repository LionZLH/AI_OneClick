# AI Station

Windows 上的 **AI 软件启动器与管理工具**：帮你挑选合适的 AI 软件，一键装好，之后在一个地方统一启动、更新和卸载。

- **首次配置**：回答 3 个问题（用途 / 网络 / 偏好），结合这台电脑的真实硬件给出推荐；也可以跳过问卷，自己在软件库里挑。
- **我的 AI（启动台）**：已安装的 AI 软件和收藏的网页版集中在一起，点击即启动；托盘右键也能快速启动。
- **软件库**：24 款精选 AI 软件（国内可用 / 需海外网络 / 本地运行），单个安装或勾选后批量安装。
- **下载与更新**：安装、更新、卸载、模型下载排队执行。安装分“查询 → 下载 → 安装”三个阶段，下载阶段显示实测的百分比、已下载 / 总大小、速度和剩余时间。
- **卸载**：软件正在运行（包括关掉窗口后留在托盘或后台的情况）时，确认后会自动关闭再静默卸载。
- **本地模型**：按内存、显存推荐 Qwen3 模型规格，可通过 Ollama 一键下载，或从离线包导入 GGUF 文件。

## 使用

1. 下载 `AIStation.exe`（GitHub Actions 每次构建的 `AIStation-exe` 产物，或自己编译，见下文）。
2. 双击运行。程序会在托盘常驻，并用系统自带的 Edge 打开一个应用窗口。
3. 首次打开会进入配置向导：推荐「帮我推荐」，也可以「我自己挑」。

**运行要求**：Windows 10 / 11，系统自带的 .NET Framework 4.x 即可（Windows 10 1903 之后都已内置），不需要安装任何运行库。自动安装依赖系统自带的 winget（“应用安装程序”）；没有 winget 时，程序会提示从 Microsoft Store 获取，或改从官网下载。

**数据位置**：`%LOCALAPPDATA%\AIStation\`（配置 `profile.json`、日志 `logs\aistation.log`）。

## 离线 / 内网电脑

把程序和安装包、模型一起拷过去即可，目录结构：

```text
AI Station 离线包/
├── AIStation.exe
├── apps/      各软件的官方安装包（.exe / .msi），文件名包含软件名即可识别，如 LM-Studio-0.4.26-x64.exe
└── models/    GGUF 模型文件，如 Qwen3-8B-Q4_K_M.gguf
```

- 软件库中有对应离线安装包的软件，会优先用离线包静默安装（NSIS / Inno / MSI 安装包自动静默；其他类型会打开安装界面让你点下一步）。
- 「本地模型」页会列出 `models/` 中的模型，可一键导入 LM Studio、Ollama 或 GPT4All。
- 问卷选择「完全离线」时只推荐本地运行的软件。

## 编译

不需要 Visual Studio。在 Windows 上双击或在命令行运行：

```bat
build.cmd
```

产物为 `dist\AIStation.exe`。它调用的是 Windows 自带的 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，界面文件（`ui/`）会作为资源嵌入 exe，单文件即可分发。

## 开发与测试

```bash
npm test                 # 推荐引擎与软件目录单元测试（Node 18+）
./build.sh               # 在 Linux / macOS 上用 Mono 编译（仅用于开发）
node tests/e2e.mjs       # 端到端界面测试（Linux 下用模拟 winget）
```

每次推送都会在 GitHub Actions 的 Windows 机器上：用系统自带 csc 编译 → 自检 → 核对全部 winget 包 ID → 真实安装/启动/卸载一个软件 → 跑界面端到端测试并截图。

详细的架构、接口与维护说明见 [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)，收录软件与来源见 [docs/catalog-sources.md](docs/catalog-sources.md)。

## 边界与免责

首次启动会显示「使用须知」，需要同意后才能使用，设置页可随时查看全文。要点：

- AI Station 只是软件的下载、安装和启动工具，**不提供、不内置、也不推荐任何代理、VPN 或其他跨境网络访问服务**。
- “需海外网络”只表示该软件的服务器位于境外；能否使用取决于用户所在地区的网络环境和该服务的地区政策，用户需遵守所在地法律法规。
- 网络检测只连接 www.baidu.com 和 www.google.com 判断连通性，不收集、不上传任何数据。
- 只通过官方渠道（winget 官方源、Microsoft Store、各软件官网）安装软件。
- 软件图标由 `tools/fetch_icons.py` 从各软件官网获取，仅用于在启动器中标识对应软件，商标归各自所有者。
- 各软件的账号、费用、可用地区和数据政策以其官方说明为准。
- macOS 版本尚未重做，旧的 macOS 脚本暂存在 `scripts/macos/`，未随本次重构更新。
