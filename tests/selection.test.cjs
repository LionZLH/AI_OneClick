const { test } = require('node:test');
const assert = require('node:assert/strict');
const { apps, scenes, systems, networks, unavailable, recommend, filterApps, configuration, generateScript } = require('../prototype/catalog.js');

test('目录条目唯一，推荐都指向存在的工具与 HTTPS 官方入口', () => {
  assert.equal(new Set(apps.map(app => app.id)).size, apps.length);
  assert.equal(apps.length, 19);
  for (const app of apps) {
    assert.equal(new URL(app.url).protocol, 'https:');
    assert.ok(app.note);
  }
  for (const scene of scenes) for (const id of Object.values(scene.picks)) {
    if (id) assert.ok(apps.some(app => app.id === id), `${scene.id}: ${id}`);
  }
});
test('所有系统和网络组合：推荐去重、保留理由且符合所选环境', () => {
  for (const system of Object.keys(systems)) for (const network of Object.keys(networks)) {
    const result = recommend(scenes.map(scene => scene.id), system, network);
    assert.equal(new Set(result.matches.map(match => match.app.id)).size, result.matches.length);
    assert.equal(result.matches.reduce((n, match) => n + match.reasons.length, 0) + result.unsupported.length, scenes.length);
    for (const { app, reasons } of result.matches) {
      assert.equal(unavailable(app, system, network), '');
      assert.ok(reasons.length);
    }
  }
});
test('离线不推荐联网搜索、公众号和语音；重复需求不重复计算', () => {
  const result = recommend(['search', 'wechat', 'voice', 'local_llm', 'local_llm'], 'windows', 'offline');
  assert.equal(result.unsupported.length, 3);
  assert.deepEqual(result.matches.map(match => match.app.id), ['lmstudio']);
  assert.deepEqual(result.matches[0].reasons, ['运行本地模型']);
  assert.ok(filterApps({ system: 'windows', network: 'offline' }).every(app => app.group === 'offline'));
});
test('搜索按名称、用途和厂商匹配，并与分类和使用方式共同筛选', () => {
  const env = { system: 'macos', network: 'global' };
  assert.deepEqual(filterApps({ ...env, query: '  CURSOR ' }).map(app => app.id), ['cursor']);
  assert.ok(filterApps({ ...env, query: '字节跳动' }).some(app => app.id === 'doubao'));
  assert.equal(filterApps({ ...env, group: 'offline' }).length, 4);
  assert.ok(filterApps({ ...env, desktopOnly: true }).every(app => app.type === 'desktop'));
  assert.deepEqual(filterApps({ ...env, query: '<script>不存在</script>' }), []);
  assert.deepEqual(filterApps({ ...env, group: 'offline', query: 'Cursor' }), []);
  assert.ok(filterApps({ system: 'windows', network: 'domestic' }).every(app => app.group !== 'global'));
});
test('导出的清单包含实际选择、系统、网络、官方入口与准备条件', () => {
  const content = configuration(['lmstudio', 'spark', 'lmstudio'], 'macos', 'domestic');
  assert.match(content, /目标系统：macOS/);
  assert.match(content, /网络环境：国内普通网络/);
  assert.equal((content.match(/## LM Studio/g) || []).length, 1);
  assert.match(content, /https:\/\/lmstudio.ai\/download/);
  assert.match(content, /需提前准备模型及运行时/);
  assert.match(content, /网页服务/);
  assert.match(content, /未执行安装/);
  assert.doesNotMatch(content, /Claude/);
});
test('无效场景、无效系统、空清单与不兼容选择会被拒绝', () => {
  assert.throws(() => recommend(['unknown'], 'windows', 'domestic'));
  assert.throws(() => recommend([], 'linux', 'offline'));
  assert.throws(() => configuration([], 'windows', 'domestic'));
  assert.throws(() => configuration(['unknown'], 'windows', 'domestic'));
  assert.throws(() => configuration(['chatgpt'], 'windows', 'offline'));
  assert.throws(() => configuration(['cursor'], 'windows', 'domestic'));
});
test('generateScript 正确生成 Windows PowerShell 和 macOS Bash 安装脚本', () => {
  const ps = generateScript(['doubao', 'lmstudio', 'spark'], 'windows', 'domestic');
  assert.match(ps, /#Requires -Version 5\.1/);
  assert.match(ps, /winget install 'ByteDance\.Doubao'/);
  assert.match(ps, /lms\.exe/);
  assert.match(ps, /Avx2/);
  assert.match(ps, /讯飞星火/);

  const sh = generateScript(['claude', 'cursor', 'gemini'], 'macos', 'global');
  assert.match(sh, /#!\/usr\/bin\/env bash/);
  assert.match(sh, /brew install --cask claude/);
  assert.match(sh, /brew install --cask cursor/);
  assert.match(sh, /Gemini/);

  assert.throws(() => generateScript(['doubao'], 'linux', 'domestic'));
});

