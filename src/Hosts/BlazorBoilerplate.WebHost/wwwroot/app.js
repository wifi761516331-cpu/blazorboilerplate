// BlazorBoilerplate WebHost 自定义脚本
// 通过 JS interop 把服务端 Stream 触发为浏览器下载（Blazor Server 电路内文件无法直接 a.href 指向）。
window.blazorDownloadFile = async (fileName, contentType, contentStreamReference) => {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer], { type: contentType });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
};

// ---------------------------------------------------------------------------
// 认证：浏览器只持有 HttpOnly Cookie，所有 JWT 留在服务端加密票据中。
// JS 只负责调用同源 /auth/* 端点并拿到 {ok,error} / 账号列表（不含任何令牌）。
// ---------------------------------------------------------------------------
(() => {
    const PREFS_KEY = 'bb_prefs';
    const CULTURE_COOKIE = 'bb_culture';

    function writeCookie(name, value, days) {
        const expires = new Date(Date.now() + days * 86400000).toUTCString();
        document.cookie = `${name}=${encodeURIComponent(value)}; path=/; expires=${expires}; SameSite=Lax`;
    }

    function readPrefs() {
        try {
            const raw = localStorage.getItem(PREFS_KEY);
            return raw ? JSON.parse(raw) : null;
        } catch {
            return null;
        }
    }

    function applyPrefs(theme, accentColor, culture) {
        const root = document.documentElement;
        if (theme) root.dataset.bbTheme = String(theme) === 'dark' ? 'dark' : 'light';
        if (accentColor) root.style.setProperty('--bb-accent', accentColor);
        else root.style.removeProperty('--bb-accent');
        if (culture) {
            root.lang = culture;
            writeCookie(CULTURE_COOKIE, culture, 365); // SSR 首屏据此渲染语言
        }
    }

    window.blazorPrefs = {
        get: () => readPrefs(),
        set: (payload) => {
            try { localStorage.setItem(PREFS_KEY, JSON.stringify(payload || {})); } catch { /* 隐私模式 */ }
            applyPrefs(payload && payload.theme, payload && payload.accentColor, payload && payload.culture);
        },
        apply: (theme, accentColor, culture) => applyPrefs(theme, accentColor, culture)
    };

    // 首屏（Blazor 接管前）立即应用本地主题，避免明暗闪烁。
    const initial = readPrefs();
    if (initial) applyPrefs(initial.theme, initial.accentColor, initial.culture);

    async function postJson(url, body) {
        try {
            const response = await fetch(url, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body || {})
            });
            const text = await response.text();
            let data = {};
            try { data = text ? JSON.parse(text) : {}; } catch { data = { error: text }; }
            if (data.ok === true) return { ok: true };
            return { ok: false, error: data.error || `HTTP ${response.status}` };
        } catch (err) {
            return { ok: false, error: (err && err.message) || '网络请求失败' };
        }
    }

    window.blazorAuth = {
        login: (payload) => postJson('/auth/login', payload),
        register: (payload) => postJson('/auth/register', payload),
        refresh: () => postJson('/auth/refresh', {}),
        logout: (all) => postJson('/auth/logout', { all: !!all }),
        switchAccount: (accountId) => postJson('/auth/switch-account', { accountId }),
        switchTenant: (tenantId) => postJson('/auth/switch-tenant', { tenantId }),
        getAccounts: async () => {
            try {
                const response = await fetch('/auth/accounts', { credentials: 'same-origin' });
                if (!response.ok) return [];
                const data = await response.json();
                return Array.isArray(data) ? data : [];
            } catch {
                return [];
            }
        },
        redirect: (url) => {
            const target = url || '/';
            if (window.location.pathname + window.location.search === target) {
                window.location.reload();
            } else {
                window.location.assign(target);
            }
        }
    };
})();
