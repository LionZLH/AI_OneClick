'use strict';
const { apps, scenes, systems, networks, recommend, filterApps, unavailable, configuration, generateScript } = AIStation;
const $ = id => document.getElementById(id);
const state = { mode: null, step: 'network', review: false, scenes: new Set(), selected: new Set() };

function notify(message) {
  $('notice').textContent = message;
  $('notice').hidden = !message;
}
function renderScenes() {
  $('scenes').innerHTML = scenes.map(scene => `<button type="button" data-scene="${scene.id}" aria-pressed="${state.scenes.has(scene.id)}" ${!scene.picks[$('network').value] ? 'disabled title="此用途需要联网"' : ''}>${scene.name}</button>`).join('');
  $('scene-note').hidden = $('network').value !== 'offline';
  $('scene-note').textContent = '离线编程仅指代码问答；纪要整理需已有文字稿。';
  updateSceneCount();
}
function updateSceneCount() {
  $('generate').disabled = !state.scenes.size;
  $('scene-count').textContent = state.scenes.size ? `已选 ${state.scenes.size} 项；生成后更新清单` : '可多选';
}
function showView(focusQuestion = false) {
  $('wizard').hidden = state.mode !== 'wizard' || state.review;
  $('custom').hidden = state.mode !== 'custom' || state.review;
  document.querySelector('.entries').classList.toggle('home', state.mode === null);
  document.querySelector('.entries').hidden = state.review;
  $('review').hidden = !state.review;
  document.querySelector('footer').hidden = state.review || !state.mode || (state.mode === 'wizard' && state.step !== 'result');
  for (const step of ['network', 'scenes', 'result']) $('question-' + step).hidden = state.step !== step;
  $('list-panel').hidden = state.review || !state.mode || (state.mode === 'wizard' && state.step !== 'result');
  document.querySelectorAll('[data-mode]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.mode === state.mode)));
  renderTools();
  renderSelection();
  if (focusQuestion && state.mode === 'wizard') $('question-' + state.step).querySelector('h2').focus();
}
function renderTools() {
  let visible;
  if (state.mode === 'wizard') {
    const { matches } = recommend(state.scenes, $('system').value, $('network').value);
    visible = matches.map(({ app }) => app);
    $('result-count').textContent = `${state.scenes.size} 个需求，匹配 ${visible.length} 款工具。可取消不需要的。`;
  } else {
    visible = filterApps({ system: $('system').value, network: $('network').value, query: $('search').value });
  }
  $('filter-summary').hidden = !$('search').value.trim();
  $('match-count').textContent = `匹配 ${visible.length} 款`;
  $('tools').innerHTML = visible.map(app => `<div class="tool">
    <label><input type="checkbox" data-app="${app.id}" aria-label="选择 ${app.name}" ${state.selected.has(app.id) ? 'checked' : ''}><span class="tool-copy"><span class="tool-name">${app.name}${app.type === 'web' ? '<small>网页版</small>' : app.group === 'offline' ? '<small>本地 · 需备模型</small>' : ''}</span><span class="tool-desc">${app.desc}</span></span></label>
    <a href="${app.url}" target="_blank" rel="noopener noreferrer" title="${app.note}" aria-label="前往 ${app.name} 官网（新窗口）">官网 ↗</a>
  </div>`).join('');
  $('empty').hidden = !!visible.length;
}
function renderSelection() {
  $('count').textContent = state.selected.size;
  $('review-plan').disabled = $('export').disabled = !state.selected.size;
  $('clear').hidden = !state.selected.size;
  $('selected').innerHTML = [...state.selected].map(id => {
    const app = apps.find(item => item.id === id);
    return `<button type="button" data-remove="${id}" aria-label="移除 ${app.name}">${app.name} ×</button>`;
  }).join('');
  const showingTools = state.review || state.mode === 'custom' || (state.mode === 'wizard' && state.step === 'result');
  $('offline-note').hidden = !showingTools || ($('network').value !== 'offline' && ![...state.selected].some(id => apps.find(app => app.id === id).group === 'offline'));
  document.querySelectorAll('[data-app]').forEach(input => { input.checked = state.selected.has(input.dataset.app); });
}
function changeEnvironment() {
  let removed = 0;
  for (const id of state.selected) {
    if (unavailable(apps.find(app => app.id === id), $('system').value, $('network').value)) {
      state.selected.delete(id);
      removed++;
    }
  }
  for (const id of state.scenes) {
    if (!scenes.find(scene => scene.id === id).picks[$('network').value]) state.scenes.delete(id);
  }
  state.step = 'network';
  state.review = false;
  renderScenes(); showView(); renderSelection();
  notify(removed ? `已移除 ${removed} 款不适用于当前网络或系统的工具。` : '');
}
$('system').addEventListener('change', changeEnvironment);
$('network').addEventListener('change', changeEnvironment);
$('search').addEventListener('input', renderTools);
document.querySelectorAll('[data-mode]').forEach(button => button.addEventListener('click', () => {
  state.mode = button.dataset.mode;
  state.review = false;
  notify(''); showView();
}));
document.querySelectorAll('[data-network]').forEach(button => button.addEventListener('click', () => {
  $('network').value = button.dataset.network;
  changeEnvironment();
  state.step = 'scenes'; showView(true);
}));
$('back').addEventListener('click', () => { state.step = 'network'; showView(true); });
$('edit').addEventListener('click', () => { state.step = 'scenes'; showView(true); });
$('generate').addEventListener('click', () => {
  if (!state.scenes.size) return;
  const { matches } = recommend(state.scenes, $('system').value, $('network').value);
  state.selected = new Set(matches.map(({ app }) => app.id));
  state.step = 'result';
  notify(''); showView(true); renderSelection();
});
$('scenes').addEventListener('click', event => {
  const button = event.target.closest('[data-scene]');
  if (!button || button.disabled) return;
  const id = button.dataset.scene;
  if (state.scenes.has(id)) state.scenes.delete(id); else state.scenes.add(id);
  button.setAttribute('aria-pressed', String(state.scenes.has(id)));
  updateSceneCount();
});
$('tools').addEventListener('change', event => {
  const id = event.target.dataset.app;
  if (!id) return;
  if (event.target.checked) state.selected.add(id); else state.selected.delete(id);
  notify(''); renderSelection();
});
$('selected').addEventListener('click', event => {
  const button = event.target.closest('[data-remove]');
  if (!button) return;
  state.selected.delete(button.dataset.remove);
  renderSelection();
  $('selection').querySelector('summary').focus();
});
$('clear').addEventListener('click', () => {
  state.selected.clear(); renderSelection(); notify('');
  $('selection').querySelector('summary').focus();
});
$('reset').addEventListener('click', () => {
  $('search').value = '';
  renderTools(); $('search').focus();
});
function syncHardwareLink() {
  const isWin = $('system').value === 'windows';
  const hwLink = $('check-hardware-link');
  if (hwLink) {
    hwLink.href = isWin ? '../scripts/check-hardware.ps1' : '../scripts/check-hardware.sh';
    hwLink.download = isWin ? 'check-hardware.ps1' : 'check-hardware.sh';
    hwLink.textContent = isWin ? '下载 Windows 硬件体检工具 ⤓' : '下载 macOS 硬件体检工具 ⤓';
  }
}

$('review-plan').addEventListener('click', () => {
  if (!state.selected.size) return;
  $('console-preview').href = `console.html?preview=${encodeURIComponent([...state.selected].join(','))}`;
  $('review-context').textContent = `${systems[$('system').value]} · ${networks[$('network').value]} · ${state.selected.size} 款工具`;
  const isOffline = $('network').value === 'offline';
  const reviewNote = $('review-note');
  if (reviewNote) {
    reviewNote.textContent = isOffline
      ? '离线模式：将生成离线安装包配套脚本。请将脚本置于网盘完整包目录执行。'
      : '在线模式：将生成全自动一键安装脚本（自动通过系统包管理器安装 + 拉取推荐本地模型）。';
  }
  const autoBtn = $('auto-install');
  if (autoBtn) {
    autoBtn.textContent = isOffline ? '下载离线安装脚本' : '生成一键安装脚本';
  }
  $('configuration-list').innerHTML = [...state.selected].map(id => {
    const app = apps.find(item => item.id === id);
    return `<li><strong>${app.name}</strong><span>${app.type === 'web' ? '配置网页入口' : '安装桌面客户端'}</span><p>${app.note}</p></li>`;
  }).join('');
  syncHardwareLink();
  state.review = true; notify(''); showView();
  $('review').querySelector('h2').focus();
});
$('review-back').addEventListener('click', () => { state.review = false; showView(); $('review-plan').focus(); });
$('export').addEventListener('click', () => {
  try {
    const content = configuration(state.selected, $('system').value, $('network').value);
    const url = URL.createObjectURL(new Blob(['\uFEFF', content], { type: 'text/markdown;charset=utf-8' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = `AI-Station-${$('system').value}-清单.md`;
    document.body.append(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    notify('已发起清单下载，软件需前往官网安装。');
  } catch (error) { notify(error.message); }
});
$('auto-install').addEventListener('click', () => {
  try {
    const isWin = $('system').value === 'windows';
    const isOffline = $('network').value === 'offline';
    if (isOffline) {
      const ext = isWin ? 'ps1' : 'sh';
      const link = document.createElement('a');
      link.href = `../scripts/offline-install.${ext}`;
      link.download = `offline-install.${ext}`;
      document.body.append(link); link.click(); link.remove();
      notify(`已发起离线安装脚本（offline-install.${ext}）下载，请解压网盘离线包后在包根目录下运行。`);
      return;
    }
    const scriptContent = generateScript(state.selected, $('system').value, $('network').value);
    const ext = isWin ? 'ps1' : 'sh';
    const mime = isWin ? 'text/plain;charset=utf-8' : 'text/x-sh;charset=utf-8';
    const blob = new Blob([isWin ? '\uFEFF' + scriptContent : scriptContent], { type: mime });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `AI-Station-${$('system').value}-一键安装.${ext}`;
    document.body.append(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    notify(isWin
      ? '已生成一键安装脚本！右键选择【使用 PowerShell 运行】（建议管理员权限）即可全自动完成配置。'
      : '已生成一键安装脚本！在终端执行 bash AI-Station-macos-一键安装.sh 即可全自动完成配置。');
  } catch (error) {
    notify(error.message);
  }
});
renderScenes(); showView(); renderSelection(); syncHardwareLink();
