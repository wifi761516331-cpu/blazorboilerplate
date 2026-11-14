// CDP 验证：/identity/users 的 BlazorBoilerplateModal 弹框 + Toast 通知端到端
// 1) 点击「新建用户」→ .blazorboilerplate-modal-backdrop 出现且居中
// 2) 填表并点「创建」→ 弹框关闭 + .blazorboilerplate-toast.success 出现
// 3) 删除该用户 → 确认弹框出现 → 确认 → toast 出现
// Node 22 内置 WebSocket（global），无第三方依赖。
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
  // 轮询等待 CDP 端点就绪（最多 20s）
  let targets = null;
  for (let i = 0; i < 20; i++) {
    try {
      const res = await fetch(`http://127.0.0.1:${PORT}/json/list`, { signal: AbortSignal.timeout(2000) });
      const list = await res.json();
      const page = list.find(t => t.type === 'page' && t.webSocketDebuggerUrl);
      if (page) { targets = page; break; }
    } catch (e) { console.log('[cdp waiting]', e.message); }
    await sleep(1000);
  }
  if (!targets) throw new Error('CDP 端点未就绪（chrome 启动失败？）');
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
  const r = await send('Runtime.evaluate', {
    expression: expr,
    returnByValue: true,
    awaitPromise: true
  });
  if (r?.exceptionDetails) throw new Error('Eval failed: ' + JSON.stringify(r.exceptionDetails));
  return r?.result?.value;
}

async function clickByText(selector, text) {
  // 通过 JS 找到包含指定文本的元素并点击（Blazor 事件走 DOM click 即可触发）
  return evaluate(`(() => {
    const els = [...document.querySelectorAll('${selector}')];
    const el = els.find(e => e.textContent.trim().includes('${text}') && e.offsetParent !== null);
    if (!el) return false;
    el.click();
    return true;
  })()`);
}

