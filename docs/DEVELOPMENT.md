# AI Station 开发说明

## 1. 架构

```text
AIStation.exe（C# 单文件，.NET Framework 4.x）
├── 托盘（WinForms NotifyIcon）：打开窗口 / 快速启动 / 开机自启 / 退出
├── 本地 HTTP 服务（127.0.0.1 随机端口，TcpListener 实现，无需管理员权限）
│   ├── 静态文件：ui/ 下的界面，编译时嵌入 exe 资源
│   └── /api/*：状态、任务、启动、配置等接口（需要启动时生成的随机令牌）
├── 后台检测：硬件、网络连通、winget 版本、已安装软件
└── 任务队列：winget / Store 安装、更新、卸载，离线包安装，Ollama 下载模型，GGUF 导入

界面窗口：Edge（找不到时 Chrome）以 --app 模式打开，使用独立配置目录
          %LOCALAPPDATA%\AIStation\WebView；都没有时用默认浏览器。
```

为什么不用 WinForms/WPF 画界面：HTML/CSS 能做出质量高得多的界面，而且界面和推荐逻辑能在任何系统上用浏览器自动化测试。系统自带的 Edge 不需要打包浏览器内核，exe 仍是零依赖的单文件。

## 2. 目录

```text
src/                C# 源码（必须保持 C# 5 语法，见第 6 节）
  Program.cs        入口、命令行参数、单实例、自检（--selftest）
  Core.cs           目录加载、状态、用户配置、离线包、网络探测、启动软件、图标提取
  Detector.cs       已安装识别：注册表卸载项 + 开始菜单应用（Shell AppsFolder）+ 商店包目录 + 常见路径
  Hardware.cs       内存、AVX2、显卡/显存、系统版本、架构、磁盘
  JobRunner.cs      任务队列、winget 调用与输出解析、返回码翻译、模型下载/导入
  HttpServer.cs     极简 HTTP/1.1 服务器
  Api.cs            接口路由与安全校验
  Shell.cs          Edge 应用窗口、托盘、开机自启
ui/
  catalog.json      软件目录（C# 和界面共用的唯一数据源）
  recommend.js      推荐引擎（纯函数，浏览器和 Node 通用）
  app.js            界面（无框架：状态 → 模板字符串 → 事件委托）
  styles.css        样式（浅色 / 深色自动跟随系统）
  index.html
assets/app.ico      程序图标
tests/
  recommend.test.cjs / catalog.test.cjs   单元测试
  e2e.mjs           端到端界面测试（Playwright）
  fake-winget.sh    非 Windows 环境下的 winget 替身
  check-winget-ids.ps1  核对目录中所有 winget / Store 包 ID
  real-install.ps1  真实安装 → 识别 → 启动 → 卸载
  window-smoke.ps1  正常启动并截取屏幕，确认 Edge 应用窗口能打开
build.cmd           Windows 编译（系统自带 csc）
build.sh            Mono 编译（开发调试用）
.github/workflows/windows.yml   CI
```

## 3. 命令行参数

| 参数 | 作用 |
| --- | --- |
| `--minimized` | 只显示托盘，不打开窗口（开机自启用） |
| `--headless` | 不显示托盘和窗口，只运行本地服务，并把访问地址打印到标准输出（测试用） |
| `--port N` | 指定端口（默认随机） |
| `--token T` | 指定访问令牌（默认随机，测试用） |
| `--ui-dir 路径` | 从磁盘读取界面文件而不是嵌入资源（改界面时免重新编译） |
| `--data-dir 路径` | 指定数据目录（默认 `%LOCALAPPDATA%\AIStation`） |
| `--selftest 文件` | 在本机检测各项能力，把结果写成 JSON 后退出 |
| `--install-self` | 安装到本机（复制程序、快捷方式、登记到系统应用列表）后退出 |
| `--uninstall-self [--quiet]` | 从本机卸载 AI Station 本身 |
| `--wait-pid N` | 等进程 N 退出后再启动（安装到本机后接管用） |

