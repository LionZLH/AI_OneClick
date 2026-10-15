/* AI Station 界面。无框架：状态 → 模板字符串 → 事件委托。 */
(function () {
  'use strict';

  // ---------------------------------------------------------------- 基础

  const root = document.getElementById('app');
  const TOKEN = (function () {
    const url = new URL(location.href);
    let t = url.searchParams.get('t');
    try {
      if (t) { sessionStorage.setItem('aistation-token', t); history.replaceState(null, '', location.pathname); }
      else t = sessionStorage.getItem('aistation-token');
    } catch (e) { /* 存储不可用时只用 URL 中的令牌 */ }
    return t || '';
  })();

  const ui = {
    view: 'boot',
    quiz: { step: 0, needs: [], network: null, priority: null },
    rec: null,
    recSel: new Set(),
    lib: { q: '', cat: 'all', region: 'all', sel: new Set(), setup: false },
    detail: null,
    menu: null,
    lastKey: ''
  };
  let S = null;            // 服务端状态
  let catalog = null;
  let apps = [];
  let byId = {};
  let needById = {};

  async function api(path, body) {
    const opt = { headers: { 'X-AIStation-Token': TOKEN } };
    if (body !== undefined) {
      opt.method = 'POST';
      opt.headers['Content-Type'] = 'application/json';
      opt.body = JSON.stringify(body);
    }
    const res = await fetch('/api/' + path, opt);
    const data = await res.json().catch(() => ({}));
    if (!res.ok) throw new Error(data.error || ('请求失败（' + res.status + '）'));
    if (data && data.ok === false && data.error) throw new Error(data.error);
    return data;
  }

  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }

  function toast(msg, kind) {
    const box = document.getElementById('toasts');
    const el = document.createElement('div');
    el.className = 'toast' + (kind === 'error' ? ' error' : '');
    el.innerHTML = icon(kind === 'error' ? 'alert' : 'check') + '<span>' + esc(msg) + '</span>';
    box.appendChild(el);
    setTimeout(() => el.remove(), kind === 'error' ? 6000 : 3200);
  }

  function fail(e) { toast(e && e.message ? e.message : String(e), 'error'); }

  // ---------------------------------------------------------------- 图标

  const ICONS = {
    chat: '<path d="M7.9 20A9 9 0 1 0 4 16.1L2 22Z"/>',
    doc: '<path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"/><path d="M14 2v4a2 2 0 0 0 2 2h4"/><path d="M16 13H8"/><path d="M16 17H8"/><path d="M10 9H8"/>',
    search: '<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>',
    briefcase: '<path d="M16 20V4a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16"/><rect width="20" height="14" x="2" y="6" rx="2"/>',
    code: '<polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/>',
    image: '<rect width="18" height="18" x="3" y="3" rx="2"/><circle cx="9" cy="9" r="2"/><path d="m21 15-3.1-3.1a2 2 0 0 0-2.8 0L6 21"/>',
    mic: '<path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><path d="M12 19v3"/>',
    lock: '<rect width="18" height="11" x="3" y="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>',
    home: '<path d="m3 9 9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><path d="M9 22V12h6v10"/>',
    grid: '<rect width="7" height="7" x="3" y="3" rx="1.5"/><rect width="7" height="7" x="14" y="3" rx="1.5"/><rect width="7" height="7" x="14" y="14" rx="1.5"/><rect width="7" height="7" x="3" y="14" rx="1.5"/>',
    download: '<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><path d="m7 10 5 5 5-5"/><path d="M12 15V3"/>',
    box: '<path d="M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z"/><path d="m3.3 7 8.7 5 8.7-5"/><path d="M12 22V12"/>',
    sliders: '<path d="M21 4h-7M10 4H3M21 12h-9M8 12H3M21 20h-5M12 20H3M14 2v4M8 10v4M16 18v4"/>',
    check: '<path d="M20 6 9 17l-5-5"/>',
    x: '<path d="M18 6 6 18M6 6l12 12"/>',
    external: '<path d="M15 3h6v6"/><path d="M10 14 21 3"/><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/>',
    play: '<polygon points="6 3 20 12 6 21 6 3"/>',
    more: '<circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/><circle cx="5" cy="12" r="1"/>',
    refresh: '<path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8"/><path d="M21 3v5h-5"/><path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16"/><path d="M8 16H3v5"/>',
    globe: '<circle cx="12" cy="12" r="10"/><path d="M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20"/><path d="M2 12h20"/>',
    wifi: '<path d="M12 20h.01"/><path d="M2 8.8a15 15 0 0 1 20 0"/><path d="M5 12.9a10 10 0 0 1 14 0"/><path d="M8.5 16.4a5 5 0 0 1 7 0"/>',
    wifiOff: '<path d="M12 20h.01"/><path d="M8.5 16.4a5 5 0 0 1 7 0"/><path d="M5 12.9a10 10 0 0 1 5.2-2.7"/><path d="M19 12.9a10 10 0 0 0-2-1.5"/><path d="M2 8.8a15 15 0 0 1 4.2-2.6"/><path d="M22 8.8a15 15 0 0 0-11.3-3.8"/><path d="m2 2 20 20"/>',
    zap: '<path d="M4 14a1 1 0 0 1-.8-1.6l9.9-10.2a.5.5 0 0 1 .9.5l-1.9 6A1 1 0 0 0 13 10h7a1 1 0 0 1 .8 1.6l-9.9 10.2a.5.5 0 0 1-.9-.5l1.9-6A1 1 0 0 0 11 14z"/>',
    layers: '<path d="m12.8 2.2a2 2 0 0 0-1.6 0L2.6 6.1a1 1 0 0 0 0 1.8l8.6 3.9a2 2 0 0 0 1.6 0l8.6-3.9a1 1 0 0 0 0-1.8Z"/><path d="m22 17.6-9.2 4.2a2 2 0 0 1-1.6 0L2 17.6"/><path d="m22 12.6-9.2 4.2a2 2 0 0 1-1.6 0L2 12.6"/>',
    shield: '<path d="M20 13c0 5-3.5 7.5-7.7 9a1 1 0 0 1-.6 0C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.2-2.7a1.2 1.2 0 0 1 1.6 0C14.5 3.8 17 5 19 5a1 1 0 0 1 1 1z"/>',
    sparkles: '<path d="M9.9 15.5A2 2 0 0 0 8.5 14l-6.1-1.6a.5.5 0 0 1 0-1L8.5 10A2 2 0 0 0 9.9 8.5l1.6-6.1a.5.5 0 0 1 1 0L14 8.5A2 2 0 0 0 15.5 10l6.1 1.6a.5.5 0 0 1 0 1L15.5 14a2 2 0 0 0-1.4 1.4l-1.6 6.1a.5.5 0 0 1-1 0z"/><path d="M20 3v4M22 5h-4"/>',
    left: '<path d="m12 19-7-7 7-7"/><path d="M19 12H5"/>',
    right: '<path d="M5 12h14"/><path d="m12 5 7 7-7 7"/>',
    trash: '<path d="M3 6h18"/><path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6"/><path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2"/>',
    up: '<circle cx="12" cy="12" r="10"/><path d="m16 12-4-4-4 4"/><path d="M12 16V8"/>',
    folder: '<path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.7-.9L9.6 3.9A2 2 0 0 0 7.9 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"/>',
    alert: '<path d="m21.7 18-8-14a2 2 0 0 0-3.5 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.7-3"/><path d="M12 9v4"/><path d="M12 17h.01"/>',
    info: '<circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/>',
    cpu: '<rect width="16" height="16" x="4" y="4" rx="2"/><rect width="6" height="6" x="9" y="9" rx="1"/><path d="M15 2v2M15 20v2M2 15h2M2 9h2M20 15h2M20 9h2M9 2v2M9 20v2"/>',
    monitor: '<rect width="20" height="14" x="2" y="3" rx="2"/><path d="M8 21h8M12 17v4"/>',
    memory: '<path d="M6 19v-3M10 19v-3M14 19v-3M18 19v-3"/><rect width="20" height="10" x="2" y="6" rx="2"/><path d="M6 10h.01M10 10h.01M14 10h.01M18 10h.01"/>',
    disk: '<path d="M22 12H2"/><path d="M5.5 5.1 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.5-6.9A2 2 0 0 0 16.8 4H7.2a2 2 0 0 0-1.7 1.1z"/><path d="M6 16h.01M10 16h.01"/>',
    windows: '<rect width="8" height="8" x="3" y="3" rx="1"/><rect width="8" height="8" x="13" y="3" rx="1"/><rect width="8" height="8" x="3" y="13" rx="1"/><rect width="8" height="8" x="13" y="13" rx="1"/>',
    loader: '<path d="M21 12a9 9 0 1 1-6.2-8.6"/>'
  };

  function icon(name, cls) {
    return '<svg class="i' + (cls ? ' ' + cls : '') + '" viewBox="0 0 24 24" aria-hidden="true">' + (ICONS[name] || '') + '</svg>';
  }

  const LOGO = '<svg class="logo" viewBox="0 0 64 64" aria-hidden="true"><defs><linearGradient id="lg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#6366f1"/><stop offset="1" stop-color="#7c3aed"/></linearGradient></defs><rect x="2" y="2" width="60" height="60" rx="15" fill="url(#lg)"/><path d="M29 16l4.4 13.6L47 34l-13.6 4.4L29 52l-4.4-13.6L11 34l13.6-4.4z" fill="#fff"/><path d="M46 11l1.9 5.1L53 18l-5.1 1.9L46 25l-1.9-5.1L39 18l5.1-1.9z" fill="#fff" opacity=".9"/></svg>';

  // ---------------------------------------------------------------- 数据辅助

  function setState(state) {
    S = state;
    if (!catalog) {
      catalog = state.catalog;
      apps = catalog.apps;
      byId = Object.fromEntries(apps.map((a) => [a.id, a]));
      needById = Object.fromEntries(catalog.needs.map((n) => [n.id, n]));
    }
  }

  const inst = (id) => !!(S.installed[id] && S.installed[id].installed);
  const webAdded = (id) => (S.profile.webShortcuts || []).includes(id);
  const owned = (id) => (byId[id] && byId[id].kind === 'web' ? webAdded(id) : inst(id));
  const activeJobs = () => S.jobs.filter((j) => j.status === 'queued' || j.status === 'running');
  const jobFor = (appId, action) => S.jobs.slice().reverse().find((j) => j.appId === appId && (!action || j.action === action) && (j.status === 'queued' || j.status === 'running'));
  const hw = () => S.hardware || null;

  function avatar(app, size) {
    const letter = app.letter || app.name[0];
    const cls = 'avatar' + (size ? ' ' + size : '') + (letter.length > 1 ? ' long' : '');
    const info = S.installed[app.id];
    if (info && info.installed && info.exe) {
      return '<span class="' + cls + ' img" data-letter="' + esc(letter) + '" style="--c:' + esc(app.color) + '">' +
        '<img alt="" src="/api/icon?id=' + encodeURIComponent(app.id) + '&t=' + encodeURIComponent(TOKEN) + '" ' +
        'onerror="var p=this.parentNode;p.classList.remove(\'img\');p.textContent=p.dataset.letter"></span>';
    }
    return '<span class="' + cls + '" style="--c:' + esc(app.color) + '">' + esc(letter) + '</span>';
  }

  function regionTag(app) {
    return '<span class="tag ' + app.region + '">' + esc(catalog.regions[app.region].label) + '</span>';
  }

  function kindTag(app) {
    return app.kind === 'web' ? '<span class="tag">网页版</span>' : '';
  }

  function fmtSize(bytes) {
    if (!bytes) return '';
    const gb = bytes / 1073741824;
    return gb >= 1 ? gb.toFixed(1) + ' GB' : Math.round(bytes / 1048576) + ' MB';
  }

  function shortCpu(name) {
    if (!name) return '';
    return name.replace(/\((R|TM)\)/gi, '').replace(/ CPU/i, '').replace(/\s+@.*$/, '').replace(/\s+\d+-Core Processor/i, '').replace(/\s+/g, ' ').trim();
  }

  function mainGpu(h) {
    const gpus = (h && h.gpus) || [];
    const d = gpus.find((g) => Recommend.isDiscreteGpu(g.name));
    return d || gpus[0] || null;
  }

  function hwSummary(h) {
    if (!h) return '正在检测硬件…';
    const parts = [];
    if (h.ramGB) parts.push(Math.round(h.ramGB) + 'GB 内存');
    const g = mainGpu(h);
    if (g && Recommend.isDiscreteGpu(g.name)) parts.push(g.name.replace(/NVIDIA |GeForce |AMD |\(R\)|\(TM\)/g, '').trim());
    return parts.join(' · ') || '硬件信息不可用';
  }

  function installLabel(app) {
    const s = app.install.source;
    if (s === 'winget') return 'winget · ' + app.install.id;
    if (s === 'msstore') return 'Microsoft Store · ' + app.install.id;
    if (s === 'web') return '网页版，无需安装';
    return '需从官网下载安装';
  }

  // ---------------------------------------------------------------- 动作

  async function refreshState() {
    setState(await api('state'));
  }

  async function startJobs(jobs, msg) {
    if (!jobs.length) return;
    try {
      const r = await api('jobs', { jobs });
      S.jobs = r.jobs;
      S.installed = r.installed;
      if (msg) toast(msg);
      render();
      schedulePoll(400);
    } catch (e) { fail(e); }
  }

  function install(ids) {
    const desktop = ids.filter((id) => byId[id] && byId[id].kind !== 'web' && !inst(id));
    const web = ids.filter((id) => byId[id] && byId[id].kind === 'web' && !webAdded(id));
    const tasks = [];
    web.forEach((id) => tasks.push(api('web-shortcut', { id, add: true }).then((p) => { S.profile = p; })));
    return Promise.all(tasks).then(() => {
      if (desktop.length) {
        const first = byId[desktop[0]].name;
        return startJobs(desktop.map((id) => ({ action: 'install', appId: id })),
          desktop.length > 1 ? '已开始安装 ' + first + ' 等 ' + desktop.length + ' 个软件' : '已开始安装 ' + first);
      }
      if (web.length) toast('已添加到「我的 AI」');
      render();
    }).catch(fail);
  }

  async function launch(id) {
    try {
      await api('launch', { id });
      toast('正在打开 ' + byId[id].name);
    } catch (e) { fail(e); }
  }

  function go(view) {
    ui.view = view;
    ui.detail = null;
    ui.menu = null;
    render(true);
  }

  async function finishOnboarding(extra) {
    try {
      S.profile = await api('profile', Object.assign({ onboarded: true }, extra || {}));
    } catch (e) { fail(e); }
  }

  // ---------------------------------------------------------------- 首次配置

  function viewWelcome() {
    const h = hw();
    let tags;
    if (!h) tags = '<span class="tag">' + icon('loader', 'spin') + '正在检测这台电脑…</span>';
    else {
      tags = '';
      if (h.os && h.os.name) tags += '<span class="tag">' + icon('windows') + esc(h.os.name) + '</span>';
      if (h.cpu) tags += '<span class="tag">' + icon('cpu') + esc(shortCpu(h.cpu)) + '</span>';
      if (h.ramGB) tags += '<span class="tag">' + icon('memory') + Math.round(h.ramGB) + 'GB 内存</span>';
      const g = mainGpu(h);
      if (g) tags += '<span class="tag">' + icon('monitor') + esc(g.name) + '</span>';
    }
    return `
      <div class="onboard">
        <div class="topbar"><div class="brand">${LOGO}<span>AI Station</span></div>
          <button class="btn ghost skip" data-act="skip-onboard">跳过，直接进入</button></div>
        <div class="ob-wrap welcome">
          <div>${LOGO.replace('class="logo"', 'class="logo-xl"')}</div>
          <h1>把合适的 AI 装进你的电脑</h1>
          <p class="sub">根据你的用途、网络和电脑配置，推荐合适的 AI 软件并一键装好。装好后在这里统一启动和管理。</p>
          <div class="hwline">${tags}</div>
          <div class="choices">
            <button class="choice primary" data-act="start-quiz">
              <div class="ic">${icon('sparkles')}</div>
              <h3>帮我推荐</h3>
              <p>回答 3 个简单问题，约 1 分钟，得到为你定制的 AI 软件组合。</p>
              <div class="go">开始 ${icon('right')}</div>
            </button>
            <button class="choice" data-act="self-pick">
              <div class="ic">${icon('grid')}</div>
              <h3>我自己挑</h3>
              <p>浏览全部 ${apps.length} 款精选 AI 软件，勾选后一键安装。</p>
              <div class="go">进入软件库 ${icon('right')}</div>
            </button>
          </div>
          <p class="fine">软件通过 Windows 官方 winget 软件源或 Microsoft Store 安装，安装包来自各软件的官方下载地址。</p>
        </div>
      </div>`;
  }

  const NETWORK_OPTS = [
    { id: 'cn', icon: 'wifi', title: '国内网络', desc: '能正常使用国内网站和服务，无法访问 ChatGPT 等海外服务' },
    { id: 'global', icon: 'globe', title: '可以访问海外服务', desc: '能稳定打开 ChatGPT、Claude、Google 等海外服务' },
    { id: 'offline', icon: 'wifiOff', title: '完全离线', desc: '内网、涉密或不联网的电脑，只使用本地运行的 AI' }
  ];
  const PRIORITY_OPTS = [
    { id: 'easy', icon: 'zap', title: '简单省心', desc: '少而精，尽量用一个软件覆盖多个用途' },
    { id: 'best', icon: 'layers', title: '能力全面', desc: '每个用途多推荐一个，方便对比，选最顺手的' },
    { id: 'private', icon: 'shield', title: '隐私优先', desc: '优先在本机运行的 AI，资料尽量不出电脑' }
  ];

  function stepsBar(step) {
    const names = ['用途', '网络', '偏好'];
    return '<div class="steps">' + names.map((n, i) =>
      (i ? '<span class="line"></span>' : '') +
      '<span class="dot ' + (i === step ? 'on' : i < step ? 'done' : '') + '"><i>' + (i < step ? '✓' : i + 1) + '</i>' + n + '</span>'
    ).join('') + '</div>';
  }

  function viewQuiz() {
    const q = ui.quiz;
    let body;
    if (q.step === 0) {
      body = `
        <div class="q">
          <h2>你主要想用 AI 做什么？</h2>
          <p class="hint">可以多选，我们会为每个用途挑选合适的软件。</p>
          <div class="opts">
            ${catalog.needs.map((n) => {
              const on = q.needs.includes(n.id);
              return `<button class="opt ${on ? 'on' : ''}" data-act="quiz-need" data-id="${n.id}" aria-pressed="${on}">
                <span class="check ${on ? 'on' : ''}">${icon('check')}</span>
                <div class="ic">${icon(n.icon)}</div><b>${esc(n.label)}</b><span>${esc(n.desc)}</span></button>`;
            }).join('')}
          </div>
          <div class="qnav">
            <button class="btn ghost" data-act="quiz-back">${icon('left')}返回</button>
            <span class="grow"></span>
            <span class="muted">${q.needs.length ? '已选 ' + q.needs.length + ' 项' : '至少选择 1 项'}</span>
            <button class="btn primary lg" data-act="quiz-next" ${q.needs.length ? '' : 'disabled'}>下一步 ${icon('right')}</button>
          </div>
        </div>`;
    } else if (q.step === 1) {
      const guess = Recommend.guessNetwork(S.network);
      body = `
        <div class="q">
          <h2>这台电脑的网络环境是？</h2>
          <p class="hint">${S.network ? '已根据实际连通情况预先选好，如不符合可以修改。' : '正在检测网络连通情况…'}</p>
          <div class="opts wide">
            ${NETWORK_OPTS.map((o) => {
              const on = q.network === o.id;
              return `<button class="opt ${on ? 'on' : ''}" data-act="quiz-network" data-id="${o.id}" aria-pressed="${on}">
                ${guess === o.id ? '<span class="tag ok detected">检测结果</span>' : ''}
                <div class="ic">${icon(o.icon)}</div><b>${o.title}</b><span>${o.desc}</span></button>`;
            }).join('')}
          </div>
          <div class="qnav">
            <button class="btn ghost" data-act="quiz-back">${icon('left')}上一步</button>
            <span class="grow"></span>
            <button class="btn primary lg" data-act="quiz-next" ${q.network ? '' : 'disabled'}>下一步 ${icon('right')}</button>
          </div>
        </div>`;
    } else {
      body = `
        <div class="q">
          <h2>你更看重什么？</h2>
          <p class="hint">决定推荐的数量和方向。</p>
          <div class="opts wide">
            ${PRIORITY_OPTS.map((o) => {
              const on = q.priority === o.id;
              return `<button class="opt ${on ? 'on' : ''}" data-act="quiz-priority" data-id="${o.id}" aria-pressed="${on}">
                <div class="ic">${icon(o.icon)}</div><b>${o.title}</b><span>${o.desc}</span></button>`;
            }).join('')}
          </div>
          <div class="qnav">
            <button class="btn ghost" data-act="quiz-back">${icon('left')}上一步</button>
            <span class="grow"></span>
            <button class="btn primary lg" data-act="quiz-next" ${q.priority ? '' : 'disabled'}>查看推荐 ${icon('right')}</button>
          </div>
        </div>`;
    }
    return `
      <div class="onboard">
        <div class="topbar"><div class="brand">${LOGO}<span>AI Station</span></div>
          <button class="btn ghost skip" data-act="skip-onboard">跳过，直接进入</button></div>
        <div class="ob-wrap">${stepsBar(q.step)}${body}</div>
      </div>`;
  }

  function computeRec() {
    const q = ui.quiz;
    const installedMap = {};
    apps.forEach((a) => { if (owned(a.id)) installedMap[a.id] = true; });
    ui.rec = Recommend.recommend(catalog, { needs: q.needs, network: q.network, priority: q.priority }, hw(), installedMap);
    ui.recSel = new Set(ui.rec.picks.filter((p) => !p.installed).map((p) => p.id));
  }

  function recCard(item, big) {
    const app = byId[item.id];
    const on = ui.recSel.has(item.id);
    const notes = (app.notes || []).map((n) => '<div>· ' + esc(n) + '</div>').join('') +
      (item.warnings || []).map((w) => '<div class="warn">· ' + esc(w) + '</div>').join('');
    if (!big) {
      return `<button class="alt ${on ? 'on' : ''}" data-act="rec-toggle" data-id="${app.id}" aria-pressed="${on}" ${item.installed ? 'disabled' : ''}>
        ${avatar(app, 'sm')}<div class="grow"><div class="n">${esc(app.name)}</div><div class="s">${esc(app.summary)}</div></div>
        ${item.installed ? '<span class="tag ok">已有</span>' : '<span class="check ' + (on ? 'on' : '') + '">' + icon('check') + '</span>'}</button>`;
    }
    return `
      <div class="rec ${on ? 'on' : ''}" data-act="rec-toggle" data-id="${app.id}" role="checkbox" aria-checked="${on}" tabindex="0">
        ${avatar(app, 'lg')}
        <div>
          <div class="name">${esc(app.name)} <span class="muted" style="font-weight:400;font-size:13px">${esc(app.vendor)}</span>
            ${regionTag(app)} ${kindTag(app)} ${item.installed ? '<span class="tag ok">已安装</span>' : ''}</div>
          <div class="summary">${esc(app.summary)}</div>
          <div class="why">${item.reasons.map((r) => `<div><span class="tag">${esc(needById[r.need].label)}</span><span>${esc(r.text)}</span></div>`).join('')}</div>
          ${notes ? '<div class="notes">' + notes + '</div>' : ''}
        </div>
        ${item.installed ? '' : '<span class="check ' + (on ? 'on' : '') + '">' + icon('check') + '</span>'}
      </div>`;
  }

  function viewResult() {
    const r = ui.rec;
    const a = r.answers;
    const netLabel = NETWORK_OPTS.find((o) => o.id === a.network).title;
    const priLabel = PRIORITY_OPTS.find((o) => o.id === a.priority).title;
    const basis = [a.needs.map((n) => needById[n].label).join('、'), netLabel, priLabel];
    if (hw() && hw().ramGB) basis.push(Math.round(hw().ramGB) + 'GB 内存');
    const sel = [...ui.recSel];
    const nDesktop = sel.filter((id) => byId[id].kind !== 'web').length;
    const nWeb = sel.length - nDesktop;
    let summary = '没有选择软件';
    if (sel.length) {
      const parts = [];
      if (nDesktop) parts.push(nDesktop + ' 款将自动安装');
      if (nWeb) parts.push(nWeb + ' 款网页版将添加到「我的 AI」');
      summary = '已选 ' + sel.length + ' 款：' + parts.join('，');
    }
    const model = r.model;
    return `
      <div class="onboard">
        <div class="topbar"><div class="brand">${LOGO}<span>AI Station</span></div></div>
        <div class="ob-wrap">
          <div class="rec-head">
            <div style="flex:1">
              <h2>${r.picks.length ? '为你推荐 ' + r.picks.length + ' 款 AI 软件' : '暂时没有合适的推荐'}</h2>
              <p>根据：${esc(basis.join(' · '))}</p>
            </div>
            <button class="btn ghost" data-act="quiz-restart">${icon('refresh')}重新回答</button>
          </div>
          <div class="recs">${r.picks.map((p) => recCard(p, true)).join('')}</div>
          ${r.uncovered.length ? '<div class="note-box">' + r.uncovered.map((u) => '<div>' + icon('info') + ' <b>' + esc(needById[u.need].label) + '</b>：' + esc(u.reason) + '</div>').join('') + '</div>' : ''}
          ${model ? `
            <h2 class="section">${icon('box')}本地模型建议</h2>
            <div class="panel"><div class="model-hero">
              <div><div class="muted">推荐规格</div><div class="big">${esc(model.label)}</div></div>
              <div style="flex:1;min-width:240px"><div>${esc(model.reason)}。</div>
              <div class="muted">约 ${model.sizeGB}GB，装好软件后可以在「本地模型」页一键下载。</div>
              ${model.warnings.map((w) => '<div style="color:var(--warn)">' + esc(w) + '</div>').join('')}</div>
            </div></div>` : ''}
          ${r.alternates.length ? `
            <h2 class="section">其他选择 <span class="muted">按需勾选</span></h2>
            <div class="alt-list">${r.alternates.map((p) => recCard(p, false)).join('')}</div>` : ''}
        </div>
        <div class="ob-bar"><div class="in">
          <span class="grow">${esc(summary)}</span>
          <button class="btn ghost" data-act="rec-browse">浏览全部软件</button>
          <button class="btn primary lg" data-act="rec-confirm">${sel.length ? '安装并完成配置' : '完成'}</button>
        </div></div>
      </div>`;
  }

  // ---------------------------------------------------------------- 主界面

  const NAV = [
    { id: 'home', icon: 'home', label: '我的 AI' },
    { id: 'library', icon: 'grid', label: '软件库' },
    { id: 'tasks', icon: 'download', label: '下载与更新' },
    { id: 'models', icon: 'box', label: '本地模型' },
    { id: 'settings', icon: 'sliders', label: '设置' }
  ];

  function shell(content) {
    const active = activeJobs().length;
    return `
      <div class="shell">
        <aside class="sidebar">
          <div class="brand">${LOGO}<span>AI Station</span></div>
          <nav class="nav">
            ${NAV.map((n) => `<button class="${ui.view === n.id ? 'active' : ''}" data-act="nav" data-id="${n.id}" title="${n.label}">
              ${icon(n.icon)}<span class="lbl">${n.label}</span>${n.id === 'tasks' && active ? '<span class="count">' + active + '</span>' : ''}</button>`).join('')}
          </nav>
          <div class="foot"><div class="hw">${esc(hwSummary(hw()))}</div>v${esc(S.version)}</div>
        </aside>
        <main class="main"><div class="content"><div class="page">${content}</div></div></main>
      </div>`;
  }

  function greeting() {
    const h = new Date().getHours();
    if (h < 6) return '夜深了';
    if (h < 11) return '早上好';
    if (h < 13) return '中午好';
    if (h < 18) return '下午好';
    return '晚上好';
  }

  function wingetBanner() {
    if (!S.winget.checked || S.winget.available) return '';
    return `<div class="banner warn">${icon('alert')}<span class="grow">这台电脑没有可用的 winget，无法自动安装。请先从 Microsoft Store 安装「应用安装程序」，或在软件详情中前往官网下载。</span>
      <button class="btn sm" data-act="open-winget-store">获取应用安装程序</button></div>`;
  }

  function jobsBanner() {
    const act = activeJobs();
    if (!act.length) return '';
    const first = act[0];
    const name = byId[first.appId] ? byId[first.appId].name : (first.arg || '');
    return `<div class="banner">${icon('loader', 'spin')}<span class="grow">${esc(actionName(first.action))} ${esc(name)}${act.length > 1 ? ' 等 ' + act.length + ' 个任务' : ''}：${esc(first.message)}</span>
      <button class="btn sm" data-act="nav" data-id="tasks">查看进度</button></div>`;
  }

  function viewHome() {
    const launches = S.profile.launches || {};
    const mine = apps.filter((a) => owned(a.id))
      .sort((a, b) => (launches[b.id] || 0) - (launches[a.id] || 0) || apps.indexOf(a) - apps.indexOf(b));
    const pending = (S.profile.recommendation || []).filter((id) => byId[id] && !owned(id) && !jobFor(id));
    const firstScan = !S.scannedAt;
    let body;
    if (mine.length) {
      body = `<div class="tiles">
        ${mine.map((a) => `
          <div class="tile" data-act="launch" data-id="${a.id}" role="button" tabindex="0" title="启动 ${esc(a.name)}">
            ${avatar(a, 'lg')}
            <div><div class="name">${esc(a.name)}</div><div class="sub">${a.kind === 'web' ? '网页版' : esc(a.vendor)}</div></div>
            <span class="launch tag ok">${icon(a.kind === 'web' ? 'external' : 'play')}${a.kind === 'web' ? '打开' : '启动'}</span>
            <button class="btn ghost icon sm more" data-act="tile-menu" data-id="${a.id}" title="更多">${icon('more')}</button>
          </div>`).join('')}
        <button class="tile add" data-act="nav" data-id="library">${icon('grid')}<span>添加软件</span></button>
      </div>`;
    } else if (firstScan) {
      body = `<div class="empty"><div class="art">${icon('loader', 'spin')}</div><h3>正在识别已安装的 AI 软件…</h3><p>稍等几秒。</p></div>`;
    } else {
      body = `<div class="empty"><div class="art">${icon('sparkles')}</div><h3>还没有 AI 软件</h3>
        <p>回答 3 个问题获取推荐，或者直接去软件库挑选。装好的软件会出现在这里，点击即可启动。</p>
        <div class="row"><button class="btn primary" data-act="start-quiz">帮我推荐</button><button class="btn" data-act="nav" data-id="library">浏览软件库</button></div></div>`;
    }
    return shell(`
      <div class="page-head"><div><h1 class="hello">${greeting()}</h1>
        <p>${mine.length ? '你的 ' + mine.length + ' 个 AI 工具都在这里，点击即可启动。' : '在这里统一启动和管理你的 AI 软件。'}</p></div>
        <div class="actions"><button class="btn" data-act="refresh">${icon('refresh', S.scanning ? 'spin' : '')}刷新</button>
        <button class="btn primary" data-act="nav" data-id="library">${icon('grid')}添加软件</button></div></div>
      ${jobsBanner()}${wingetBanner()}
      ${body}
      ${pending.length ? `<h2 class="section">${icon('sparkles')}为你推荐，还没装 <span class="muted">来自问卷结果</span></h2>
        <div class="chips">${pending.map((id) => `<button class="chip" data-act="install-one" data-id="${id}">${avatar(byId[id], 'sm')}${esc(byId[id].name)}<span class="muted">${byId[id].kind === 'web' ? '添加' : '安装'}</span></button>`).join('')}</div>` : ''}
    `);
  }

  function libraryFiltered() {
    const q = ui.lib.q.trim().toLowerCase();
    return apps.filter((a) => {
      if (ui.lib.region !== 'all' && a.region !== ui.lib.region) return false;
      if (ui.lib.cat !== 'all' && !((a.scores[ui.lib.cat] || 0) >= 2)) return false;
      if (q && !(a.name + a.vendor + a.summary + Object.values(a.why || {}).join('')).toLowerCase().includes(q)) return false;
      return true;
    });
  }

  function appAction(a, small) {
    const sz = small ? ' sm' : '';
    const job = jobFor(a.id);
    if (job) {
      const pct = job.status === 'running' && job.progress != null ? ' ' + Math.round(job.progress) + '%' : '';
      return `<span class="status-line">${icon('loader', 'spin')}${job.status === 'queued' ? '排队中' : esc(actionName(job.action)) + '中' + pct}</span>`;
    }
    if (a.kind === 'web') {
      return webAdded(a.id)
        ? `<button class="btn${sz}" data-act="launch" data-id="${a.id}">${icon('external')}打开</button>`
        : `<button class="btn${sz}" data-act="install-one" data-id="${a.id}">添加</button>`;
    }
    if (inst(a.id)) return `<button class="btn${sz}" data-act="launch" data-id="${a.id}">${icon('play')}启动</button>`;
    if (a.install.source === 'manual') return `<button class="btn${sz}" data-act="open-home" data-id="${a.id}">${icon('external')}官网下载</button>`;
    return `<button class="btn primary${sz}" data-act="install-one" data-id="${a.id}">${icon('download')}安装</button>`;
  }

  function viewLibrary() {
    const list = libraryFiltered();
    const sel = [...ui.lib.sel].filter((id) => byId[id] && !owned(id));
    const regions = [['all', '全部'], ['cn', '国内可用'], ['global', '需海外网络'], ['local', '本地运行']];
    return shell(`
      <div class="page-head"><div><h1>软件库</h1><p>精选 ${apps.length} 款 AI 软件，可以单独安装，也可以勾选后一起安装。</p></div></div>
      ${ui.lib.setup ? '<div class="banner">' + icon('info') + '<span class="grow">勾选想要的软件，然后点击下方「安装选中」。网页版会添加到「我的 AI」，方便一键打开。</span></div>' : ''}
      ${wingetBanner()}
      <div class="toolbar">
        <label class="search">${icon('search')}<span class="sr">搜索</span>
          <input id="lib-search" type="search" placeholder="搜索软件名称或用途" value="${esc(ui.lib.q)}" autocomplete="off"></label>
        <div class="seg">${regions.map(([id, l]) => `<button class="${ui.lib.region === id ? 'on' : ''}" data-act="lib-region" data-id="${id}">${l}</button>`).join('')}</div>
      </div>
      <div class="cats">
        <button class="${ui.lib.cat === 'all' ? 'on' : ''}" data-act="lib-cat" data-id="all">全部</button>
        ${catalog.needs.map((n) => `<button class="${ui.lib.cat === n.id ? 'on' : ''}" data-act="lib-cat" data-id="${n.id}">${esc(n.label)}</button>`).join('')}
      </div>
      ${list.length ? `<div class="grid">${list.map((a) => {
        const own = owned(a.id);
        const on = ui.lib.sel.has(a.id) && !own;
        return `
        <div class="card ${on ? 'selected' : ''}" data-act="detail" data-id="${a.id}" tabindex="0">
          ${own || jobFor(a.id) ? '' : `<button class="check ${on ? 'on' : ''}" data-act="lib-toggle" data-id="${a.id}" title="选择" aria-pressed="${on}">${icon('check')}</button>`}
          <div class="top">${avatar(a)}<div><div class="title">${esc(a.name)}${own ? '<span class="tag ok">' + (a.kind === 'web' ? '已添加' : '已安装') + '</span>' : ''}</div><div class="vendor">${esc(a.vendor)}</div></div></div>
          <div class="desc">${esc(a.summary)}</div>
          <div class="bottom"><div class="tags">${regionTag(a)}${kindTag(a)}${a.advanced ? '<span class="tag">进阶</span>' : ''}</div>${appAction(a, true)}</div>
        </div>`;
      }).join('')}</div>` : `<div class="empty"><div class="art">${icon('search')}</div><h3>没有找到匹配的软件</h3><p>换个关键词或筛选条件试试。</p></div>`}
      ${sel.length ? `<div class="actionbar">
        <div class="stack">${sel.slice(0, 5).map((id) => avatar(byId[id], 'sm')).join('')}</div>
        <span class="grow">已选 ${sel.length} 款</span>
        <button class="btn ghost" data-act="lib-clear">清空</button>
        <button class="btn primary" data-act="lib-install">${icon('download')}安装选中的 ${sel.length} 款</button>
      </div>` : ''}
    `);
  }

  function actionName(action) {
    return { install: '安装', upgrade: '更新', uninstall: '卸载', 'pull-model': '下载模型', 'import-model': '导入模型' }[action] || action;
  }

  function jobTitle(j) {
    if (j.action === 'pull-model') return '下载模型 ' + (j.arg || '');
    if (j.action === 'import-model') return '导入模型 ' + (j.arg || '') + ' → ' + ({ lmstudio: 'LM Studio', ollama: 'Ollama', gpt4all: 'GPT4All' }[j.target] || j.target);
    return actionName(j.action) + ' ' + (byId[j.appId] ? byId[j.appId].name : j.appId);
  }

  function jobAvatar(j) {
    if (byId[j.appId]) return avatar(byId[j.appId]);
    return '<span class="avatar" style="--c:#5b5bd6">' + icon('box') + '</span>';
  }

  function viewTasks() {
    // 进行中 → 排队中（按加入顺序）→ 已结束（最近的在前）
    const rank = (j) => (j.status === 'running' ? 0 : j.status === 'queued' ? 1 : 2);
    const jobs = S.jobs.slice().sort((a, b) => rank(a) - rank(b) || (rank(a) < 2 ? a.id - b.id : b.id - a.id));
    const upgradable = apps.filter((a) => a.kind !== 'web' && inst(a.id) && (a.install.source === 'winget' || a.install.source === 'msstore'));
    const hasFinished = S.jobs.some((j) => j.status !== 'queued' && j.status !== 'running');
    return shell(`
      <div class="page-head"><div><h1>下载与更新</h1><p>安装、更新、卸载和模型下载都在这里排队执行，进度来自真实的安装程序输出。</p></div>
        <div class="actions">
          <button class="btn" data-act="upgrade-all" ${upgradable.length && S.winget.available ? '' : 'disabled'}>${icon('up')}检查全部更新</button>
          <button class="btn ghost" data-act="jobs-clear" ${hasFinished ? '' : 'disabled'}>清除已完成</button>
        </div></div>
      ${wingetBanner()}
      ${jobs.length ? `<div class="jobs">${jobs.map((j) => {
        const running = j.status === 'running';
        const pct = j.progress == null ? null : Math.round(j.progress);
        let statusIcon = '';
        if (running) statusIcon = icon('loader', 'spin');
        else if (j.status === 'success') statusIcon = icon('check');
        else if (j.status === 'failed') statusIcon = icon('alert');
        else if (j.status === 'manual') statusIcon = icon('info');
        let ops = '';
        if (j.status === 'queued') ops = `<button class="btn sm" data-act="job-cancel" data-id="${j.id}">取消</button>`;
        if (j.status === 'success' && j.action === 'install' && byId[j.appId]) ops = `<button class="btn sm" data-act="launch" data-id="${j.appId}">${icon('play')}启动</button>`;
        if (j.status === 'failed') ops = `<button class="btn sm" data-act="job-retry" data-id="${j.id}">${icon('refresh')}重试</button>`;
        if ((j.status === 'failed' || j.status === 'manual') && j.action === 'install' && byId[j.appId])
          ops += `<button class="btn sm" data-act="open-home" data-id="${j.appId}">${icon('external')}官网下载</button>`;
        return `
          <div class="job ${j.status}">
            ${jobAvatar(j)}
            <div class="t"><span>${esc(jobTitle(j))}</span>${j.via ? '<span class="tag">' + esc(j.via) + '</span>' : ''}</div>
            <div class="ops">${ops}</div>
            <div class="m">${statusIcon}<span>${esc(j.message)}${running && pct != null ? ' · ' + pct + '%' : ''}</span></div>
            ${running ? `<div class="bar ${pct == null ? 'busy' : ''}"><i style="width:${pct == null ? 30 : pct}%"></i></div>` : ''}
            ${j.log && j.log.length ? `<details data-job="${j.id}" ${ui.openLogs && ui.openLogs.has(j.id) ? 'open' : ''}><summary>详细输出</summary><pre>${esc(j.log.join('\n'))}</pre></details>` : ''}
          </div>`;
      }).join('')}</div>` : `<div class="empty"><div class="art">${icon('download')}</div><h3>没有任务</h3><p>在软件库中安装软件，或在「我的 AI」中更新、卸载软件时，进度会显示在这里。</p></div>`}
    `);
  }

  function viewModels() {
    const h = hw();
    const m = Recommend.modelAdvice(h || {});
    const ollama = inst('ollama'), lm = inst('lmstudio'), g4a = inst('gpt4all');
    const pulling = (tag) => S.jobs.some((j) => j.action === 'pull-model' && j.arg === tag && (j.status === 'queued' || j.status === 'running'));
    const models = (S.offline && S.offline.models) || [];
    return shell(`
      <div class="page-head"><div><h1>本地模型</h1><p>在自己的电脑上运行开源大模型：对话内容不出本机，准备好后可以断网使用。</p></div></div>
      <div class="panel">
        <div class="model-hero">
          <div><div class="muted">这台电脑推荐</div><div class="big">${esc(m.label)}</div><span class="tag">${esc(m.level)}档 · 约 ${m.sizeGB}GB</span></div>
          <div style="flex:1;min-width:260px;color:var(--text-2)">${esc(m.reason)}。Qwen3 是阿里通义开源的模型，中文能力好；规格越大越聪明，也越吃内存。
            ${m.warnings.map((w) => '<div style="color:var(--warn);margin-top:4px">' + icon('alert') + ' ' + esc(w) + '</div>').join('')}</div>
        </div>
        <div class="tierbar">${m.tiers.map((t) => `<div class="${t.key === m.tier ? 'on' : ''}"><b>${t.label}</b>${t.level} · ${t.sizeGB}GB</div>`).join('')}</div>
      </div>
      <div class="cols" style="margin-top:14px">
        <div class="panel">
          <h3>用 Ollama 一键下载</h3>
          <p class="lead">${ollama ? 'Ollama 已安装，可以直接在这里下载模型，下载进度见「下载与更新」。' : '安装 Ollama 后，可以在这里一键下载推荐的模型。'}</p>
          ${ollama ? `<div class="row">${m.tiers.map((t) => `<button class="btn ${t.key === m.tier ? 'primary' : ''} sm" data-act="pull-model" data-id="${t.ollama}" ${pulling(t.ollama) ? 'disabled' : ''}>${icon('download')}${t.ollama}</button>`).join('')}</div>
            <p class="muted" style="margin:10px 0 0">也可以在终端运行 <code class="cmd">ollama run ${esc(m.ollama)}</code> 直接对话。</p>`
            : appAction(byId.ollama)}
        </div>
        <div class="panel">
          <h3>用 LM Studio 下载</h3>
          <p class="lead">打开 LM Studio，在“发现”页搜索 <code class="cmd">${esc(m.lmSearch)}</code>，选择 Q4_K_M 版本下载即可。</p>
          ${appAction(byId.lmstudio)}
        </div>
      </div>
      <h2 class="section">${icon('folder')}离线包中的模型</h2>
      <div class="panel">
        ${models.length ? models.map((f) => `
          <div class="list-row"><span class="avatar sm" style="--c:#5b5bd6">${icon('box')}</span>
            <div class="grow"><div class="name">${esc(f.name)}</div><div class="muted">${fmtSize(f.size)}</div></div>
            <button class="btn sm" data-act="import-model" data-id="${esc(f.name)}" data-target="lmstudio" ${lm ? '' : 'disabled title="请先安装 LM Studio"'}>导入 LM Studio</button>
            <button class="btn sm" data-act="import-model" data-id="${esc(f.name)}" data-target="ollama" ${ollama ? '' : 'disabled title="请先安装 Ollama"'}>导入 Ollama</button>
            <button class="btn sm" data-act="import-model" data-id="${esc(f.name)}" data-target="gpt4all" ${g4a ? '' : 'disabled title="请先安装 GPT4All"'}>导入 GPT4All</button>
          </div>`).join('')
          : `<p class="lead" style="margin:0">没有发现离线模型。把 <code class="cmd">.gguf</code> 模型文件放到 AI Station 程序旁边的 <code class="cmd">models</code> 文件夹，就可以在这里一键导入到本地 AI 软件，适合没有网络的电脑。</p>`}
      </div>
    `);
  }

  function yesNo(v, yes, no, unknown) {
    if (v === true) return '<span class="tag ok">' + icon('check') + (yes || '是') + '</span>';
    if (v === false) return '<span class="tag warn">' + icon('x') + (no || '否') + '</span>';
    return '<span class="tag">' + (unknown || '未知') + '</span>';
  }

  function viewSettings() {
    const h = hw() || {};
    const gpus = (h.gpus || []).map((g) => esc(g.name) + (g.vramGB ? ' <small>' + g.vramGB + 'GB</small>' : '')).join('<br>') || '<small>未检测到</small>';
    const disk = h.disk && h.disk.freeGB != null ? h.disk.drive + ' 可用 ' + h.disk.freeGB + 'GB <small>/ ' + h.disk.totalGB + 'GB</small>' : '<small>未知</small>';
    const os = h.os ? esc(h.os.name) + ' <small>' + esc([h.os.version, h.os.buildFull].filter(Boolean).join(' · ')) + '</small>' : '';
    const net = S.network;
    const off = S.offline || {};
    return shell(`
      <div class="page-head"><div><h1>设置</h1><p>这台电脑的信息和 AI Station 的设置。</p></div></div>
      <div class="panel"><h3>这台电脑</h3><p class="lead">数据直接读取自系统，用于推荐软件和模型。</p>
        <div class="stats">
          <div class="stat"><div class="k">${icon('windows')}系统</div><div class="v">${os || '<small>检测中…</small>'}</div></div>
          <div class="stat"><div class="k">${icon('cpu')}处理器</div><div class="v">${esc(shortCpu(h.cpu)) || '<small>未知</small>'} <small>${h.cores ? h.cores + ' 线程 · ' + esc(h.arch || '') : ''}</small></div></div>
          <div class="stat"><div class="k">${icon('memory')}内存</div><div class="v">${h.ramGB ? h.ramGB + ' GB' : '<small>未知</small>'}</div></div>
          <div class="stat"><div class="k">${icon('monitor')}显卡</div><div class="v">${gpus}</div></div>
          <div class="stat"><div class="k">${icon('zap')}AVX2 指令集</div><div class="v">${yesNo(h.avx2, '支持', '不支持', '无法确认')}</div></div>
          <div class="stat"><div class="k">${icon('disk')}系统盘</div><div class="v">${disk}</div></div>
        </div></div>
      <div class="cols" style="margin-top:14px">
        <div class="panel"><h3>网络</h3><p class="lead">用于问卷中的网络环境预判。</p>
          <div class="list-row"><div class="grow">国内网站</div>${net ? yesNo(net.cn, '可访问', '无法访问') : '<span class="tag">检测中…</span>'}</div>
          <div class="list-row"><div class="grow">海外服务（Google）</div>${net ? yesNo(net.global, '可访问', '无法访问') : '<span class="tag">检测中…</span>'}</div>
          <div class="row" style="margin-top:10px"><button class="btn sm" data-act="net-recheck">${icon('refresh')}重新检测</button></div></div>
        <div class="panel"><h3>软件源</h3><p class="lead">自动安装依赖 Windows 自带的 winget。</p>
          <div class="list-row"><div class="grow">winget</div>${S.winget.checked ? (S.winget.available ? '<span class="tag ok">' + esc(S.winget.version) + '</span>' : '<span class="tag warn">不可用</span>') : '<span class="tag">检测中…</span>'}</div>
          ${S.winget.checked && !S.winget.available ? '<div class="row" style="margin-top:10px"><button class="btn sm" data-act="open-winget-store">获取应用安装程序</button></div>' : ''}
          <div class="list-row"><div class="grow">离线安装包 <div class="muted">${esc(off.appsDir || '程序旁边没有 apps 文件夹')}</div></div><span class="tag">${(off.installers || []).length} 个</span></div>
        </div>
      </div>
      <div class="panel" style="margin-top:14px"><h3>常规</h3>
        <div class="list-row"><div class="grow"><div class="name">开机时自动启动</div><div class="muted">开机后在托盘待命，右键托盘图标即可快速启动 AI 软件</div></div>
          <button class="toggle ${S.system.autostart ? 'on' : ''}" data-act="autostart" aria-pressed="${S.system.autostart}" title="开机时自动启动"></button></div>
        <div class="list-row"><div class="grow"><div class="name">重新获取推荐</div><div class="muted">用途或网络环境变了，可以重新做一次问卷</div></div>
          <button class="btn sm" data-act="start-quiz">重新做问卷</button></div>
        <div class="list-row"><div class="grow"><div class="name">数据与日志</div><div class="muted">${esc(S.system.dataDir)}</div></div>
          <button class="btn sm" data-act="open-data-dir">${icon('folder')}打开文件夹</button></div>
      </div>
      <p class="muted" style="margin-top:18px">AI Station v${esc(S.version)} · 只负责通过官方渠道安装和启动软件，各软件的账号、费用和数据政策以其官方说明为准。</p>
    `);
  }

  // ---------------------------------------------------------------- 浮层

  function viewDetail() {
    const a = byId[ui.detail];
    if (!a) return '';
    const info = S.installed[a.id] || {};
    const own = owned(a.id);
    const reasons = Object.keys(a.why || {}).filter((n) => (a.scores[n] || 0) >= 2);
    const ops = [];
    ops.push(appAction(a));
    if (a.kind !== 'web' && inst(a.id) && (a.install.source === 'winget' || a.install.source === 'msstore') && !jobFor(a.id))
      ops.push(`<button class="btn" data-act="upgrade" data-id="${a.id}">${icon('up')}检查更新</button>`);
    if (a.kind !== 'web' && inst(a.id) && !jobFor(a.id)) ops.push(`<button class="btn danger" data-act="uninstall" data-id="${a.id}">${icon('trash')}卸载</button>`);
    if (a.kind === 'web' && webAdded(a.id)) ops.push(`<button class="btn danger" data-act="web-remove" data-id="${a.id}">从我的 AI 移除</button>`);
    ops.push(`<button class="btn ghost" data-act="open-home" data-id="${a.id}">${icon('external')}官网</button>`);
    return `
      <div class="overlay" data-act="close-detail"></div>
      <aside class="drawer" role="dialog" aria-label="${esc(a.name)}">
        <button class="btn ghost icon close" data-act="close-detail" title="关闭">${icon('x')}</button>
        <div class="head">${avatar(a, 'lg')}<div><h2>${esc(a.name)}</h2><div class="muted">${esc(a.vendor)}</div></div></div>
        <div class="body">
          <div class="row">${regionTag(a)}${kindTag(a)}${own ? '<span class="tag ok">' + (a.kind === 'web' ? '已添加' : '已安装') + '</span>' : ''}${a.advanced ? '<span class="tag">进阶</span>' : ''}</div>
          <p>${esc(a.summary)}</p>
          ${reasons.length ? '<h3>适合用来</h3><ul>' + reasons.map((n) => '<li>' + icon('check') + '<span><b>' + esc(needById[n].label) + '</b>：' + esc(a.why[n]) + '</span></li>').join('') + '</ul>' : ''}
          ${(a.notes || []).length ? '<h3>注意事项</h3><ul>' + a.notes.map((n) => '<li>' + icon('info') + '<span>' + esc(n) + '</span></li>').join('') + '</ul>' : ''}
          <h3>安装信息</h3>
          <dl class="kv">
            <dt>安装方式</dt><dd>${esc(installLabel(a))}</dd>
            ${a.kind !== 'web' ? '<dt>状态</dt><dd>' + (info.installed ? '已安装' + (info.version ? ' · ' + esc(info.version) : '') : '未安装') + '</dd>' : ''}
            ${info.exe ? '<dt>位置</dt><dd>' + esc(info.exe) + '</dd>' : ''}
            <dt>官网</dt><dd><a href="#" data-act="open-home" data-id="${a.id}">${esc(a.homepage)}</a></dd>
          </dl>
        </div>
        <div class="foot">${ops.join('')}</div>
      </aside>`;
  }

  function viewMenu() {
    const m = ui.menu;
    if (!m) return '';
    const a = byId[m.id];
    const items = [`<button data-act="detail" data-id="${a.id}">${icon('info')}详情</button>`];
    if (a.kind !== 'web' && (a.install.source === 'winget' || a.install.source === 'msstore'))
      items.push(`<button data-act="upgrade" data-id="${a.id}">${icon('up')}检查更新</button>`);
    items.push(`<button data-act="open-home" data-id="${a.id}">${icon('external')}打开官网</button>`);
    if (a.kind === 'web') items.push(`<button class="danger" data-act="web-remove" data-id="${a.id}">${icon('x')}从我的 AI 移除</button>`);
    else items.push(`<button class="danger" data-act="uninstall" data-id="${a.id}">${icon('trash')}卸载</button>`);
    return `<div class="overlay" style="background:transparent" data-act="close-menu"></div>
      <div class="menu" style="left:${m.x}px;top:${m.y}px">${items.join('')}</div>`;
  }

  // ---------------------------------------------------------------- 渲染

  function render(navigated) {
    if (!S) return;
    let html;
    switch (ui.view) {
      case 'welcome': html = viewWelcome(); break;
      case 'quiz': html = viewQuiz(); break;
      case 'result': html = viewResult(); break;
      case 'library': html = viewLibrary(); break;
      case 'tasks': html = viewTasks(); break;
      case 'models': html = viewModels(); break;
      case 'settings': html = viewSettings(); break;
      default: html = viewHome();
    }
    html += viewDetail() + viewMenu();

    const scroller = root.querySelector('.content, .onboard');
    const top = scroller ? scroller.scrollTop : 0;
    const active = document.activeElement;
    const focusId = active && active.id;
    const caret = focusId && active.selectionStart != null ? [active.selectionStart, active.selectionEnd] : null;
    const pres = [...root.querySelectorAll('details[data-job] pre')].map((p) => [p.parentNode.dataset.job, p.scrollTop, p.scrollHeight - p.clientHeight - p.scrollTop < 8]);

    root.innerHTML = html;

    const next = root.querySelector('.content, .onboard');
    if (next && !navigated) next.scrollTop = top;
    if (focusId) {
      const el = document.getElementById(focusId);
      if (el) { el.focus(); if (caret) el.setSelectionRange(caret[0], caret[1]); }
    }
    pres.forEach(([id, st, atBottom]) => {
      const p = root.querySelector('details[data-job="' + id + '"] pre');
      if (p) p.scrollTop = atBottom ? p.scrollHeight : st;
    });
    ui.lastKey = stateKey();
  }

  function stateKey() {
    return JSON.stringify([S.jobs, S.installed, S.scanning, S.winget, S.network, S.hardware ? 1 : 0, S.profile.webShortcuts]);
  }

  // ---------------------------------------------------------------- 事件

  const handlers = {
    'skip-onboard': async () => { await finishOnboarding(); go('home'); },
    'start-quiz': () => {
      ui.quiz = { step: 0, needs: [], network: Recommend.guessNetwork(S.network), priority: null };
      const prev = S.profile.answers;
      if (prev && prev.needs) { ui.quiz.needs = prev.needs.slice(); ui.quiz.priority = prev.priority || null; ui.quiz.network = prev.network || ui.quiz.network; }
      go('quiz');
    },
    'self-pick': async () => { await finishOnboarding(); ui.lib.setup = true; go('library'); },
    'quiz-need': (el) => {
      const id = el.dataset.id;
      const n = ui.quiz.needs;
      ui.quiz.needs = n.includes(id) ? n.filter((x) => x !== id) : n.concat(id);
      render();
    },
    'quiz-network': (el) => { ui.quiz.network = el.dataset.id; render(); setTimeout(() => { if (ui.view === 'quiz' && ui.quiz.step === 1) { ui.quiz.step = 2; render(true); } }, 220); },
    'quiz-priority': (el) => { ui.quiz.priority = el.dataset.id; render(); setTimeout(() => { if (ui.view === 'quiz' && ui.quiz.step === 2) { computeRec(); go('result'); } }, 220); },
    'quiz-next': () => {
      if (ui.quiz.step < 2) { ui.quiz.step++; render(true); }
      else { computeRec(); go('result'); }
    },
    'quiz-back': () => {
      if (ui.quiz.step > 0) { ui.quiz.step--; render(true); }
      else go(S.profile.onboarded ? 'home' : 'welcome');
    },
    'quiz-restart': () => { ui.quiz.step = 0; go('quiz'); },
    'rec-toggle': (el) => {
      const id = el.dataset.id;
      if (owned(id)) return;
      if (ui.recSel.has(id)) ui.recSel.delete(id); else ui.recSel.add(id);
      render();
    },
    'rec-browse': async () => {
      await finishOnboarding({ answers: ui.rec.answers, recommendation: ui.rec.picks.map((p) => p.id) });
      ui.lib.sel = new Set(ui.recSel);
      ui.lib.setup = true;
      go('library');
    },
    'rec-confirm': async () => {
      const ids = [...ui.recSel];
      await finishOnboarding({ answers: ui.rec.answers, recommendation: ui.rec.picks.map((p) => p.id) });
      await install(ids);
      const anyDesktop = ids.some((id) => byId[id].kind !== 'web');
      go(anyDesktop ? 'tasks' : 'home');
    },
    nav: (el) => { if (el.dataset.id !== 'library') ui.lib.setup = false; go(el.dataset.id); },
    refresh: async () => { try { await api('refresh', {}); S.scanning = true; render(); schedulePoll(800); } catch (e) { fail(e); } },
    launch: (el) => launch(el.dataset.id),
    'install-one': (el) => install([el.dataset.id]),
    'tile-menu': (el, ev) => {
      const r = el.getBoundingClientRect();
      ui.menu = { id: el.dataset.id, x: Math.min(r.left, window.innerWidth - 200), y: Math.min(r.bottom + 4, window.innerHeight - 220) };
      render();
    },
    'close-menu': () => { ui.menu = null; render(); },
    detail: (el) => { ui.menu = null; ui.detail = el.dataset.id; render(); },
    'close-detail': () => { ui.detail = null; render(); },
    'open-home': async (el) => { try { await api('open', { id: el.dataset.id, target: 'home' }); } catch (e) { fail(e); } },
    'open-winget-store': async () => { try { await api('open', { target: 'winget-store' }); } catch (e) { fail(e); } },
    upgrade: (el) => { ui.menu = null; startJobs([{ action: 'upgrade', appId: el.dataset.id }], '正在检查 ' + byId[el.dataset.id].name + ' 的更新'); },
    uninstall: (el) => {
      const a = byId[el.dataset.id];
      ui.menu = null;
      if (!confirm('确定要卸载 ' + a.name + ' 吗？')) { render(); return; }
      ui.detail = null;
      startJobs([{ action: 'uninstall', appId: a.id }], '正在卸载 ' + a.name);
    },
    'web-remove': async (el) => {
      try { S.profile = await api('web-shortcut', { id: el.dataset.id, add: false }); ui.menu = null; ui.detail = null; render(); }
      catch (e) { fail(e); }
    },
    'lib-region': (el) => { ui.lib.region = el.dataset.id; render(); },
    'lib-cat': (el) => { ui.lib.cat = el.dataset.id; render(); },
    'lib-toggle': (el) => {
      const id = el.dataset.id;
      if (ui.lib.sel.has(id)) ui.lib.sel.delete(id); else ui.lib.sel.add(id);
      render();
    },
    'lib-clear': () => { ui.lib.sel.clear(); render(); },
    'lib-install': async () => {
      const ids = [...ui.lib.sel].filter((id) => !owned(id));
      ui.lib.sel.clear();
      ui.lib.setup = false;
      await install(ids);
      if (ids.some((id) => byId[id].kind !== 'web')) go('tasks');
    },
    'upgrade-all': () => {
      const ids = apps.filter((a) => a.kind !== 'web' && inst(a.id) && (a.install.source === 'winget' || a.install.source === 'msstore')).map((a) => a.id);
      startJobs(ids.map((id) => ({ action: 'upgrade', appId: id })), '正在检查 ' + ids.length + ' 个软件的更新');
    },
    'jobs-clear': async () => { try { const r = await api('jobs/clear', {}); S.jobs = r.jobs; render(); } catch (e) { fail(e); } },
    'job-cancel': async (el) => { try { const r = await api('jobs/cancel', { id: +el.dataset.id }); S.jobs = r.jobs; render(); } catch (e) { fail(e); } },
    'job-retry': (el) => {
      const j = S.jobs.find((x) => x.id === +el.dataset.id);
      if (j) startJobs([{ action: j.action, appId: j.appId, arg: j.arg, target: j.target }], '已重新加入队列');
    },
    'pull-model': (el) => startJobs([{ action: 'pull-model', appId: 'ollama', arg: el.dataset.id }], '开始下载 ' + el.dataset.id + '，进度见「下载与更新」'),
    'import-model': (el) => startJobs([{ action: 'import-model', appId: el.dataset.target, arg: el.dataset.id, target: el.dataset.target }], '开始导入 ' + el.dataset.id),
    'net-recheck': async () => {
      S.network = null; render();
      try { S.network = await api('network', {}); } catch (e) { fail(e); }
      render();
    },
    autostart: async () => {
      try { await api('autostart', { enabled: !S.system.autostart }); S.system.autostart = !S.system.autostart; render(); }
      catch (e) { fail(e); }
    },
    'open-data-dir': async () => { try { await api('open-data-dir', {}); } catch (e) { fail(e); } }
  };

  document.addEventListener('click', (ev) => {
    const el = ev.target.closest('[data-act]');
    if (!el || !root.contains(el) && !el.closest('.toasts')) return;
    if (el.tagName === 'A') ev.preventDefault();
    const fn = handlers[el.dataset.act];
    if (!fn) return;
    ev.stopPropagation();
    Promise.resolve(fn(el, ev)).catch(fail);
  });

  document.addEventListener('keydown', (ev) => {
    if (ev.key === 'Escape' && (ui.detail || ui.menu)) { ui.detail = null; ui.menu = null; render(); return; }
    if ((ev.key === 'Enter' || ev.key === ' ') && ev.target.matches('[role="button"][data-act], [role="checkbox"][data-act], .card[data-act]')) {
      ev.preventDefault();
      ev.target.click();
    }
  });

  document.addEventListener('input', (ev) => {
    if (ev.target.id === 'lib-search') { ui.lib.q = ev.target.value; render(); }
  });

  document.addEventListener('toggle', (ev) => {
    const d = ev.target;
    if (!d.dataset || !d.dataset.job) return;
    ui.openLogs = ui.openLogs || new Set();
    const id = +d.dataset.job;
    if (d.open) ui.openLogs.add(id); else ui.openLogs.delete(id);
  }, true);

  // ---------------------------------------------------------------- 轮询

  let pollTimer = null;
  let lastFull = 0;
  let hadActive = false;

  function schedulePoll(ms) {
    clearTimeout(pollTimer);
    pollTimer = setTimeout(poll, ms);
  }

  async function poll() {
    try {
      const busy = activeJobs().length > 0 || S.scanning || !S.hardware || !S.network || !S.winget.checked;
      if (!busy || Date.now() - lastFull > 15000 || hadActive) {
        setState(await api('state'));
        lastFull = Date.now();
      } else {
        const r = await api('jobs');
        S.jobs = r.jobs; S.installed = r.installed; S.scanning = r.scanning;
      }
      // 任务刚结束：给出提示
      const nowActive = activeJobs().length > 0;
      hadActive = nowActive;
      if (stateKey() !== ui.lastKey && !ui.menu) render();
      schedulePoll(nowActive || S.scanning || !S.hardware || !S.network ? 1000 : 5000);
    } catch (e) {
      schedulePoll(3000);
    }
  }

  setInterval(() => { api('ping', {}).catch(() => {}); }, 5000);

  window.addEventListener('focus', () => schedulePoll(50));

  // ---------------------------------------------------------------- 启动

  (async function boot() {
    if (!TOKEN) {
      root.innerHTML = '<div class="boot">缺少访问令牌，请从托盘图标重新打开 AI Station。</div>';
      return;
    }
    try {
      setState(await api('state'));
    } catch (e) {
      root.innerHTML = '<div class="boot">无法连接 AI Station：' + esc(e.message) + '</div>';
      return;
    }
    api('ping', {}).catch(() => {});
    ui.view = S.profile.onboarded ? 'home' : 'welcome';
    render(true);
    lastFull = Date.now();
    schedulePoll(800);
  })();

  // 供自动化测试读取
  window.__aistation = { ui, get state() { return S; } };
})();