async function main() {
  let chrome = null;
  process.on('exit', () => { try { chrome?.kill(); } catch {} });

  // 若 CDP 端点已就绪（外部已启动 chrome），则不再 spawn
  let ready = false;
  try {
    await fetch(`http://127.0.0.1:${PORT}/json/version`, { signal: AbortSignal.timeout(1500) });
    ready = true;
    console.log('[cdp] reusing already-running chrome on', PORT);
  } catch { /* 未就绪，需要 spawn */ }

  if (!ready) {
    chrome = spawn(CHROME, [
      '--headless=new', `--remote-debugging-port=${PORT}`,
      '--no-first-run', '--no-default-browser-check',
      '--user-data-dir=' + process.env.TEMP + '\\blazorboilerplate-cdp-modal-' + PORT,
      '--window-size=1440,900', 'about:blank'
    ], { stdio: 'ignore' });
    chrome.on('exit', (code) => console.log('[chrome exited]', code));
  }

  await connectPage();
  await send('Page.enable');
  await send('Page.navigate', { url: PAGE_URL });
  await sleep(6000); // 等待 Blazor circuit 与页面渲染

  console.log('== step 0: 页面就绪 ==');
  let hasApp = false;
  for (let i = 0; i < 15; i++) {
    hasApp = await evaluate(`!!document.querySelector('.id-page')`);
    if (hasApp === true) break;
    await sleep(1000);
  }
  console.log('id-page rendered:', hasApp);
  if (hasApp !== true) throw new Error('页面未渲染');

  console.log('== step 1: 点击「新建用户」==');
  const clicked = await clickByText('button', '新建用户');
  console.log('clicked:', clicked);
  await sleep(800);
  const modalInfo = await evaluate(`(() => {
    const bd = document.querySelector('.blazorboilerplate-modal-backdrop');
    if (!bd) return null;
    const modal = document.querySelector('.blazorboilerplate-modal');
    const r = modal.getBoundingClientRect();
    return {
      backdropBg: getComputedStyle(bd).backgroundColor,
      visible: r.width > 0 && r.height > 0,
      centered: Math.abs((r.left + r.width / 2) - innerWidth / 2) < 60,
      title: document.querySelector('.blazorboilerplate-modal-title')?.textContent,
      zIndex: getComputedStyle(bd).zIndex
    };
  })()`);
  console.log('modal:', JSON.stringify(modalInfo));
  if (!modalInfo?.visible || !modalInfo.centered) throw new Error('弹框未正确显示/居中');

  console.log('== step 2: 填表并创建 ==');
  const stamp = Date.now().toString().slice(-6);
  const uname = 'cdp用户' + stamp;
  await evaluate(`(() => {
    const inputs = [...document.querySelectorAll('.blazorboilerplate-modal .bit-text-field input, .blazorboilerplate-modal input.bit-text-field, .blazorboilerplate-modal input')];
    const set = (el, v) => {
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
      setter.call(el, v);
      el.dispatchEvent(new Event('input', { bubbles: true }));
      el.dispatchEvent(new Event('change', { bubbles: true }));
    };
    set(inputs[0], '${uname}');
    set(inputs[1], 'cdp' + '${stamp}' + '@test.local');
    set(inputs[3], 'Test123456');
    return inputs.length;
  })()`);
  await sleep(400);
  const okClicked = await clickByText('.blazorboilerplate-modal .blazorboilerplate-btn', '创建');
  console.log('ok clicked:', okClicked);
  await sleep(1500);

  const afterCreate = await evaluate(`(() => {
    const toasts = [...document.querySelectorAll('.blazorboilerplate-toast')].map(t => t.textContent.trim());
    return {
      modalClosed: !document.querySelector('.blazorboilerplate-modal-backdrop'),
      toasts,
      created: document.body.textContent.includes('${uname}')
    };
  })()`);
  console.log('afterCreate:', JSON.stringify(afterCreate, null, 1));
  if (!afterCreate.modalClosed) throw new Error('创建后弹框未关闭');
  if (!afterCreate.toasts.some(t => t.includes('已创建'))) throw new Error('创建成功 toast 未出现');
  if (!afterCreate.created) throw new Error('用户未出现在列表中');

  console.log('== step 3: 删除该用户（危险确认框）==');
  // BitNav 列表项是 li > button，直接点含用户名的按钮
  const navClicked = await evaluate(`(() => {
    const items = [...document.querySelectorAll('.bit-nav button')];
    const el = items.find(e => e.textContent.includes('${uname}'));
    if (!el) return false;
    el.click();
    return true;
  })()`);
  console.log('nav item clicked:', navClicked);
  await sleep(1200);
  const delClicked = await clickByText('button', '删除');
  console.log('delete clicked:', delClicked);
  await sleep(800);
  const confirmInfo = await evaluate(`(() => {
    const t = document.querySelector('.blazorboilerplate-modal-title');
    const btn = [...document.querySelectorAll('.blazorboilerplate-modal .blazorboilerplate-btn.danger')];
    return { title: t?.textContent, hasDanger: btn.length > 0,
             msg: document.querySelector('.blazorboilerplate-modal-message')?.textContent?.slice(0, 40) };
  })()`);
  console.log('confirm dialog:', JSON.stringify(confirmInfo));
  if (!confirmInfo?.hasDanger) throw new Error('删除确认弹框未出现');

  await clickByText('.blazorboilerplate-modal .blazorboilerplate-btn.danger', '删除');
  await sleep(1500);
  const afterDelete = await evaluate(`(() => {
    const toasts = [...document.querySelectorAll('.blazorboilerplate-toast')].map(t => t.textContent.trim());
    return { modalClosed: !document.querySelector('.blazorboilerplate-modal-backdrop'), toasts };
  })()`);
  console.log('afterDelete:', JSON.stringify(afterDelete, null, 1));
  if (!afterDelete.modalClosed) throw new Error('删除后弹框未关闭');
  if (!afterDelete.toasts.some(t => t.includes('已删除'))) throw new Error('删除成功 toast 未出现');

  console.log('\nALL PASSED ✅  弹框渲染正常 + 操作通知正常');
  process.exit(0);
}

main().catch(e => { console.error('FAILED ❌', e.message); process.exit(1); });
