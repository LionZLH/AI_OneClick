const AIStation = (() => {
  const apps = [
    ['deepseek',    'DeepSeek',    'D',  'blue',   'domestic', 'desktop', '深度求索',     '推理问答与桌面任务，兼顾思考和动手。',         ['推理问答', '桌面任务'],      'https://www.deepseek.com/download/',  '桌面产品为 DeepSeek Harness；具体芯片、系统与账号要求见官网。'],
    ['doubao',      '豆包',        '豆', 'peach',  'domestic', 'desktop', '字节跳动',     '日常问答、内容写作和语音交流。',               ['日常助手', '语音交流'],      'https://www.doubao.com/download',     '需联网及账号；功能和额度以当前版本为准。'],
    ['kimi',        'Kimi',        'K',  'white',  'domestic', 'desktop', '月之暗面',     '梳理长文档、提炼资料，让信息更好消化。',       ['文档阅读', '资料整理'],      'https://www.kimi.com',                '从官网选择客户端或使用网页版；文档容量受套餐限制。'],
    ['yuanbao',     '腾讯元宝',    '元', 'green',  'domestic', 'desktop', '腾讯',         '联网查资料，查找公众号内容与中文信息。',       ['中文搜索', '公众号'],        'https://yuanbao.tencent.com/download','搜索结果仍需核对原文；需联网及账号。'],
    ['qianwen',     '千问',        '千', 'violet', 'domestic', 'web',     '阿里巴巴',     '中文问答、内容整理与日常办公。',               ['办公助手', '内容整理'],      'https://www.qianwen.com',             '此处提供网页入口；转写、会议等功能是否可用以官网为准。'],
    ['zhipu',       '智谱清言',    '智', 'blue',   'domestic', 'web',     '智谱 AI',      '通过问答与智能体辅助写作、学习。',             ['写作', '智能体'],            'https://chatglm.cn',                  '此处提供网页入口，不计作已确认的桌面安装包。'],
    ['spark',       '讯飞星火',    '讯', 'blue',   'domestic', 'web',     '科大讯飞',     '辅助组织材料、起草公文与润色表达。',           ['公文写作', '材料整理'],      'https://xinghuo.xfyun.cn',            '此处提供网页入口；正式材料需人工复核。'],
    ['minimax',     '海螺 AI',     '海', 'peach',  'domestic', 'web',     'MiniMax',      '从文字和参考图出发，探索 AI 视频创作。',       ['视频创作', '视觉素材'],      'https://hailuoai.com',                '此处提供网页入口；生成额度和商用条款以官网为准。'],
    ['trae',        'TRAE',        'T',  'green',  'domestic', 'desktop', '字节跳动',     '围绕真实项目写代码、调试和协作开发。',         ['编程', 'AI 编辑器'],         'https://www.trae.cn',                 '这里指 TRAE 国内版；需联网，模型与额度以官网为准。'],
    ['chatgpt',     'ChatGPT',     '✳', 'green',  'global',   'desktop', 'OpenAI',       '通用问答、写作、文件分析与语音交流。',         ['通用助手', '文件分析'],      'https://chatgpt.com/download/',       '需受支持地区的网络与账号；不同系统和套餐的功能可能不同。'],
    ['claude',      'Claude',      'C',  'peach',  'global',   'desktop', 'Anthropic',    '长文写作、资料分析与代码讨论。',               ['长文写作', '分析'],          'https://claude.com/download',         '需受支持地区的网络与账号；功能和额度以官网为准。'],
    ['gemini',      'Gemini',      '✦', 'blue',   'global',   'web',     'Google',       '结合文本与多模态资料，辅助理解与整理。',       ['多模态', '资料分析'],        'https://gemini.google.com',           '此处提供网页入口；需受支持地区的网络与 Google 账号。'],
    ['perplexity',  'Perplexity',  'P',  'teal',   'global',   'web',     'Perplexity',   '围绕问题检索资料，沿引用继续追溯来源。',       ['研究搜索', '来源引用'],      'https://www.perplexity.ai',           '此处提供网页入口；引用不代表结论一定准确。'],
    ['antigravity', 'Antigravity', 'A',  'white',  'global',   'desktop', 'Google',       '用智能体参与项目开发与复杂工程任务。',         ['编程', '智能体'],            'https://antigravity.google/download', '需受支持的网络与账号；仍需审查智能体的代码和操作。'],
    ['cursor',      'Cursor',      '↗', 'white',  'global',   'desktop', 'Anysphere',    '在代码编辑器中完成补全、修改和项目问答。',     ['编程', 'AI 编辑器'],         'https://cursor.com/download',         '需联网及账号；部分功能需要付费。'],
    ['lmstudio',    'LM Studio',   'LM', 'violet', 'offline',  'desktop', 'Element Labs', '在图形界面里管理模型，运行本地问答。',         ['本地模型', '离线问答'],      'https://lmstudio.ai/download',        '需提前准备模型及运行时；本项目未附带模型，速度取决于硬件。'],
    ['anythingllm', 'AnythingLLM', 'a',  'white',  'offline',  'desktop', 'Mintplex Labs','围绕自己的文件，搭建本地知识库问答。',         ['私有知识库', '文档问答'],    'https://anythingllm.com/download',    '断网前需准备本地对话、嵌入模型及相关组件；勿选择云端提供商。'],
    ['jan',         'Jan',         'J',  'peach',  'offline',  'desktop', 'Menlo Research','通过桌面界面运行本地模型与私有聊天。',        ['本地模型', '私有聊天'],      'https://www.jan.ai/download',         '本地模式需提前下载模型；云端模型连接不属于离线使用。'],
    ['gpt4all',     'GPT4All',     'G',  'green',  'offline',  'desktop', 'Nomic',        '本地聊天与文档问答，支持多种开源模型。',       ['本地聊天', '文档问答'],      'https://www.nomic.ai/gpt4all',        '需提前下载适配模型；CPU 是否可用及速度取决于硬件和模型。'],
  ].map(([id, name, mark, color, group, type, vendor, desc, tags, url, note]) =>
    ({ id, name, mark, color, group, type, vendor, desc, tags, url, note, systems: ['windows', 'macos'] }));

  const networks = { domestic: '国内普通网络', global: '可访问海外服务', offline: '完全离线' };
  const systems  = { windows: 'Windows', macos: 'macOS' };
  const groups   = { domestic: '国内常用', global: '海外服务', offline: '本地离线' };

  const scenes = [
    ['code',      '写代码与开发',   '从代码问答到项目开发',     'code',       'trae',        'cursor',      'lmstudio'   ],
    ['doc',       '读文档与研报',   '提取重点，梳理长篇资料',   'file',       'kimi',        'claude',      'anythingllm'],
    ['meeting',   '整理会议纪要',   '整理已有转写稿与会议要点', 'mic',        'qianwen',     'chatgpt',     'lmstudio'   ],
    ['official',  '写作与办公',     '起草材料，润色日常表达',   'pen',        'spark',       'claude',      'lmstudio'   ],
    ['search',    '研究与深度搜索', '检索信息，追溯原始来源',   'search',     'yuanbao',     'perplexity',  null         ],
    ['wechat',    '找公众号素材',   '搜索中文内容与公众号文章', 'chat',       'yuanbao',     'yuanbao',     null         ],
    ['logic',     '推理与解题',     '拆解问题，讨论解题思路',   'spark',      'deepseek',    'chatgpt',     'lmstudio'   ],
    ['voice',     '日常与语音交流', '随手问答，练习对话表达',   'headphones', 'doubao',      'chatgpt',     null         ],
    ['local_kb',  '建私有知识库',   '围绕自己保存的文件问答',   'library',    'anythingllm', 'anythingllm', 'anythingllm'],
    ['local_llm', '运行本地模型',   '在自己的电脑上运行模型',   'chip',       'lmstudio',    'lmstudio',    'lmstudio'   ],
  ].map(([id, name, desc, icon, domestic, global, offline]) => ({ id, name, desc, icon, picks: { domestic, global, offline } }));

  // ── 安装元数据 ────────────────────────────────────────────────────────────────
  // winget:     Windows 包管理器 ID（✓ 表示已通过 WinGet 目录核实）
  // brew:       macOS Homebrew Cask 名称
  // direct:     无包管理器时的官方下载页回退
  // isLocalLLM: 安装后需注入 GGUF 模型文件
  // webOnly:    纯网页入口，无需安装任何软件
  const installMeta = {
    deepseek:    { winget: null,                       brew: null,           direct: 'https://www.deepseek.com/download/',      isLocalLLM: false },
    doubao:      { winget: 'ByteDance.Doubao',         /* ✓ */ brew: null,   direct: 'https://www.doubao.com/download',          isLocalLLM: false },
    kimi:        { winget: 'MoonshotAI.Kimi',          /* ✓ */ brew: null,   direct: 'https://www.kimi.com',                     isLocalLLM: false },
    yuanbao:     { winget: 'Tencent.Yuanbao',          /* ✓ */ brew: null,   direct: 'https://yuanbao.tencent.com/download',     isLocalLLM: false },
    trae:        { winget: 'ByteDance.Trae',           /* ✓ */ brew: null,   direct: 'https://www.trae.cn',                      isLocalLLM: false },
    chatgpt:     { winget: 'OpenAI.ChatGPT',                   brew: 'chatgpt',     direct: 'https://chatgpt.com/download/',     isLocalLLM: false },
    claude:      { winget: 'Anthropic.Claude',         /* ✓ */ brew: 'claude',      direct: 'https://claude.com/download',       isLocalLLM: false },
    antigravity: { winget: 'Google.Antigravity',       /* ✓ */ brew: 'antigravity', direct: 'https://antigravity.google/download', isLocalLLM: false },
    cursor:      { winget: 'Anysphere.Cursor',         /* ✓ */ brew: 'cursor',      direct: 'https://cursor.com/download',       isLocalLLM: false },
    lmstudio:    { winget: 'ElementLabs.LMStudio',             brew: 'lm-studio',   direct: 'https://lmstudio.ai/download',      isLocalLLM: true  },
    anythingllm: { winget: 'Mintplex.AnythingLLM',             brew: 'anythingllm', direct: 'https://anythingllm.com/download',  isLocalLLM: false },
    jan:         { winget: null,                               brew: 'jan',         direct: 'https://www.jan.ai/download',       isLocalLLM: true  },
    gpt4all:     { winget: 'Nomic.GPT4All',                    brew: 'gpt4all',     direct: 'https://www.nomic.ai/gpt4all',      isLocalLLM: true  },
    // 纯网页入口
    qianwen: { webOnly: true }, zhipu: { webOnly: true }, spark: { webOnly: true },
    minimax: { webOnly: true }, gemini: { webOnly: true }, perplexity: { webOnly: true },
  };

  // ── 模型档位（按内存从高到低，安装脚本取第一个满足 minRam 的档位）──────────
  // lmsId: LM Studio CLI `lms get <lmsId>` 使用的路径
  const modelTiers = [
    { minRam: 32, label: '高配（14B）', file: 'Qwen2.5-14B-Instruct-Q4_K_M.gguf', size: '9.0 GB', lmsId: 'lmstudio-community/Qwen2.5-14B-Instruct-GGUF/Qwen2.5-14B-Instruct-Q4_K_M.gguf' },
    { minRam: 16, label: '标准（7B）',  file: 'Qwen2.5-7B-Instruct-Q4_K_M.gguf',  size: '4.7 GB', lmsId: 'lmstudio-community/Qwen2.5-7B-Instruct-GGUF/Qwen2.5-7B-Instruct-Q4_K_M.gguf'  },
    { minRam: 8,  label: '入门（1.5B）',file: 'Qwen2.5-1.5B-Instruct-Q4_K_M.gguf', size: '1.1 GB', lmsId: 'lmstudio-community/Qwen2.5-1.5B-Instruct-GGUF/Qwen2.5-1.5B-Instruct-Q4_K_M.gguf' },
    { minRam: 4,  label: '轻量（0.5B）',file: 'Qwen2.5-0.5B-Instruct-Q4_K_M.gguf', size: '0.4 GB', lmsId: 'lmstudio-community/Qwen2.5-0.5B-Instruct-GGUF/Qwen2.5-0.5B-Instruct-Q4_K_M.gguf' },
  ];

  // ── 核心逻辑 ──────────────────────────────────────────────────────────────────
  function unavailable(app, system, network) {
    if (!systems[system] || !networks[network]) throw new Error('未知的系统或网络选项');
    if (!app.systems.includes(system)) return '当前系统暂不支持';
    if (network === 'offline' && app.group !== 'offline') return '需要联网使用';
    if (network === 'domestic' && app.group === 'global') return '需可访问海外服务的网络';
    return '';
  }
  function recommend(sceneIds, system, network) {
    if (!systems[system] || !networks[network]) throw new Error('未知的系统或网络选项');
    const matches = new Map(), unsupported = [];
    for (const id of new Set(sceneIds)) {
      const scene = scenes.find(item => item.id === id);
      if (!scene) throw new Error('未知场景：' + id);
      const app = apps.find(item => item.id === scene.picks[network]);
      if (!app || unavailable(app, system, network)) { unsupported.push(scene.name); continue; }
      if (!matches.has(app.id)) matches.set(app.id, { app, reasons: [] });
      matches.get(app.id).reasons.push(scene.name);
    }
    return { matches: [...matches.values()], unsupported };
  }
  function filterApps({ system, network, group = 'all', query = '', desktopOnly = false }) {
    const term = query.trim().toLocaleLowerCase();
    return apps.filter(app => !unavailable(app, system, network) &&
      (group === 'all' || app.group === group) && (!desktopOnly || app.type === 'desktop') &&
      [app.name, app.vendor, app.desc, ...app.tags].join(' ').toLocaleLowerCase().includes(term));
  }
  function configuration(ids, system, network) {
    const chosen = [...new Set(ids)].map(id => {
      const app = apps.find(item => item.id === id);
      if (!app || unavailable(app, system, network)) throw new Error('清单含不可用的工具：' + id);
      return app;
    });
    if (!chosen.length) throw new Error('请先选择工具');
    return ['# 我的 AI 工具配置单', '', `目标系统：${systems[system]}`, `网络环境：${networks[network]}`, '',
      ...chosen.flatMap(app => [`## ${app.name}`, `使用方式：${app.type === 'desktop' ? '桌面客户端' : '网页服务'}`, `官方入口：${app.url}`, `准备事项：${app.note}`, '']),
      '说明：此文件是选型清单，未执行安装。离线使用需另行准备模型、运行时和安装包。', '具体系统版本、芯片、地区、费用及许可要求以官网为准。', ''].join('\n');
  }

  // ── 在线安装脚本生成 ──────────────────────────────────────────────────────────
  // 根据已选应用 + 目标系统，生成可直接运行的 .ps1（Windows）或 .sh（macOS）脚本
  function generateScript(ids, system, network) {
    const chosen      = [...new Set(ids)].map(id => apps.find(a => a.id === id)).filter(Boolean);
    const desktopApps = chosen.filter(a => a.type === 'desktop');
    const webApps     = chosen.filter(a => a.type === 'web');
    const hasLocalLLM = desktopApps.some(a => installMeta[a.id]?.isLocalLLM);
    const date        = new Date().toLocaleDateString('zh-CN');
    if (system === 'windows') return _genPowerShell(desktopApps, webApps, hasLocalLLM, network, date);
    if (system === 'macos')   return _genBash(desktopApps, webApps, hasLocalLLM, network, date);
    throw new Error('未知系统：' + system);
  }

  function _genPowerShell(desktopApps, webApps, hasLocalLLM, network, date) {
    const wingetApps = desktopApps.filter(a => installMeta[a.id]?.winget);
    const directApps = desktopApps.filter(a => !installMeta[a.id]?.winget && !installMeta[a.id]?.webOnly);
    const appList    = [...desktopApps.map(a => a.name), ...webApps.map(a => a.name + '（网页）')].join('、');
    return `#Requires -Version 5.1
<#
.SYNOPSIS  AI 应用在线安装脚本（Windows）
.NOTES     生成日期：${date}  |  网络环境：${networks[network]}
           建议以管理员身份运行。安装过程需要联网。
           包含应用：${appList}
#>
[CmdletBinding()] param()
Set-StrictMode -Version Latest; $ErrorActionPreference = 'Continue'; $ProgressPreference = 'SilentlyContinue'
$Errs = [System.Collections.Generic.List[string]]::new()
function Write-Sep  { Write-Host ('─' * 50) -ForegroundColor DarkGray }
function Write-Step([string]$n,[string]$t) { Write-Host ''; Write-Host "  步骤 $n  $t" -ForegroundColor Cyan; Write-Sep }
function Write-Ok  ([string]$m) { Write-Host "  ✅  $m" -ForegroundColor Green }
function Write-Warn([string]$m) { Write-Host "  ⚠️   $m" -ForegroundColor Yellow }
function Write-Fail([string]$m) { Write-Host "  ❌  $m" -ForegroundColor Red; $script:Errs.Add($m) }

Write-Host ''; Write-Sep; Write-Host "  AI 应用在线安装向导  ·  ${date}" -ForegroundColor White
Write-Host "  ${networks[network]}" -ForegroundColor DarkGray; Write-Sep

# ── 步骤 1：硬件检查 ────────────────────────────────────────────────────────
Write-Step '1' '硬件检查'
$ramGB    = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
$diskFree = [math]::Round((Get-PSDrive ($env:SystemDrive.TrimEnd(':'))).Free / 1GB, 1)
$is64     = [Environment]::Is64BitOperatingSystem
try { Add-Type -TypeDefinition 'using System.Runtime.Intrinsics.X86; public static class _CpuChk{public static bool A=Avx2.IsSupported;}' -EA Stop; $avx2=[_CpuChk]::A } catch { $avx2=$true }
Write-Host "  内存 $ramGB GB  |  可用磁盘 $diskFree GB  |  64 位：$is64  |  AVX2：$avx2"
if (-not $is64)   { Write-Fail '需要 64 位 Windows'; exit 1 }
if ($ramGB -lt 4) { Write-Fail '内存不足 4 GB，无法运行本地 AI'; exit 1 }
if ($diskFree -lt 2) { Write-Warn "磁盘可用空间仅 $diskFree GB，安装可能失败" }
if (-not $avx2) { Write-Warn 'CPU 可能不支持 AVX2（2013 年前的处理器），本地 AI 运行可能失败' }
${hasLocalLLM ? `
$modelTier = if ($ramGB -ge 32)    { @{ label='高配（14B）'; file='Qwen2.5-14B-Instruct-Q4_K_M.gguf'; lmsId='lmstudio-community/Qwen2.5-14B-Instruct-GGUF/Qwen2.5-14B-Instruct-Q4_K_M.gguf'; size='9.0 GB' } }
             elseif ($ramGB -ge 16) { @{ label='标准（7B）';  file='Qwen2.5-7B-Instruct-Q4_K_M.gguf';  lmsId='lmstudio-community/Qwen2.5-7B-Instruct-GGUF/Qwen2.5-7B-Instruct-Q4_K_M.gguf';  size='4.7 GB' } }
             elseif ($ramGB -ge 8)  { @{ label='入门（1.5B）';file='Qwen2.5-1.5B-Instruct-Q4_K_M.gguf'; lmsId='lmstudio-community/Qwen2.5-1.5B-Instruct-GGUF/Qwen2.5-1.5B-Instruct-Q4_K_M.gguf'; size='1.1 GB' } }
             else                   { @{ label='轻量（0.5B）';file='Qwen2.5-0.5B-Instruct-Q4_K_M.gguf'; lmsId='lmstudio-community/Qwen2.5-0.5B-Instruct-GGUF/Qwen2.5-0.5B-Instruct-Q4_K_M.gguf'; size='0.4 GB' } }
Write-Ok "硬件检查通过  |  推荐模型：$($modelTier.label) ($($modelTier.size))"
` : "Write-Ok '硬件检查通过'"}

# ── 步骤 2：winget ──────────────────────────────────────────────────────────
Write-Step '2' '检查包管理器'
$useWinget = $null -ne (Get-Command winget -EA SilentlyContinue)
if ($useWinget) { Write-Ok 'winget 可用' } else { Write-Warn '未检测到 winget，将提示手动下载' }

# ── 步骤 3：安装应用 ────────────────────────────────────────────────────────
Write-Step '3' '安装桌面应用'
${wingetApps.length === 0 && directApps.length === 0 ? "Write-Host '  未选择桌面应用'" : ''}
${wingetApps.map(a => `if ($useWinget) {
  Write-Host '  正在安装 ${a.name}...' -NoNewline
  try { winget install '${installMeta[a.id].winget}' --silent --accept-package-agreements --accept-source-agreements 2>&1 | Out-Null; Write-Ok ' ${a.name} 完成' }
  catch { Write-Warn '${a.name} 安装失败；手动：${a.url}' }
} else { Write-Warn '${a.name}：请手动下载 ${a.url}' }`).join('\n')}
${directApps.map(a => `Write-Warn '${a.name} 暂无 winget 包；请手动下载：${a.url}'`).join('\n')}
${webApps.length ? `Write-Host ''; Write-Host '  ── 网页版（浏览器直接访问）──' -ForegroundColor DarkGray\n${webApps.map(a => `Write-Host '     ${a.name}：${a.url}' -ForegroundColor DarkGray`).join('\n')}` : ''}

${hasLocalLLM ? `# ── 步骤 4：下载模型 ────────────────────────────────────────────────────────
Write-Step '4' "下载 AI 模型（$($modelTier.label) · $($modelTier.size)）"
$lmsCli = "$env:LOCALAPPDATA\\Programs\\LM Studio\\lms.exe"
if (Test-Path $lmsCli) {
  Write-Host '  通过 LM Studio CLI 下载，时间取决于网速…'
  try { & $lmsCli get $modelTier.lmsId --yes; Write-Ok '模型下载完成' }
  catch { Write-Warn "自动下载失败；请在 LM Studio → 发现 中搜索并下载 $($modelTier.file)" }
} else {
  Write-Warn '未找到 lms.exe；请打开 LM Studio → 发现 → 搜索 Qwen2.5 → 下载推荐档位'
}` : `# ── 步骤 4：跳过 ───────────────────────────────────────────────────────────
Write-Step '4' '跳过模型下载（未选择本地模型工具）'`}

# ── 完成 ────────────────────────────────────────────────────────────────────
Write-Host ''; Write-Sep
if ($Errs.Count -eq 0) { Write-Host '  🎉 安装完成！' -ForegroundColor Green }
else { Write-Host '  完成，以下项目需手动处理：' -ForegroundColor Yellow; $Errs | ForEach-Object { Write-Host "   · $_" -ForegroundColor Yellow } }
Write-Sep; Write-Host ''
`;
  }

  function _genBash(desktopApps, webApps, hasLocalLLM, network, date) {
    const brewApps   = desktopApps.filter(a => installMeta[a.id]?.brew);
    const directApps = desktopApps.filter(a => !installMeta[a.id]?.brew && !installMeta[a.id]?.webOnly);
    const appList    = [...desktopApps.map(a => a.name), ...webApps.map(a => a.name + '（网页）')].join('、');
    return `#!/usr/bin/env bash
# AI 应用在线安装脚本（macOS）
# 生成日期：${date}  |  网络环境：${networks[network]}
# 包含应用：${appList}
# 用法：bash install-macos.sh
set -uo pipefail; ERRORS=()
sep()  { printf '\\n%s\\n' "$(printf '─%.0s' {1..50})"; }
step() { printf '\\n  步骤 %s  %s\\n' "$1" "$2"; sep; }
ok()   { printf '  ✅  %s\\n' "$1"; }
warn() { printf '  ⚠️   %s\\n' "$1"; }
fail() { printf '  ❌  %s\\n' "$1"; ERRORS+=("$1"); }

sep; printf '  AI 应用在线安装向导\\n  ${date}  |  ${networks[network]}\\n'; sep

# ── 步骤 1：系统检查 ─────────────────────────────────────────────────────────
step 1 '系统检查'
ARCH=$(uname -m)
RAM_GB=$(( $(sysctl -n hw.memsize 2>/dev/null || echo 0) / 1073741824 ))
DISK_FREE_GB=$(df -g / 2>/dev/null | awk 'NR==2{print $4}' || echo 0)
MACOS_MAJOR=$(sw_vers -productVersion 2>/dev/null | cut -d. -f1 || echo 0)
printf '  架构：%s  |  内存：%s GB  |  可用磁盘：%s GB\\n' "$ARCH" "$RAM_GB" "$DISK_FREE_GB"
[ "$MACOS_MAJOR" -lt 12 ] 2>/dev/null && fail "需要 macOS 12+（当前版本过低）" && exit 1
[ "$RAM_GB" -lt 4 ] && fail '内存不足 4 GB' && exit 1
[ "$DISK_FREE_GB" -lt 2 ] && warn "磁盘可用空间仅 \${DISK_FREE_GB} GB"
[ "$ARCH" = 'x86_64' ] && ! sysctl -n machdep.cpu.features 2>/dev/null | grep -qi 'AVX2' && warn 'CPU 可能不支持 AVX2，本地 AI 运行可能失败'
${hasLocalLLM ? `if   [ "$RAM_GB" -ge 32 ]; then ML='高配（14B）'  MF='Qwen2.5-14B-Instruct-Q4_K_M.gguf' MS='9.0 GB' MI='lmstudio-community/Qwen2.5-14B-Instruct-GGUF/Qwen2.5-14B-Instruct-Q4_K_M.gguf'
elif [ "$RAM_GB" -ge 16 ]; then ML='标准（7B）'   MF='Qwen2.5-7B-Instruct-Q4_K_M.gguf'  MS='4.7 GB' MI='lmstudio-community/Qwen2.5-7B-Instruct-GGUF/Qwen2.5-7B-Instruct-Q4_K_M.gguf'
elif [ "$RAM_GB" -ge 8  ]; then ML='入门（1.5B）' MF='Qwen2.5-1.5B-Instruct-Q4_K_M.gguf' MS='1.1 GB' MI='lmstudio-community/Qwen2.5-1.5B-Instruct-GGUF/Qwen2.5-1.5B-Instruct-Q4_K_M.gguf'
else                            ML='轻量（0.5B）' MF='Qwen2.5-0.5B-Instruct-Q4_K_M.gguf' MS='0.4 GB' MI='lmstudio-community/Qwen2.5-0.5B-Instruct-GGUF/Qwen2.5-0.5B-Instruct-Q4_K_M.gguf'; fi
ok "系统检查通过  |  推荐模型：\${ML}（\${MS}）"` : "ok '系统检查通过'"}

# ── 步骤 2：Homebrew ─────────────────────────────────────────────────────────
step 2 '检查 Homebrew'
command -v brew &>/dev/null && { ok 'Homebrew 已安装'; USE_BREW=1; } || { warn '未检测到 Homebrew，将提示手动下载'; USE_BREW=0; }

# ── 步骤 3：安装应用 ─────────────────────────────────────────────────────────
step 3 '安装桌面应用'
${brewApps.map(a => `if [ "$USE_BREW" = '1' ]; then
  printf '  正在安装 ${a.name}...'
  brew install --cask ${installMeta[a.id].brew} --quiet 2>/dev/null && ok ' ${a.name} 完成' || warn '${a.name} 失败；手动：${a.url}'
else warn '${a.name}：请手动下载 ${a.url}'; fi`).join('\n')}
${directApps.map(a => `warn '${a.name} 暂无 Homebrew Cask；请手动下载：${a.url}'`).join('\n')}
${webApps.length ? `printf '\\n  ── 网页版（浏览器直接访问）──\\n'\n${webApps.map(a => `printf '     ${a.name}：${a.url}\\n'`).join('\n')}` : ''}

${hasLocalLLM ? `# ── 步骤 4：下载模型 ─────────────────────────────────────────────────────
step 4 "下载 AI 模型（\${ML} · \${MS}）"
LMS_CLI=""; [ -f "$HOME/.cache/lm-studio/bin/lms" ] && LMS_CLI="$HOME/.cache/lm-studio/bin/lms"
[ -z "$LMS_CLI" ] && [ -f "/Applications/LM Studio.app/Contents/MacOS/lms" ] && LMS_CLI="/Applications/LM Studio.app/Contents/MacOS/lms"
if [ -n "$LMS_CLI" ]; then
  printf '  通过 LM Studio CLI 下载，时间取决于网速…\\n'
  "$LMS_CLI" get "$MI" --yes && ok '模型下载完成' || warn "自动下载失败；请在 LM Studio → 发现 中手动搜索 \${MF}"
else warn '未找到 lms CLI；请打开 LM Studio → 发现 → 搜索 Qwen2.5 → 下载推荐档位'; fi` : `# ── 步骤 4：跳过 ─────────────────────────────────────────────────────────────
step 4 '跳过模型下载（未选择本地模型工具）'`}

# ── 完成 ──────────────────────────────────────────────────────────────────────
sep
if [ \${#ERRORS[@]} -eq 0 ]; then printf '  🎉 安装完成！\\n'
else printf '  完成，以下项目需手动处理：\\n'; for e in "\${ERRORS[@]}"; do printf '   · %s\\n' "$e"; done; fi
sep; printf '\\n'
`;
  }

  return { apps, scenes, networks, systems, groups, installMeta, modelTiers, unavailable, recommend, filterApps, configuration, generateScript };
})();
if (typeof module !== 'undefined') module.exports = AIStation;

