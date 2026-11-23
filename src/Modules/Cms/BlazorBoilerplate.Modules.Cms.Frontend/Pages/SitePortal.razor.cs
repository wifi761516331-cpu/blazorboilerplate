using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using BlazorBoilerplate.Modules.Cms.Frontend.Services;
using BlazorBoilerplate.Web.Shared.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Pages;

public partial class SitePortal
{
    [Inject]
    private CmsApiClient CmsApi { get; set; } = default!;

    [Inject]
    private HttpClient Http { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private FrontendLocalizationService FrontendLoc { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "siteKey")]
    public string? SiteKey { get; set; }

    [SupplyParameterFromQuery(Name = "culture")]
    public string? Culture { get; set; }

    private CmsPublishedSnapshot? _snapshot;
    private bool _loading = true;
    private string _activeSlug = "home";
    private string? _activeCulture;

    private string StylePreset =>
        string.IsNullOrWhiteSpace(_snapshot?.Theme.StylePreset) ? "classic" : _snapshot!.Theme.StylePreset;

    private string ThemeVariablesStyle
    {
        get
        {
            if (_snapshot is null) return string.Empty;
            var t = _snapshot.Theme;
            return $"--cms-pri:{t.PrimaryColor};--cms-acc:{t.AccentColor};--cms-bg:{t.BackgroundColor};--cms-surf:{t.SurfaceColor};--cms-fg:{t.TextColor};--cms-font:{t.FontFamily};--cms-radius:{t.BorderRadius};";
        }
    }

    private IJSObjectReference? _scrollJs;

    private string? _resolvedSiteKey;
    private bool _mobileMenuOpen;

    private void ToggleMobileMenu() => _mobileMenuOpen = !_mobileMenuOpen;

    protected override async Task OnInitializedAsync()
    {
        if (!string.IsNullOrWhiteSpace(Culture))
            await FrontendLoc.SetCultureAsync(Culture);
        else
            await FrontendLoc.EnsureLoadedAsync();
        _activeCulture = Culture;
        _resolvedSiteKey = SiteKey;
        if (string.IsNullOrWhiteSpace(_resolvedSiteKey) && DomainRouteResolverRegistry.Current is { } resolver)
        {
            try
            {
                var match = await resolver.ResolveAsync(Http, new Uri(Navigation.BaseUri).Host);
                if (!string.IsNullOrWhiteSpace(match?.SiteKey))
                    _resolvedSiteKey = match.SiteKey;
            }
            catch
            {
                // 域名解析失败时回退默认站点
            }
        }
        await LoadSiteAsync();
    }

    private async Task LoadSiteAsync()
    {
        try
        {
            _snapshot = await CmsApi.GetPublicSiteAsync(_resolvedSiteKey, _activeCulture);
            if (_snapshot is { Sections.Count: > 0 })
                _activeSlug = _snapshot.Sections[0].AnchorSlug;
            if (_snapshot is not null)
            {
                if (!string.Equals(FrontendLoc.CurrentCulture, _snapshot.Culture, StringComparison.OrdinalIgnoreCase))
                    await FrontendLoc.SetCultureAsync(_snapshot.Culture);
                if (!string.IsNullOrWhiteSpace(_snapshot.FaviconUrl))
                    await ApplyFaviconAsync(_snapshot.FaviconUrl);
            }
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>单页内无刷新切换官网语言，不跳转页面。</summary>
    private async Task SwitchCultureAsync(string culture)
    {
        _activeCulture = culture;
        await FrontendLoc.SetCultureAsync(culture);
        await LoadSiteAsync();
    }

    private async Task ApplyFaviconAsync(string faviconUrl)
    {
        try
        {
            var escaped = System.Text.Json.JsonSerializer.Serialize(faviconUrl);
            await JS.InvokeVoidAsync("eval",
                $"(() => {{ let l = document.querySelector(\"link[rel~='icon']\"); if (!l) {{ l = document.createElement('link'); l.rel = 'icon'; document.head.appendChild(l); }} l.href = {escaped}; }})()");
        }
        catch
        {
            // 预渲染阶段忽略 JS 调用
        }
    }

    /// <summary>单页内平滑滚动定位，不发生路由跳转。</summary>
    private async Task NavigateAnchorAsync(string rawTarget)
    {
        if (string.IsNullOrWhiteSpace(rawTarget))
            return;

        if (rawTarget.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            rawTarget.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            await JS.InvokeVoidAsync("open", rawTarget, "_blank", "noopener,noreferrer");
            return;
        }

        var slug = rawTarget.TrimStart('#').Trim().ToLowerInvariant();
        _activeSlug = slug;
        _mobileMenuOpen = false;

        try
        {
            await JS.InvokeVoidAsync("eval",
                $"(() => {{ const el = document.getElementById('sec-{slug}'); if (el) el.scrollIntoView({{ behavior: 'smooth', block: 'start' }}); if (history.replaceState) history.replaceState(null, '', window.location.pathname + window.location.search + '#{slug}'); }})()");
        }
        catch
        {
            // 预渲染阶段忽略 JS 调用
        }
    }

    private async Task ScrollToTopAsync()
    {
        _mobileMenuOpen = false;
        if (_snapshot is { Sections.Count: > 0 })
            _activeSlug = _snapshot.Sections[0].AnchorSlug;

        try
        {
            await JS.InvokeVoidAsync("eval",
                "(() => { window.scrollTo({ top: 0, behavior: 'smooth' }); const shell = document.querySelector('.cms-portal-shell'); if (shell) shell.scrollTo({ top: 0, behavior: 'smooth' }); })()");
        }
        catch
        {
            // 预渲染阶段忽略 JS 调用
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        const string js = """
            let handler = null;
            export function attachScrollState() {
                if (handler) return;
                handler = () => {
                    const root = document.querySelector('.cms-site-root');
                    if (!root) return;
                    const shell = document.querySelector('.cms-portal-shell');
                    const y = window.scrollY || (shell ? shell.scrollTop : 0) || 0;
                    root.classList.toggle('is-scrolled', y > 40);
                };
                window.addEventListener('scroll', handler, { passive: true });
                const shell = document.querySelector('.cms-portal-shell');
                if (shell) shell.addEventListener('scroll', handler, { passive: true });
                handler();
            }
            export function detachScrollState() {
                if (!handler) return;
                window.removeEventListener('scroll', handler);
                const shell = document.querySelector('.cms-portal-shell');
                if (shell) shell.removeEventListener('scroll', handler);
                handler = null;
            }
            """;

        try
        {
            _scrollJs ??= await JS.InvokeAsync<IJSObjectReference>(
                "import", $"data:text/javascript;charset=utf-8,{Uri.EscapeDataString(js)}");
            await _scrollJs.InvokeVoidAsync("attachScrollState");
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { }
        catch (InvalidOperationException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_scrollJs is not null)
        {
            try
            {
                await _scrollJs.InvokeVoidAsync("detachScrollState");
                await _scrollJs.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (JSException) { }
            catch (InvalidOperationException) { }
        }
    }
}
