'use strict';
const consoleCatalog = typeof AIStation !== 'undefined' ? AIStation : require('./catalog.js');

function resolveConsoleApps(ids) {
  const wanted = new Set(ids);
  return consoleCatalog.apps.filter(app => wanted.has(app.id));
}
if (typeof module !== 'undefined') module.exports = { resolveConsoleApps };

if (typeof document !== 'undefined') {
  const params = new URLSearchParams(location.search);
  // shortcut: preview entries never represent installed software; use verified installation receipts when the desktop installer is connected.
  let entries = resolveConsoleApps(params.has('preview') ? params.get('preview').split(',') : ['doubao', 'kimi', 'lmstudio']);
  const search = document.getElementById('console-search');
  const tools = document.getElementById('tools');
  function renderConsole() {
    const query = search.value.trim().toLocaleLowerCase();
    const visible = entries.filter(app => [app.name, app.desc, ...app.tags].join(' ').toLocaleLowerCase().includes(query));
    tools.innerHTML = visible.map(app => `<article class="tool console-app">
      <div class="console-copy"><h2>${app.name}</h2><p>${app.desc}</p></div>
      <div class="console-actions">${app.type === 'web' ? `<a class="primary" href="${app.url}" target="_blank" rel="noopener noreferrer" aria-label="打开 ${app.name} 官方网页（新窗口）">打开网页 ↗</a>` : `<button type="button" class="primary" disabled aria-label="启动 ${app.name}（预览中暂不可用）" title="桌面启动尚未开放">启动</button>`}<button type="button" class="remove-entry" data-remove="${app.id}" aria-label="移除 ${app.name} 的演示入口" title="仅移除演示入口，不卸载软件">移除入口</button></div>
    </article>`).join('');
    const empty = document.getElementById('console-empty');
    empty.hidden = !!visible.length;
    empty.textContent = entries.length ? '没有找到这个应用。' : '还没有应用入口，点击“添加软件”开始配置。';
    document.getElementById('console-count').textContent = query ? `找到 ${visible.length} 款 · 共 ${entries.length} 个演示入口` : `${entries.length} 个演示入口`;
  }
  search.addEventListener('input', renderConsole);
  tools.addEventListener('click', event => {
    const button = event.target.closest('[data-remove]');
    if (!button) return;
    const app = entries.find(item => item.id === button.dataset.remove);
    if (!app) return;
    entries = entries.filter(item => item.id !== app.id);
    renderConsole(); search.focus();
    const notice = document.getElementById('console-notice');
    notice.textContent = `已移除 ${app.name} 的演示入口，未卸载任何软件。`;
    notice.hidden = false;
  });
  renderConsole();
}
