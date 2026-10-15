const { test } = require('node:test');
const assert = require('node:assert/strict');
const { resolveConsoleApps } = require('../prototype/console.js');

test('控制台只解析已收录的条目，去重并排除未知输入', () => {
  const result = resolveConsoleApps(['kimi', 'kimi', 'doubao', 'unknown', '<script>alert(1)</script>']);
  assert.deepEqual(result.map(app => app.id), ['doubao', 'kimi']);
  assert.ok(result.every(app => new URL(app.url).protocol === 'https:'));
  assert.ok(result.every(app => !Object.hasOwn(app, 'installed')));
});
test('空预览显示为空，不补充或伪造已安装的软件', () => {
  assert.deepEqual(resolveConsoleApps([]), []);
  assert.deepEqual(resolveConsoleApps(['not-installed']), []);
});
