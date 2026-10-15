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
let sizeServer = null;
if (!REAL) {
  env.AISTATION_WINGET = path.join(repo, 'tests', 'fake-winget.sh');
  env.AISTATION_OLLAMA = path.join(repo, 'tests', 'fake-ollama.sh');
  env.AISTATION_NETWORK = 'cn';
  env.TMPDIR = mkdtempSync(path.join(tmpdir(), 'aistation-tmp-'));
  env.FAKE_WINGET_FAIL = 'kangfenmao.CherryStudio';
  // 模拟安装包所在的 CDN：只需要正确返回文件大小
  const http = await import('node:http');
  sizeServer = http.createServer((req, res) => {
    res.writeHead(200, { 'Content-Length': String(40 * 1024 * 1024), 'Content-Type': 'application/octet-stream' });
    res.end();
  });
  await new Promise((r) => sizeServer.listen(port + 1, '127.0.0.1', r));
  env.FAKE_INSTALLER_BASE = `http://127.0.0.1:${port + 1}`;
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

function cleanupFakeOllama() {
  if (REAL || isWin) return;
  try { require('node:child_process').execSync('pkill -f "http.server 11434"'); } catch { /* 没有在运行 */ }
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
  await page.getByRole('dialog', { name: '使用须知' }).waitFor();
  check(await page.getByText('不提供、不内置、也不推荐任何代理').count() === 1, '首次打开显示使用须知（网络免责）');
  await shot(page, 'disclaimer');
  await page.getByRole('button', { name: '同意并继续' }).click();
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
  await page.locator('.opt', { hasText: '国内网络' }).click();
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
  // 下载进度来自对 winget 下载文件的实际测量（真实与模拟环境都应能看到）
  await page.waitForFunction(() => window.__aistation.state.jobs.some((j) => j.status === 'running' && j.phase === 'download' && j.downloaded > 0), null, { timeout: REAL ? 120000 : 30000 });
  await page.waitForFunction(() => window.__aistation.state.jobs.some((j) => j.phase === 'download' && j.progress > 5 && j.total > 0), null, { timeout: REAL ? 120000 : 30000 });
  check(true, '显示真实下载进度（已下载 / 总大小）');
  await shot(page, 'tasks-downloading');
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
  check(await page.locator('.card', { hasText: '豆包' }).getByText('免费 · 手机号登录').count() === 1, '软件卡片标注费用和账号要求');
  await shot(page, 'library-selected');
  if (!REAL) {
    await page.getByRole('button', { name: /安装选中的 2 款/ }).click();
    await page.getByRole('dialog', { name: '安装前请留意' }).waitFor();
    check(await page.getByText('需要自备模型服务的 API Key').count() === 1, '安装前提醒（Cherry Studio 需要 API Key）');
    await shot(page, 'precheck');
    await page.getByRole('button', { name: '知道了，继续安装' }).click();
    await page.getByRole('heading', { name: '下载与更新' }).waitFor();
    await page.waitForFunction(() => window.__aistation.state.jobs.some((j) => j.appId === 'cherry' && j.status === 'failed'), null, { timeout: 60000 });
    check(await page.locator('.job.failed').getByRole('button', { name: /重试/ }).count() === 1, '失败任务提供重试');
    check(await page.locator('.job.failed .help').count() === 1, '失败任务显示自助说明');
    await page.locator('.job.failed').getByRole('button', { name: /导出问题报告/ }).click();
    await page.getByText(/已保存到桌面：AIStation-问题报告-/).waitFor();
    check(true, '一键导出问题报告');
    await page.locator('.job.failed summary').click();
    await shot(page, 'tasks-failed');
    await page.getByRole('button', { name: /我的 AI/ }).click();
    check(await page.locator('.tile', { hasText: '秘塔' }).count() === 1, '网页版添加到我的 AI');
  }

  await page.getByRole('button', { name: /本地模型/ }).click();
  await page.getByText('这台电脑推荐').waitFor();
  await shot(page, 'models');
  if (!REAL) {
    await page.getByRole('button', { name: /一键准备：安装 Ollama \+ 下载/ }).click();
    await page.waitForFunction(() => {
      const js = window.__aistation.state.jobs.filter((j) => j.appId === 'ollama');
      return js.length === 2 && js.every((j) => j.status === 'success');
    }, null, { timeout: 60000 });
    check(true, '一键准备本地 AI：安装 Ollama 并下载推荐模型');
    await page.getByRole('button', { name: /本地模型/ }).click();
    await page.getByRole('button', { name: /开始对话/ }).waitFor({ timeout: 15000 });
    check(true, '模型下载后显示“开始对话”');
    await shot(page, 'models-ready');
  }
  await page.getByRole('button', { name: /设置/ }).click();
  await page.getByText('这台电脑', { exact: true }).waitFor();
  check(await page.getByText('绿色软件说明').count() === 1 && await page.getByRole('button', { name: /清除数据并退出/ }).count() === 1, '设置页有绿色软件说明和清除数据');
  check(await page.getByText('安装到本机').count() === 0, '不再提示安装到本机');
  await shot(page, 'settings');

  // 暗色模式 + 窄窗口
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.getByRole('button', { name: /我的 AI/ }).click();
  await shot(page, 'home-dark');
  await page.setViewportSize({ width: 820, height: 760 });
  await page.getByRole('button', { name: /软件库/ }).click();
  await shot(page, 'library-narrow-dark');

  // 重新加载后保持已完成首次配置，且不再弹出使用须知
  await page.reload();
  await page.getByText(/早上好|中午好|下午好|晚上好|夜深了/).waitFor();
  check(await page.getByRole('dialog').count() === 0, '刷新后直接进入我的 AI（配置与须知确认已保存）');

  // 卸载：应用内确认框，说明会自动关闭软件
  await page.emulateMedia({ colorScheme: 'light' });
  await page.setViewportSize({ width: 1200, height: 820 });
  await page.locator('.tile', { hasText: 'Kimi' }).locator('.more').click();
  await page.locator('.menu').getByRole('button', { name: /卸载/ }).click();
  await page.getByRole('dialog', { name: /卸载 Kimi/ }).waitFor();
  check(await page.getByText('包括它在托盘或后台运行的进程').count() === 1, '卸载确认框说明会关闭后台进程');
  await shot(page, 'uninstall-confirm');
  if (!REAL) {
    await page.getByRole('button', { name: '关闭并卸载' }).click();
    await page.waitForFunction(() => window.__aistation.state.jobs.some((j) => j.action === 'uninstall' && j.status === 'success'), null, { timeout: 30000 });
    check(true, '卸载任务完成');
  } else {
    await page.getByRole('button', { name: '取消' }).click();
  }

  check(errors.length === 0, '浏览器控制台无错误' + (errors.length ? '：' + errors.join(' | ') : ''));
  await browser.close();
}

main().then(() => {
  console.log(results.join('\n'));
  console.log(`\nE2E 通过，截图：${out}`);
  server.kill();
  if (sizeServer) sizeServer.close();
  cleanupFakeOllama();
  process.exit(0);
}).catch((e) => {
  cleanupFakeOllama();
  console.log(results.join('\n'));
  console.error('\nE2E 失败：', e.message);
  console.error('--- 服务日志 ---\n' + serverLog.slice(-4000));
  server.kill();
  process.exit(1);
});
