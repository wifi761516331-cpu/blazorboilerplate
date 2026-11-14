// CDP 验证：卡片版本下拉框出现（catalog 2 个版本）→ 选 v1.0.1 → 切换成功
import { spawn } from 'node:child_process';
import fs from 'node:fs';

const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 9224;

const sleep = ms => new Promise(r => setTimeout(r, ms));
let msgId = 0;
const pending = new Map();
let ws;

function send(method, params = {}) {
  const id = ++msgId;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    ws.send(JSON.stringify({ id, method, params }));
  });
}

async function connectPage() {
  let t = null;
  for (let i = 0; i < 20; i++) {
    try {
      const res = await fetch(`http://127.0.0.1:${PORT}/json/list`, { signal: AbortSignal.timeout(2000) });
      const list = await res.json();
      t = list.find(x => x.type === 'page' && x.webSocketDebuggerUrl);
      if (t) break;
    } catch { /* waiting */ }
    await sleep(1000);
  }
  if (!t) throw new Error('CDP 未就绪');
  return new Promise((resolve, reject) => {
    ws = new WebSocket(t.webSocketDebuggerUrl);
    ws.onopen = () => resolve(t);
    ws.onerror = (e) => reject(new Error(e.message || 'ws error'));
    ws.onmessage = ev => {
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
  if (r?.exceptionDetails) throw new Error('Eval: ' + JSON.stringify(r.exceptionDetails).slice(0, 400));
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
    chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${PORT}`, '--no-first-run',
      `--user-data-dir=${process.env.TEMP}\\blazorboilerplate-cdp-ver-${PORT}`, '--window-size=1440,900', 'about:blank'], { stdio: 'ignore' });
  }
  await connectPage();
  await send('Page.enable');
  await send('Page.navigate', { url: 'http://localhost:5200/modules' });
  await sleep(7000);
  for (let i = 0; i < 15; i++) {
    if (await evaluate(`!!document.querySelector('.module-grid')`) === true) break;
    await sleep(1000);
  }

  // 1) catalog 卡片应有版本下拉（BitDropdown），identity 保持徽章
  const headInfo = await evaluate(`(() => {
    const cardOf = t => [...document.querySelectorAll('.module-card')].find(c => c.querySelector('.module-card-title')?.textContent.trim() === t);
    const cat = cardOf('商品目录');
    const idn = cardOf('身份认证');
    return {
      catalogHasDropdown: !!cat?.querySelector('.module-version-select'),
      identityHasBadge: !!idn?.querySelector('.module-version-badge'),
      catalogDropdownText: cat?.querySelector('.module-version-select')?.textContent.trim()
    };
  })()`);
  console.log('head:', JSON.stringify(headInfo));
  if (!headInfo.catalogHasDropdown) throw new Error('catalog 卡片未出现版本下拉');

  // 2) 展开下拉并选择 v1.0.1
  await evaluate(`(() => {
    const card = [...document.querySelectorAll('.module-card')].find(c => c.querySelector('.module-card-title')?.textContent.trim() === '商品目录');
    const lbl = card.querySelector('.module-version-select .bit-ddl-lbl') || card.querySelector('.module-version-select');
    lbl.click();
  })()`);
  await sleep(1200);
  const picked = await evaluate(`(() => {
    const items = [...document.querySelectorAll('.bit-ddl-itm, [role=option], .bit-ddl-items button')];
    const target = items.find(e => e.textContent.includes('v1.0.1'));
    if (!target) return { found: false, options: items.map(e => e.textContent.trim()) };
    target.click();
    return { found: true };
  })()`);
  console.log('picked v1.0.1:', JSON.stringify(picked));
  if (!picked.found) throw new Error('下拉选项中未找到 v1.0.1: ' + JSON.stringify(picked.options));

  // 3) 等待切换完成 → 卡片徽章/下拉值 + toast
  let after = null;
  for (let i = 0; i < 20; i++) {
    await sleep(1500);
    after = await evaluate(`(() => {
      const card = [...document.querySelectorAll('.module-card')].find(c => c.querySelector('.module-card-title')?.textContent.trim() === '商品目录');
      const toasts = [...document.querySelectorAll('.blazorboilerplate-toast')].map(t => t.textContent.trim());
      const badge = card.querySelector('.module-version-badge')?.textContent.trim();
      const dropVal = card.querySelector('.module-version-select input')?.value;
      return { badge, dropVal, toasts };
    })()`);
    if (after.toasts.some(t => t.includes('已切换'))) break;
  }
  console.log('after switch:', JSON.stringify(after, null, 1));
  const ok = after.toasts.some(t => t.includes('已切换')) &&
    (after.badge === 'v1.0.1' || after.dropVal === 'v1.0.1');
  if (!ok) throw new Error('版本切换验证失败: ' + JSON.stringify(after));

  // 截图
  const shot = await send('Page.captureScreenshot', { format: 'png' });
  fs.writeFileSync('scripts/modules-version-dropdown.png', Buffer.from(shot.data, 'base64'));
  console.log('\nALL PASSED ✅  版本下拉切换生效');
  process.exit(0);
}

main().catch(e => { console.error('FAILED ❌', e.message); process.exit(1); });
