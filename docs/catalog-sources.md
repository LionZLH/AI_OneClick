# 收录软件与来源

核对日期：2026-10-15。winget 包 ID 对照 [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) 官方清单核对（版本号为当时清单中的最新版本），CI 每次构建时还会用 `winget show` 实际查询一遍。

| 软件 | 分组 | 安装方式 | 清单中的发布者 / 版本 |
| --- | --- | --- | --- |
| 豆包 | 国内可用 | winget `ByteDance.Doubao` | Beijing Chuntian Zhiyun Technology · 2.31.4 |
| Kimi | 国内可用 | winget `MoonshotAI.Kimi` | 北京月之暗面科技 · 3.2.16 |
| 腾讯元宝 | 国内可用 | winget `Tencent.Yuanbao` | 腾讯科技(深圳) · 2.88.1 |
| 千问 | 国内可用 | winget `Alibaba.Qianwen` | Alibaba Group · 2.3.1 |
| 智谱清言 | 国内可用 | winget `ZhipuAI.ChatGLM` | ZhipuAI · 2.0.6 |
| TRAE 国内版 | 国内可用 | winget `ByteDance.Trae.CN` | Beijing Yinli Catapult Technology · 3.4.3 |
| WPS Office | 国内可用 | winget `Kingsoft.WPSOffice.CN` | Kingsoft Corp. · 12.1.0.29161 |
| 剪映专业版 | 国内可用 | winget `ByteDance.JianyingPro` | 深圳市脸萌科技 · 11.6.0.14552 |
| Cherry Studio | 国内可用 | winget `kangfenmao.CherryStudio` | 安装包来自 github.com/CherryHQ · 2.1.6 |
| DeepSeek | 国内可用 | 网页版 chat.deepseek.com | winget 中仅有第三方打包，不收录 |
| 即梦 AI | 国内可用 | 网页版 jimeng.jianying.com | — |
| 秘塔 AI 搜索 | 国内可用 | 网页版 metaso.cn | — |
| ChatGPT | 需海外网络 | Microsoft Store `9NT1R1C2HH7J` | OpenAI |
| Claude | 需海外网络 | winget `Anthropic.Claude` | Anthropic, PBC · 2.19675.1 |
| Gemini | 需海外网络 | winget `Google.GeminiDesktop` | Google LLC · 1.15.0 |
| Microsoft Copilot | 需海外网络 | Microsoft Store `9NHT9RB2F4HD` | Microsoft |
| Cursor | 需海外网络 | winget `Anysphere.Cursor` | Anysphere · 3.19.7 |
| Antigravity | 需海外网络 | winget `Google.Antigravity` | Google · 2.23.1 |
| Perplexity | 需海外网络 | 网页版 perplexity.ai | winget 清单仅剩校验目录，不收录 |
| LM Studio | 本地运行 | winget `ElementLabs.LMStudio` | LM Studio · 0.4.26+4 |
| Ollama | 本地运行 | winget `Ollama.Ollama` | Ollama · 0.40.4 |
| GPT4All | 本地运行 | winget `nomic.gpt4all` | Nomic · 3.10.0 |
| Jan | 本地运行 | winget `Jan.Jan` | Jan · 0.8.6 |
| AnythingLLM | 本地运行 | 官网下载 | winget 中未收录 |
| 可灵 AI | 国内可用 | 网页版 klingai.kuaishou.com | 快手 |
| 海螺 AI | 国内可用 | 网页版 hailuoai.com | MiniMax |
| 腾讯 WorkBuddy | 国内可用 | winget `Tencent.WorkBuddy` | Tencent Technology (Shenzhen) · 5.6.2 |
| 腾讯 Ardot | 国内可用 | 网页版 ardot.tencent.com | 腾讯 |
| Notion | 需海外网络 | winget `Notion.Notion` | Notion Labs, Inc · 7.38.1 |
| Obsidian | 本地运行 | winget `Obsidian.Obsidian` | Dynalist Inc. · 1.14.4 |

说明：

- “国内可用”指在国内常规网络下可以直接使用，不保证每个地区、每条线路都可访问；“需海外网络”的软件需要用户自己具备相应网络和账号。
- 推荐规则是按用途匹配，不是模型能力排名，也不包含价格和额度的实时信息。
- 本地模型建议使用 Qwen3 系列（阿里通义开源，Apache-2.0），体积为 Ollama `qwen3:*` Q4 量化版本的大致大小。
