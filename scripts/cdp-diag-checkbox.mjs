// CDP 诊断：检查 /identity/users 角色 tab 中 BitCheckbox 实际渲染的 DOM 与样式
import { spawn } from 'node:child_process';

const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 9224;
const PAGE_URL = 'http://localhost:5200/identity/users';

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
    console.log('[cdp] reusing running chrome on', PORT);
  } catch { /* spawn */ }
  if (!ready) {
    chrome = spawn(CHROME, [
      '--headless=new', `--remote-debugging-port=${PORT}`,
      '--no-first-run', '--no-default-browser-check',
      '--user-data-dir=' + process.env.TEMP + '\\blazorboilerplate-cdp-diag-' + PORT,
      '--window-size=1440,900', 'about:blank'
    ], { stdio: 'ignore' });
  }

  await connectPage();
  await send('Page.enable');
  await send('Page.navigate', { url: PAGE_URL });
  await sleep(6000);

  for (let i = 0; i < 15; i++) {
    if (await evaluate(`!!document.querySelector('.id-page')`) === true) break;
    await sleep(1000);
  }

  // 登录状态检查
  const bodyTxt = await evaluate(`document.body.textContent.slice(0, 300)`);
  console.log('page head text:', JSON.stringify(bodyTxt?.slice(0, 120)));

  // 点第一个用户
  const navClicked = await evaluate(`(() => {
    const items = [...document.querySelectorAll('.bit-nav button')];
    if (!items.length) return false;
    items[0].click(); return true;
  })()`);
  console.log('nav clicked:', navClicked);
  await sleep(1500);

  // 切到 角色 tab
  const tabClicked = await evaluate(`(() => {
    const tabs = [...document.querySelectorAll('.bit-pivot button, .bit-piv button, [role=tab]')];
    const t = tabs.find(e => e.textContent.includes('角色'));
    if (!t) return false;
    t.click(); return true;
  })()`);
  console.log('role tab clicked:', tabClicked);
  await sleep(1200);

  // 检查 checkbox DOM
  const info = await evaluate(`(() => {
    const cbs = [...document.querySelectorAll('.id-check-list .bit-checkbox, .id-check-list .bit-cb, .id-check-list input[type=checkbox], .id-check-list *')].filter(e => e.className && String(e.className).includes && String(e.className).toLowerCase().includes('checkbox'));
    const list = document.querySelector('.id-check-list');
    if (!list) return { listExists: false };
    const first = list.firstElementChild;
    const outer = first ? first.outerHTML.slice(0, 1500) : null;
    // 找 input
    const inputs = [...list.querySelectorAll('input')].map(i => {
      const cs = getComputedStyle(i);
      const r = i.getBoundingClientRect();
      return { type: i.type, cls: i.className, w: r.width, h: r.height, display: cs.display, visibility: cs.visibility, opacity: cs.opacity, appearance: cs.appearance, position: cs.position, checked: i.checked };
    });
    // 找可能的视觉 box span
    const spans = [...list.querySelectorAll('span, label, div')].slice(0, 10).map(e => ({ tag: e.tagName, cls: String(e.className), text: e.textContent.trim().slice(0, 20) }));
    return { listExists: true, firstOuterHTML: outer, inputs, spans, cbCount: cbs.length };
  })()`);
  console.log(JSON.stringify(info, null, 1));

  // 截图存证
  const shot = await send('Page.captureScreenshot', { format: 'png' });
  const fs = await import('node:fs');
  fs.writeFileSync('scripts/roles-tab-diag.png', Buffer.from(shot.data, 'base64'));
  console.log('screenshot saved: scripts/roles-tab-diag.png');
  process.exit(0);
}

main().catch(e => { console.error('FAILED ❌', e.message); process.exit(1); });
