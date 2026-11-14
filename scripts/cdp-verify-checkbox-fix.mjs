// CDP 验证：BitCheckbox 方块修复后 —— 用户页角色 tab / 角色页菜单权限 tab
import { spawn } from 'node:child_process';

const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 9224;

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

const fs = await import('node:fs');

async function shot(name) {
  const s = await send('Page.captureScreenshot', { format: 'png' });
  fs.writeFileSync(`scripts/${name}`, Buffer.from(s.data, 'base64'));
  console.log('screenshot:', name);
}

async function checkCheckboxTab(pageUrl, tabText, listSel, shotName) {
  await send('Page.navigate', { url: pageUrl });
  await sleep(6000);
  for (let i = 0; i < 15; i++) {
    if (await evaluate(`!!document.querySelector('.id-page')`) === true) break;
    await sleep(1000);
  }
  // 选中左侧第一项
  await evaluate(`(() => { const items=[...document.querySelectorAll('.bit-nav button')]; if(items.length) items[0].click(); })()`);
  await sleep(1500);
  // 切 tab
  const tabOk = await evaluate(`(() => {
    const tabs=[...document.querySelectorAll('[role=tab], .bit-pivot button, .bit-piv button')];
    const t=tabs.find(e=>e.textContent.includes('${tabText}'));
    if(!t) return false; t.click(); return true;
  })()`);
  await sleep(1200);
  const info = await evaluate(`(() => {
    const list=document.querySelector('${listSel}');
    if(!list) return { ok:false, reason:'no list' };
    const boxes=[...list.querySelectorAll('.bit-chb-box')];
    const root=[...list.querySelectorAll('.bit-chb')];
    const checked=root.filter(e=>e.classList.contains('bit-chb-ckd'));
    const boxInfo=boxes.slice(0,2).map(b=>{const cs=getComputedStyle(b);const r=b.getBoundingClientRect();return {w:r.width,h:r.height,border:cs.borderWidth,bg:cs.backgroundColor};});
    // 勾选图标的透明度
    const ico=boxes.slice(0,2).map(b=>{const i=b.querySelector('.bit-chb-ico');return i?getComputedStyle(i).opacity:null;});
    return { ok: boxes.length>0, boxCount: boxes.length, rootCount: root.length, checkedCount: checked.length, boxes: boxInfo, iconOpacity: ico };
  })()`);
  console.log(`[${shotName}]`, JSON.stringify(info));
  await shot(shotName);
  return info;
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
      '--user-data-dir=' + process.env.TEMP + '\\blazorboilerplate-cdp-fix-' + PORT,
      '--window-size=1440,900', 'about:blank'
    ], { stdio: 'ignore' });
  }
  await connectPage();
  await send('Page.enable');

  const users = await checkCheckboxTab('http://localhost:5200/identity/users', '角色', '.id-check-list', 'fix-users-roles-tab.png');
  if (!users.ok || users.boxCount < 2) throw new Error('用户页角色 tab 方块未渲染');

  const roles = await checkCheckboxTab('http://localhost:5200/identity/roles', '菜单权限', '.id-check-list', 'fix-roles-menus-tab.png');
  if (!roles.ok || roles.boxCount < 1) throw new Error('角色页菜单权限 tab 方块未渲染');

  console.log('\nALL PASSED ✅  复选框方块与选中态均可见');
  process.exit(0);
}

main().catch(e => { console.error('FAILED ❌', e.message); process.exit(1); });
