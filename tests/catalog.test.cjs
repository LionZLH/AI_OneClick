const test = require('node:test');
const assert = require('node:assert');
const path = require('node:path');
const fs = require('node:fs');

const catalog = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'ui', 'catalog.json'), 'utf8'));
const needIds = new Set(catalog.needs.map((n) => n.id));

test('软件 ID 唯一，必填字段齐全', () => {
  const seen = new Set();
  for (const app of catalog.apps) {
    assert.ok(/^[a-z0-9]+$/.test(app.id), app.id);
    assert.ok(!seen.has(app.id), `重复 ID ${app.id}`);
    seen.add(app.id);
    for (const key of ['name', 'vendor', 'summary', 'letter', 'color', 'homepage']) {
      assert.ok(app[key], `${app.id} 缺少 ${key}`);
    }
    assert.ok(['desktop', 'web'].includes(app.kind), app.id);
    assert.ok(catalog.regions[app.region], app.id);
    assert.match(app.color, /^#[0-9a-f]{6}$/i, app.id);
  }
});

test('链接全部为 HTTPS', () => {
  for (const app of catalog.apps) {
    for (const url of [app.homepage, app.webUrl].filter(Boolean)) {
      assert.ok(url.startsWith('https://'), `${app.id}: ${url}`);
    }
  }
});

test('评分与推荐理由只引用已定义的用途，且得分 ≥2 的用途都有理由', () => {
  for (const app of catalog.apps) {
    for (const [need, score] of Object.entries(app.scores)) {
      assert.ok(needIds.has(need), `${app.id}: ${need}`);
      assert.ok(score >= 0 && score <= 3, `${app.id}: ${need}`);
      if (score >= 2) assert.ok(app.why && app.why[need], `${app.id} 缺少 ${need} 的推荐理由`);
    }
  }
});

test('安装方式与软件类型一致', () => {
  for (const app of catalog.apps) {
    const src = app.install && app.install.source;
    if (app.kind === 'web') {
      assert.strictEqual(src, 'web', app.id);
      assert.ok(app.webUrl, app.id);
    } else {
      assert.ok(['winget', 'msstore', 'manual'].includes(src), app.id);
      if (src === 'winget') assert.match(app.install.id, /^[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$/, app.id);
      if (src === 'msstore') assert.match(app.install.id, /^[0-9A-Z]{12}$/, app.id);
      assert.ok(app.detect && (app.detect.displayName || app.detect.pfn), `${app.id} 缺少识别规则`);
      if (app.detect.displayName) new RegExp(app.detect.displayName, 'i');
    }
  }
});

test('离线安装配置合法', () => {
  for (const app of catalog.apps) {
    if (!app.offline) continue;
    assert.ok(Array.isArray(app.offline.match) && app.offline.match.length, app.id);
    assert.ok(['nullsoft', 'inno', 'exe', 'msi'].includes(app.offline.installerType), app.id);
  }
});

test('本地软件声明了硬件要求', () => {
  for (const app of catalog.apps.filter((a) => a.region === 'local')) {
    assert.ok(app.requires && app.requires.ramGB, app.id);
  }
});
