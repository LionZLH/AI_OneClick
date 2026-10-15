// 端到端测试：启动 AIStation.exe（--headless），用 Playwright 走完首次配置 → 安装 → 启动台 → 软件库等流程。
// 用法：
//   Linux（Mono + 模拟 winget）：node tests/e2e.mjs
//   Windows（真实 winget）：     node tests/e2e.mjs --exe dist\AIStation.exe --real
// 截图输出到 --out 指定目录（默认 test-results/）。
import { spawn } from 'node:child_process';
import { mkdirSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
let playwright;
try { playwright = require('playwright'); } catch { playwright = require(path.join(process.execPath, '..', '..', 'lib', 'node_modules', 'playwright')); }
const { chromium } = playwright;

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '..');
const args = process.argv.slice(2);
const opt = (name, def) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : def; };
const REAL = args.includes('--real');
const exe = path.resolve(opt('--exe', path.join(repo, 'dist', 'AIStation.exe')));
const out = path.resolve(opt('--out', path.join(repo, 'test-results')));
const port = Number(opt('--port', 18765));
const token = 'e2e-token';
mkdirSync(out, { recursive: true });

const dataDir = mkdtempSync(path.join(tmpdir(), 'aistation-e2e-'));
const isWin = process.platform === 'win32';
const cmd = isWin ? exe : 'mono';
const cmdArgs = [...(isWin ? [] : [exe]), '--headless', '--port', String(port), '--data-dir', dataDir, '--token', token];
if (!isWin) cmdArgs.push('--ui-dir', path.join(repo, 'ui'));
const env = { ...process.env };
if (!REAL) {
  env.AISTATION_WINGET = path.join(repo, 'tests', 'fake-winget.sh');
  env.FAKE_WINGET_FAIL = 'kangfenmao.CherryStudio';
}
const server = spawn(cmd, cmdArgs, { env, stdio: ['ignore', 'pipe', 'pipe'] });
let serverLog = '';
server.stdout.on('data', (d) => { serverLog += d; });
server.stderr.on('data', (d) => { serverLog += d; });

const base = `http://127.0.0.1:${port}`;
const errors = [];
let step = 0;
const results = [];

function check(cond, msg) {
  if (!cond) throw new Error('断言失败：' + msg);
  results.push('✔ ' + msg);
}

async function shot(page, name) {
  step++;
  await page.waitForTimeout(250);
  await page.screenshot({ path: path.join(out, String(step).padStart(2, '0') + '-' + name + '.png') });
}

async function waitServer() {
  for (let i = 0; i < 60; i++) {
    try {
      const r = await fetch(base + '/api/state', { headers: { 'X-AIStation-Token': token } });
      await r.text(); // 必须读完响应体，否则 Node 的 fetch 在连接关闭时会触发内部断言
      if (r.ok) return;
    } catch { /* 还没启动 */ }
    await new Promise((r) => setTimeout(r, 250));
  }
  throw new Error('服务没有启动：\n' + serverLog);
}