测试用环境变量：`AISTATION_WINGET`（替换 winget）、`AISTATION_OLLAMA`（指定 ollama.exe）、`AISTATION_LMS`（指定 lms.exe）、`AISTATION_NETWORK`（固定网络探测结果：cn / global / offline）。

## 4. 接口

所有 `/api/*` 请求必须带 `X-AIStation-Token` 请求头（图标接口允许 `?t=` 参数），并且 `Host` 必须是 `127.0.0.1:端口`，用来防止任意网页跨站调用本机接口或 DNS 重绑定。

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/state` | 全部状态：目录、硬件、网络、已安装、任务、配置、winget、离线包 |
| GET | `/api/jobs` | 任务与已安装状态（轮询用的轻量接口） |
| GET | `/api/icon?id=` | 已安装软件的图标（PNG） |
| GET | `/api/show` | 让已运行的实例打开窗口（第二次双击 exe 时使用） |
| POST | `/api/jobs` | `{jobs:[{action, appId, arg?, target?}]}`，action：install / upgrade / uninstall / pull-model / import-model |
| POST | `/api/jobs/cancel` `/api/jobs/clear` | 取消排队中的任务 / 清除已结束的任务 |
| POST | `/api/launch` | `{id}` 启动软件；网页版用默认浏览器打开 |
| POST | `/api/open` | `{id, target: home/web}` 打开官网；`{target: "winget-store"}` 打开应用安装程序商店页 |
| POST | `/api/profile` | 更新配置（onboarded、answers、recommendation、webShortcuts 等白名单字段） |
| POST | `/api/web-shortcut` | `{id, add}` 网页版加入 / 移出「我的 AI」 |
| POST | `/api/refresh` `/api/network` `/api/autostart` `/api/ping` `/api/open-data-dir` | 重新识别 / 重新探测网络 / 开机自启 / 窗口心跳 / 打开数据目录 |

## 5. 关键实现说明

**已安装识别**（`Detector.cs`）：用目录里的 `detect.displayName` 正则匹配
`HKCU/HKLM(64/32 位视图)\...\Uninstall` 的 DisplayName，再匹配「开始」菜单应用列表（与 PowerShell `Get-StartApps` 同源，包含商店应用，可得到 AUMID）。启动时优先用解析出的 exe 路径，没有就用 `explorer.exe shell:AppsFolder\<AUMID>`，商店应用（ChatGPT、Copilot）走后者。

**winget 调用**（`JobRunner.cs`）：
`winget install --id <ID> --exact --source winget|msstore --silent --accept-source-agreements --accept-package-agreements [--disable-interactivity]`。
标准输出逐字符读取，`\r` 刷新的进度行解析为百分比（`12.0 MB / 100 MB` 或 `45%`）。实测 winget 的输出被重定向时通常不打印进度条，此时界面显示“进行中”动画和阶段文字（下载中 / 安装中），不虚构数字。常见返回码（已安装、已是最新、下载失败、哈希不匹配、软件正在运行等）翻译成中文提示，未知错误显示十六进制返回码和原始输出。

**下载进度**：winget 的输出被重定向时不打印进度条，所以进度改为实测：安装前先运行 `winget show` 得到本机将使用的安装包地址，用 HEAD（或 Range 请求）读出文件总大小；winget 运行期间，每 0.4 秒测量它写入 `%TEMP%\WinGet\<包ID>.<版本>\` 的安装包大小，计算百分比、速度（指数平滑）和剩余时间。文件达到总大小后进入“安装”阶段，显示已用时间。查不到总大小时只显示已下载量和速度；商店应用没有下载文件，只显示阶段。

**卸载**：winget 卸载时只运行注册表中的普通 `UninstallString`，NSIS / Inno 打包的软件会弹出完整的卸载向导（CI 中 Jan 曾卡在“Click Uninstall to start”）。因此卸载顺序是：
1. 先检查安装目录下是否有进程在运行（很多软件关掉窗口后仍在托盘或后台）。卸载确认框已告知用户会自动关闭，因此卸载任务带 `arg: "close"`：先 `CloseMainWindow` 正常关闭，5 秒后仍在的进程强制结束。更新任务不会擅自关闭，而是失败并标记 `hint: "running"`，界面提供“关闭并重试”；
2. 注册表有 `QuietUninstallString` 就用它；否则按 `installerType` 追加 `/S`（NSIS）、`/VERYSILENT`（Inno）或改为 `msiexec /x … /qn`；
3. 商店应用和类型不明的软件交给 `winget uninstall`；
4. NSIS 卸载程序会复制到临时目录后在后台继续执行，所以命令返回后会反复重新识别，直到软件真正消失才标记“已卸载”。

**硬件检测**（`Hardware.cs`）：内存用 `GlobalMemoryStatusEx`；AVX2 用 `IsProcessorFeaturePresent(40)`，该常量从 Windows 10 2004（build 19041）起才可靠，更老的系统返回“无法确认”；显存读显卡驱动注册表 `HardwareInformation.qwMemorySize`（WMI 的 AdapterRAM 最多只到 4GB）；Windows 11 的 ProductName 仍写着 Windows 10，按 build ≥ 22000 修正。

**推荐引擎**（`ui/recommend.js`）：
1. 按网络和硬件过滤：需海外网络的软件只在“可以访问海外服务”时出现；完全离线只出本地软件；CPU 不支持 AVX2 时排除 LM Studio；内存明显不足时排除本地软件。
2. 每款软件对 8 个用途有 0–3 分（`catalog.json` 的 `scores`），≥2 分才算适合，理由取自 `why`。
3. “简单省心”和“隐私优先”用贪心覆盖：每轮挑能覆盖最多剩余用途的软件，最多 3 / 4 款；“能力全面”每个用途取前两名，最多 6 款。
4. 无法覆盖的用途会说明原因；推荐了本地软件时附带模型规格建议（按内存 / 独显显存分 1.7B / 4B / 8B / 14B 四档）。

**软件图标**：`tools/fetch_icons.py` 从官网读取 `iconUrl`（目录中可选）、网页声明的 SVG / apple-touch-icon / icon 链接和 `favicon.ico`，选矢量或分辨率最高者，去掉透明留白后统一为 128px PNG，写入 `ui/icons/<id>.png` 和 `ui/icons/index.json`（`full` 表示自带底色的满版图标）。`.github/workflows/icons.yml` 在目录或脚本变化时自动运行并提交结果。图标显示优先级：内置官方图标 → 已安装软件的程序图标 → 文字头像。

**使用须知**：首次打开界面时弹出（`profile.disclaimerAccepted` 记录是否已同意），文字在 `ui/app.js` 的 `DISCLAIMER` 常量中，设置页显示全文。

**安装到本机**（`SelfInstall.cs`）：复制到 `%LOCALAPPDATA%\Programs\AIStation\AIStation.exe`，用 `WScript.Shell` 创建桌面和开始菜单快捷方式，在 `HKCU\...\Uninstall\AIStation` 登记（`UninstallString` 为 `--uninstall-self`）。从界面安装后，新位置的程序以 `--wait-pid` 启动、等旧进程退出后接管。命令行 `--install-self` / `--uninstall-self [--quiet]` 供 CI 和系统“应用”列表使用。离线包（程序旁有 apps/ 或 models/）不提示安装到本机，因为离线资源在 U 盘上。

**安装前检查与失败自助**：`ui/app.js` 的 `preCheck()` 在安装前汇总阻止项（Windows 版本过旧、没联网、CPU 不支持 AVX2）和提醒（无 winget、海外服务不可达、内存小、磁盘少、需海外账号 / API Key）。后端在下载前检查系统盘空间（至少 1GB 且不少于安装包 3 倍）。任务失败时带 `hint`（disk / network / uac / blocked / hash / unsupported / reboot / nowinget / running / lms-missing / unknown），界面按 `HINTS` 显示对应说明和操作按钮。

**问题报告**（`Diagnostics.cs`）：zip 内含 `report.json`（状态快照，不含目录）、`aistation.log`、`winget --info` 输出和说明文件，保存到桌面并在资源管理器中选中。

**本地模型**：“一键准备”= 依次排队 `install ollama` 和 `pull-model`；`/api/local-models` 运行 `ollama list` 返回已有模型，界面据此显示“下载”或“开始对话”（打开 Ollama 自带的聊天窗口）。LM Studio 通过其命令行 `lms get <模型> --yes` 下载（需要先打开过一次 LM Studio），CI 中的 `tests/probe-lmstudio.ps1` 用于核实其位置和参数。

**费用与账号**：`catalog.json` 中每款软件有 `pricing`（free / freemium / paid）、`account`（phone / overseas / microsoft / optional / none / apikey）和 `pricingNote`，标签文字在 `pricing` / `accounts` 字典中。

## 6. 维护注意事项

- **C# 只能用 C# 5 语法**：Windows 自带的 csc.exe 版本很老。不要用 `$"..."`、`?.`、`nameof`、`=>` 成员、自动属性初始化、`out var` 等。`build.sh` 用 `-langversion:5` 编译，能提前发现问题。
- **源文件编码**：源码是无 BOM 的 UTF-8，`build.cmd` 已加 `/codepage:65001`，否则中文系统上中文字符串会乱码。
- **TLS**：未声明目标框架时 .NET 4.x 默认不启用 TLS 1.2，`Program.cs` 中已显式开启。
- **PowerShell 脚本**（tests/*.ps1）保存为带 BOM 的 UTF-8，否则 Windows PowerShell 5.1 会按 GBK 读取导致乱码。
- **界面改动**：开发时用 `--ui-dir ui` 直接读取磁盘文件，刷新窗口即可看到效果。

## 7. 新增一款软件

只需要修改 `ui/catalog.json`，C# 和界面都从这里读取：

```json
{
  "id": "example", "name": "示例 AI", "vendor": "厂商", "kind": "desktop", "region": "cn",
  "letter": "示", "color": "#3366ff",
  "summary": "一句话介绍。",
  "scores": { "chat": 2, "docs": 3 },
  "why": { "chat": "为什么适合聊天", "docs": "为什么适合读文档" },
  "notes": ["需要登录账号"],
  "homepage": "https://example.com",
  "install": { "source": "winget", "id": "Vendor.Example" },
  "detect": { "displayName": "^示例 AI", "exe": ["Example.exe"] },
  "offline": { "match": ["example"], "installerType": "nullsoft" }
}
```

- `region`：`cn` 国内可用 / `global` 需海外网络 / `local` 本地运行（本地软件需要 `requires: {ramGB, avx2}`）。
- `install.source`：`winget`、`msstore`（ID 为 12 位商店 ID）、`web`（需要 `webUrl`）、`manual`（只能去官网下载）。
- winget ID 务必在 [winget-pkgs](https://github.com/microsoft/winget-pkgs/tree/master/manifests) 中核对，并确认发布者是官方；`installerType` 从该包清单的 `InstallerType` 字段获取。
- `detect.displayName` 是匹配“设置 → 应用”中显示名的正则；`exe` 是主程序文件名。
- 运行 `npm test` 会校验字段完整性、链接、评分与理由是否对应；CI 会用 winget 实际查询每个 ID。

## 8. 后续方向

- **Skill 管理**：在侧栏增加新模块即可，接口按 `/api/skills/*` 扩展，数据可沿用 catalog.json 的结构单独建 `skills.json`。
- 软件更新检查可以改为解析 `winget upgrade` 的结果，在启动台上标出可更新的软件。
- macOS 版本。
