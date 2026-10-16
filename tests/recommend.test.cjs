const test = require('node:test');
const assert = require('node:assert');
const path = require('node:path');
const fs = require('node:fs');

const R = require('../ui/recommend.js');
const catalog = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'ui', 'catalog.json'), 'utf8'));
const byId = Object.fromEntries(catalog.apps.map((a) => [a.id, a]));

const HW16 = { ramGB: 15.8, avx2: true, gpus: [{ name: 'Intel(R) UHD Graphics 630', vramGB: 1 }] };
const ids = (r) => r.picks.map((p) => p.id);

test('国内网络不会推荐需要海外网络的软件', () => {
  for (const priority of ['easy', 'best', 'private']) {
    const r = R.recommend(catalog, { needs: catalog.needs.map((n) => n.id), network: 'cn', priority }, HW16);
    for (const p of [...r.picks, ...r.alternates]) {
      assert.notStrictEqual(byId[p.id].region, 'global', `${priority}: ${p.id}`);
    }
  }
});

test('完全离线只推荐本地软件，并说明无法覆盖的用途', () => {
  const r = R.recommend(catalog, { needs: ['chat', 'search', 'private'], network: 'offline', priority: 'easy' }, HW16);
  assert.ok(r.picks.length > 0);
  for (const p of r.picks) assert.strictEqual(byId[p.id].region, 'local');
  assert.deepStrictEqual(r.uncovered.map((u) => u.need), ['search']);
  assert.match(r.uncovered[0].reason, /离线/);
  assert.ok(r.model, '选了本地软件时应给出模型建议');
});

test('简单省心模式用尽量少的软件覆盖多个用途', () => {
  const r = R.recommend(catalog, { needs: ['chat', 'voice', 'create'], network: 'cn', priority: 'easy' }, HW16);
  assert.deepStrictEqual(ids(r), ['doubao']);
  assert.deepStrictEqual(r.picks[0].reasons.map((x) => x.need), ['chat', 'voice', 'create']);
  assert.strictEqual(r.uncovered.length, 0);
});

test('简单省心模式最多 3 款，能力全面模式最多 6 款', () => {
  const all = catalog.needs.map((n) => n.id);
  const easy = R.recommend(catalog, { needs: all, network: 'global', priority: 'easy' }, HW16);
  const best = R.recommend(catalog, { needs: all, network: 'global', priority: 'best' }, HW16);
  assert.ok(easy.picks.length <= 3);
  assert.ok(best.picks.length <= 6);
  assert.ok(best.picks.length > easy.picks.length);
});

test('能力全面模式为单个用途给出两个选择', () => {
  const r = R.recommend(catalog, { needs: ['code'], network: 'global', priority: 'best' }, HW16);
  assert.strictEqual(r.picks.length, 2);
  for (const p of r.picks) assert.ok(byId[p.id].scores.code >= 3);
});

test('国内写代码推荐 TRAE，海外网络推荐 Claude/Cursor 等', () => {
  assert.deepStrictEqual(ids(R.recommend(catalog, { needs: ['code'], network: 'cn', priority: 'easy' }, HW16)), ['trae']);
  const g = ids(R.recommend(catalog, { needs: ['code'], network: 'global', priority: 'easy' }, HW16));
  assert.strictEqual(g.length, 1);
  assert.ok(['claude', 'cursor', 'antigravity'].includes(g[0]));
});

test('隐私优先时优先本地软件', () => {
  const r = R.recommend(catalog, { needs: ['chat', 'docs'], network: 'cn', priority: 'private' }, HW16);
  assert.strictEqual(byId[r.picks[0].id].region, 'local');
});

test('CPU 不支持 AVX2 时不推荐 LM Studio', () => {
  const hw = { ...HW16, avx2: false };
  const r = R.recommend(catalog, { needs: ['private', 'chat'], network: 'offline', priority: 'best' }, hw);
  assert.ok(!ids(r).includes('lmstudio'));
  assert.ok(r.blocked.lmstudio);
  assert.ok(r.picks.length > 0);
});

test('内存过小时不推荐本地软件', () => {
  const r = R.recommend(catalog, { needs: ['private'], network: 'offline', priority: 'easy' }, { ramGB: 3.9, avx2: true, gpus: [] });
  assert.strictEqual(r.picks.length, 0);
  assert.deepStrictEqual(r.uncovered.map((u) => u.need), ['private']);
});

test('推荐理由来自目录文案，备选不与主推荐重复', () => {
  const r = R.recommend(catalog, { needs: ['docs', 'search', 'office'], network: 'cn', priority: 'easy' }, HW16);
  const picked = new Set(ids(r));
  for (const p of r.picks) for (const reason of p.reasons) assert.ok(reason.text.length > 0, `${p.id}/${reason.need}`);
  for (const a of r.alternates) assert.ok(!picked.has(a.id));
});

test('未选用途时默认按日常问答推荐', () => {
  const r = R.recommend(catalog, { needs: [], network: 'cn', priority: 'easy' }, HW16);
  assert.deepStrictEqual(r.answers.needs, ['chat']);
  assert.strictEqual(r.picks.length, 1);
});

test('模型建议随内存与显存分档', () => {
  assert.strictEqual(R.modelAdvice({ ramGB: 3.8, gpus: [] }).tier, '1.7b');
  assert.strictEqual(R.modelAdvice({ ramGB: 7.8, gpus: [] }).tier, '4b');
  assert.strictEqual(R.modelAdvice({ ramGB: 15.8, gpus: [] }).tier, '8b');
  assert.strictEqual(R.modelAdvice({ ramGB: 31.9, gpus: [] }).tier, '14b');
  assert.strictEqual(R.modelAdvice({ ramGB: 15.8, gpus: [{ name: 'NVIDIA GeForce RTX 4080', vramGB: 16 }] }).tier, '14b');
  assert.strictEqual(R.modelAdvice({ ramGB: 0, gpus: [] }).tier, '4b');
  assert.ok(R.modelAdvice({ ramGB: 16, avx2: false, gpus: [] }).warnings.length > 0);
});

test('网络探测结果映射到问卷默认选项', () => {
  assert.strictEqual(R.guessNetwork({ cn: true, global: true }), 'global');
  assert.strictEqual(R.guessNetwork({ cn: true, global: false }), 'cn');
  assert.strictEqual(R.guessNetwork({ cn: false, global: false }), 'offline');
  assert.strictEqual(R.guessNetwork(null), null);
});

test('读文档 + 写代码（国内、省心）推荐 Kimi 和 TRAE，不会被综合办公工具替代', () => {
  const r = R.recommend(catalog, { needs: ['docs', 'code'], network: 'cn', priority: 'easy' }, HW16);
  assert.deepStrictEqual(ids(r).sort(), ['kimi', 'trae']);
});

test('本地私有 + 读文档时推荐真正的本地 AI，而不是只推笔记软件', () => {
  for (const network of ['offline', 'cn']) {
    for (const priority of ['easy', 'private']) {
      const r = R.recommend(catalog, { needs: ['private', 'docs'], network, priority }, HW16);
      assert.ok(ids(r).some((id) => ['lmstudio', 'gpt4all', 'anythingllm', 'ollama', 'jan'].includes(id)), `${network}/${priority}: ${ids(r)}`);
      assert.notDeepStrictEqual(ids(r), ['obsidian'], `${network}/${priority}`);
    }
  }
});