async function main() {
  await waitServer();

  // 接口安全：没有令牌或 Host 不对时拒绝
  const noToken = await fetch(base + '/api/state');
  await noToken.text();
  check(noToken.status === 403, '无令牌访问接口返回 403');
  const bad = await fetch(base + '/api/launch', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ id: 'kimi' }) });
  await bad.text();
  check(bad.status === 403, '跨站 POST（无令牌头）被拒绝');

  const browser = await chromium.launch(isWin ? { channel: 'msedge' } : {});
  const ctx = await browser.newContext({ viewport: { width: 1200, height: 820 }, locale: 'zh-CN', colorScheme: 'light' });
  const page = await ctx.newPage();
  page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('pageerror', (e) => errors.push(e.message));

  await page.goto(`${base}/?t=${token}`);
  await page.getByText('把合适的 AI 装进你的电脑').waitFor();
  await page.waitForFunction(() => window.__aistation.state.hardware);
  check(!page.url().includes('t='), '令牌从地址栏移除');
  await shot(page, 'welcome');

  // 问卷
  await page.getByRole('button', { name: /帮我推荐/ }).click();
  await page.getByText('你主要想用 AI 做什么？').waitFor();
  await page.getByRole('button', { name: /读文档/ }).click();
  await page.getByRole('button', { name: /写代码/ }).click();
  check(await page.locator('.opt.on').count() === 2, '问卷第 1 题可以多选');
  await shot(page, 'quiz-needs');
  await page.getByRole('button', { name: /下一步/ }).click();
  await page.getByText('这台电脑的网络环境是？').waitFor();
  await page.waitForFunction(() => window.__aistation.state.network);
  await shot(page, 'quiz-network');
  await page.getByRole('button', { name: /^国内网络/ }).click();
  await page.getByText('你更看重什么？').waitFor();
  await shot(page, 'quiz-priority');
  await page.getByRole('button', { name: /简单省心/ }).click();
  await page.getByText(/为你推荐 \d 款 AI 软件/).waitFor();
  const picks = await page.locator('.rec').count();
  check(picks === 2, '读文档 + 写代码（国内、省心）推荐 2 款，实际 ' + picks);
  check(await page.locator('.rec', { hasText: 'Kimi' }).count() === 1, '推荐中包含 Kimi');
  check(await page.locator('.rec', { hasText: 'TRAE' }).count() === 1, '推荐中包含 TRAE');
  await shot(page, 'result');

  // 安装推荐
  await page.getByRole('button', { name: '安装并完成配置' }).click();
  await page.getByRole('heading', { name: '下载与更新' }).waitFor();
  await page.waitForSelector('.job.running');
  // 真实 winget 的输出被重定向时不打印进度条，界面显示“进行中”动画；模拟 winget 会输出下载进度
  if (!REAL) await page.waitForFunction(() => window.__aistation.state.jobs.some((j) => j.status === 'running' && j.progress > 0));
  await shot(page, 'tasks-running');
  // 真实 winget 下载安装可能需要几分钟
  await page.waitForFunction(() => window.__aistation.state.jobs.length === 2 &&
    window.__aistation.state.jobs.every((j) => j.status !== 'queued' && j.status !== 'running'), null, { timeout: REAL ? 15 * 60000 : 60000 });
  const jobs = await page.evaluate(() => window.__aistation.state.jobs.map((j) => ({ appId: j.appId, status: j.status, message: j.message, exitCode: j.exitCode })));
  console.log('安装结果：' + JSON.stringify(jobs));
  const succeeded = jobs.filter((j) => j.status === 'success').length;
  check(REAL ? succeeded >= 1 : succeeded === 2, '安装任务成功 ' + succeeded + '/2');
  await shot(page, 'tasks-done');

  // 启动台
  await page.getByRole('button', { name: /我的 AI/ }).click();
  await page.waitForSelector('.tile[data-act="launch"]');
  await page.waitForFunction(() => !window.__aistation.state.scanning);
  await page.waitForTimeout(1500);
  const tiles = await page.locator('.tile[data-act="launch"]').count();
  check(tiles >= succeeded, '我的 AI 中出现已安装的软件（' + tiles + ' 个）');
  await shot(page, 'home');

  // 软件库：筛选、详情、批量选择、失败任务
  await page.getByRole('button', { name: /软件库/ }).click();
  await shot(page, 'library');
  await page.getByRole('button', { name: '本地运行' }).click();
  check(await page.locator('.card').count() === 5, '本地运行分类有 5 款');
  await page.getByRole('button', { name: '全部' }).first().click();
  await page.fill('#lib-search', '知识库');
  check(await page.locator('.card').count() >= 2, '按用途关键字搜索');
  await page.fill('#lib-search', '');
  await page.locator('.card', { hasText: 'DeepSeek' }).click();
  await page.locator('.drawer').waitFor();
  await shot(page, 'detail');
  await page.keyboard.press('Escape');
  await page.locator('.card', { hasText: 'Cherry Studio' }).locator('.check').click();
  await page.locator('.card', { hasText: '秘塔' }).locator('.check').click();
  check(await page.getByText('已选 2 款').count() === 1, '批量选择计数');
  await shot(page, 'library-selected');
  if (!REAL) {
    await page.getByRole('button', { name: /安装选中的 2 款/ }).click();
    await page.getByRole('heading', { name: '下载与更新' }).waitFor();
    await page.waitForFunction(() => window.__aistation.state.jobs.some((j) => j.appId === 'cherry' && j.status === 'failed'), null, { timeout: 60000 });
    check(await page.locator('.job.failed').getByRole('button', { name: /重试/ }).count() === 1, '失败任务提供重试');
    await page.locator('.job.failed summary').click();
    await shot(page, 'tasks-failed');
    await page.getByRole('button', { name: /我的 AI/ }).click();
    check(await page.locator('.tile', { hasText: '秘塔' }).count() === 1, '网页版添加到我的 AI');
  }

  await page.getByRole('button', { name: /本地模型/ }).click();
  await page.getByText('这台电脑推荐').waitFor();
  await shot(page, 'models');
  await page.getByRole('button', { name: /设置/ }).click();
  await page.getByText('这台电脑', { exact: true }).waitFor();
  await shot(page, 'settings');

  // 暗色模式 + 窄窗口
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.getByRole('button', { name: /我的 AI/ }).click();
  await shot(page, 'home-dark');
  await page.setViewportSize({ width: 820, height: 760 });
  await page.getByRole('button', { name: /软件库/ }).click();
  await shot(page, 'library-narrow-dark');

  // 重新加载后保持已完成首次配置
  await page.reload();
  await page.getByText(/早上好|中午好|下午好|晚上好|夜深了/).waitFor();
  check(true, '刷新后直接进入我的 AI（配置已保存）');

  check(errors.length === 0, '浏览器控制台无错误' + (errors.length ? '：' + errors.join(' | ') : ''));
  await browser.close();
}

main().then(() => {
  console.log(results.join('\n'));
  console.log(`\nE2E 通过，截图：${out}`);
  server.kill();
  process.exit(0);
}).catch((e) => {
  console.log(results.join('\n'));
  console.error('\nE2E 失败：', e.message);
  console.error('--- 服务日志 ---\n' + serverLog.slice(-4000));
  server.kill();
  process.exit(1);
});
