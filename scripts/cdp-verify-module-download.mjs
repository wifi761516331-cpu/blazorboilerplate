// CDP 验证：/modules 卡片显示中文名 + 下载按钮触发 .zip 下载
import { spawn } from 'node:child_process';
import fs from 'node:fs';

const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 9224;
const DL_DIR = process.env.TEMP + '\\blazorboilerplate-dltest';
fs.mkdirSync(DL_DIR, { recursive: true });

const sleep = ms => new Promise(r => setTimeout(r, ms));
let msgId = 0;
const pending = new Map();
let ws;

function send(method, params = {}, sessionId) {
  const id = ++msgId;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    ws.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
  });
}

async function connectPage() {
  let targets = null;
  for (let i = 0; i < 20; i++) {
    try {
      const res = await fetch(`http://127.0.0.1:${PORT}/json/list`, { signal: AbortSignal.timeout(2000) });
      const list = await res.json();
      const page = list.find(t => t.type === 'page' && t.webSocketDebuggerUrl);
      if (page) { targets = page; break; }
    } catch { /* waiting */ }
    await sleep(1000);
  }
  if (!targets) throw new Error('CDP 端点未就绪');
  return new Promise((resolve, reject) => {
    ws = new WebSocket(targets.webSocketDebuggerUrl);
    ws.onopen = () => resolve(targets);
    ws.onerror = (e) => reject(new Error(e.message || 'ws error'));
    ws.onmessage = (ev) => {
      const msg = JSON.parse(ev.data);
      if (msg.id && pending.has(msg.id)) {
        const { resolve, reject } = pending.get(msg.id);
        pending.delete(msg.id);
        msg.error ? reject(new Error(msg.error.message)) : resolve(msg.result);
      }
    };
  });
}

async function evaluate(expr) {
  const r = await send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
  if (r?.exceptionDetails) throw new Error('Eval failed: ' + JSON.stringify(r.exceptionDetails));
  return r?.result?.value;
}

async function main() {
  let chrome = null;
  process.on('exit', () => { try { chrome?.kill(); } catch {} });

  let ready = false;
  try {
    await fetch(`http://127.0.0.1:${PORT}/json/version`, { signal: AbortSignal.timeout(1500) });
    ready = true;
    console.log('[cdp] reusing running chrome');
  } catch { /* spawn */ }
  if (!ready) {
    chrome = spawn(CHROME, [
      '--headless=new', `--remote-debugging-port=${PORT}`,
      '--no-first-run', '--no-default-browser-check',
      `--user-data-dir=${process.env.TEMP}\\blazorboilerplate-cdp-dl-${PORT}`,
      `--window-size=1440,900`, 'about:blank'
    ], { stdio: 'ignore' });
  }
  await connectPage();
  await send('Page.enable');
  // 允许 headless 下载落盘
  try {
    await send('Browser.setDownloadBehavior', { behavior: 'allow', downloadPath: DL_DIR, eventsEnabled: true });
  } catch (e) { console.log('[warn] setDownloadBehavior:', e.message); }

  await send('Page.navigate', { url: 'http://localhost:5200/modules' });
  await sleep(6000);
  for (let i = 0; i < 15; i++) {
    if (await evaluate(`!!document.querySelector('.module-grid')`) === true) break;
    await sleep(1000);
  }

  // 1) 卡片标题中文名
  const titles = await evaluate(`[...document.querySelectorAll('.module-card-title')].map(e => e.textContent.trim())`);
  const ids = await evaluate(`[...document.querySelectorAll('.module-card-id')].map(e => e.textContent.trim())`);
  console.log('card titles:', JSON.stringify(titles), 'ids:', JSON.stringify(ids));
  if (!titles.some(t => t === '身份认证') || !titles.some(t => t === '商品目录'))
    throw new Error('卡片未显示中文名称: ' + JSON.stringify(titles));

  // 2) blazorDownloadFile 函数存在
  const hasFn = await evaluate(`typeof window.blazorDownloadFile === 'function'`);
  console.log('blazorDownloadFile exists:', hasFn);
  if (hasFn !== true) throw new Error('JS 下载助手未注册');

  // 3) 点击 catalog 卡片的下载按钮（图标按钮，按 aria-label 定位）→ 等待下载文件落盘
  fs.readdirSync(DL_DIR).forEach(f => fs.unlinkSync(DL_DIR + '\\' + f));
  const clicked = await evaluate(`(() => {
    const cards = [...document.querySelectorAll('.module-card')];
    const card = cards.find(c => c.querySelector('.module-card-title')?.textContent.trim() === '商品目录');
    const btn = [...card.querySelectorAll('button')].find(b =>
      b.getAttribute('aria-label') === '下载模块包' || b.title === '下载模块包 (.zip)');
    if (!btn) return false; btn.click(); return true;
  })()`);
  console.log('download clicked:', clicked);
  let downloaded = null;
  for (let i = 0; i < 20; i++) {
    await sleep(1000);
    const files = fs.readdirSync(DL_DIR).filter(f => f.endsWith('.zip') || f.endsWith('.crdownload'));
    if (files.some(f => f.endsWith('.zip'))) { downloaded = files.find(f => f.endsWith('.zip')); break; }
  }
  console.log('downloaded file:', downloaded);
  if (!downloaded) throw new Error('浏览器未收到 .zip 下载文件');

  const size = fs.statSync(DL_DIR + '\\' + downloaded).size;
  console.log('downloaded size:', size);
  if (size < 1000) throw new Error('下载文件过小，疑似空包');

  // 截图
  const shot = await send('Page.captureScreenshot', { format: 'png' });
  fs.writeFileSync('scripts/modules-cn-download.png', Buffer.from(shot.data, 'base64'));

  console.log('\nALL PASSED ✅  中文名显示 + 下载按钮产出 .zip');
  process.exit(0);
}

main().catch(e => { console.error('FAILED ❌', e.message); process.exit(1); });
