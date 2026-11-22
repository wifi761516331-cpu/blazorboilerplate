using System.Globalization;
using BlazorBoilerplate.Application.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsAmapView : IAsyncDisposable
{
    private const string JsModuleSource = """
        const maps = new Map();
        let sdkPromise = null;

        function loadAmapSdk(key, securityJsCode) {
            if (window.AMap) return Promise.resolve(window.AMap);
            if (securityJsCode) {
                window._AMapSecurityConfig = { securityJsCode };
            }
            if (!sdkPromise) {
                sdkPromise = new Promise((resolve, reject) => {
                    const script = document.createElement('script');
                    script.src = `https://webapi.amap.com/maps?v=2.0&key=${encodeURIComponent(key)}`;
                    script.async = true;
                    script.onload = () => resolve(window.AMap);
                    script.onerror = (err) => {
                        sdkPromise = null;
                        reject(err);
                    };
                    document.head.appendChild(script);
                });
            }
            return sdkPromise;
        }

        export async function renderMap(containerId, opts) {
            const el = document.getElementById(containerId);
            if (!el || !opts?.key) return;
            const AMap = await loadAmapSdk(opts.key, opts.securityJsCode);
            if (!AMap) return;

            const existing = maps.get(containerId);
            if (existing) {
                try { existing.destroy(); } catch {}
                maps.delete(containerId);
            }

            const center = [opts.lng, opts.lat];
            const map = new AMap.Map(containerId, {
                zoom: opts.zoom || 15,
                center,
                viewMode: '2D'
            });

            const marker = new AMap.Marker({
                position: center,
                title: opts.title || ''
            });
            map.add(marker);

            if (opts.title || opts.address) {
                const safeTitle = String(opts.title || '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
                const safeAddr = String(opts.address || '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
                const info = new AMap.InfoWindow({
                    content: `<div style="padding:4px 6px;font-size:13px;line-height:1.5;"><strong>${safeTitle}</strong>${safeAddr ? `<div style="opacity:.78;">${safeAddr}</div>` : ''}</div>`,
                    offset: new AMap.Pixel(0, -28)
                });
                info.open(map, center);
                marker.on('click', () => info.open(map, center));
            }

            maps.set(containerId, map);
        }

        export function disposeMap(containerId) {
            const existing = maps.get(containerId);
            if (existing) {
                try { existing.destroy(); } catch {}
                maps.delete(containerId);
            }
        }
        """;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Parameter] public string? Title { get; set; }
    [Parameter] public string? Address { get; set; }
    [Parameter] public double Longitude { get; set; } = 113.6401;
    [Parameter] public double Latitude { get; set; } = 34.72468;
    [Parameter] public int Zoom { get; set; } = 15;
    [Parameter] public int Height { get; set; } = 210;
    [Parameter] public bool FillHeight { get; set; }
    [Parameter] public string? AmapKey { get; set; } = "adc139d56406f3844c8f1cf1c6b65c41";
    [Parameter] public string? SecurityJsCode { get; set; }
    [Parameter] public string? MapEmbedUrl { get; set; }
    [Parameter] public string? Coordinates { get; set; }

    private readonly string _containerId = $"cms-amap-{Guid.NewGuid():N}";
    private IJSObjectReference? _jsModule;
    private string? _lastRenderSignature;

    private int EffectiveHeight => Math.Clamp(Height, 140, 640);
    private string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? L["cms.contact.mapTitle"] : Title!;
    private string DisplayCoordinates => !string.IsNullOrWhiteSpace(Coordinates)
        ? Coordinates!
        : string.Create(CultureInfo.InvariantCulture, $"{Latitude:F4}° N, {Longitude:F4}° E");

    private string AmapUriUrl
    {
        get
        {
            var pos = string.Create(CultureInfo.InvariantCulture, $"{Longitude:F6},{Latitude:F6}");
            var name = Uri.EscapeDataString(DisplayTitle);
            return $"https://uri.amap.com/marker?position={pos}&name={name}";
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (string.IsNullOrWhiteSpace(AmapKey))
            return;

        var sig = $"{AmapKey}|{SecurityJsCode}|{Longitude:F6}|{Latitude:F6}|{Zoom}|{Title}|{Address}";
        if (string.Equals(_lastRenderSignature, sig, StringComparison.Ordinal))
            return;

        _lastRenderSignature = sig;
        try
        {
            _jsModule ??= await JS.InvokeAsync<IJSObjectReference>(
                "import",
                $"data:text/javascript;charset=utf-8,{Uri.EscapeDataString(JsModuleSource)}");

            await _jsModule.InvokeVoidAsync("renderMap", _containerId, new
            {
                key = AmapKey.Trim(),
                securityJsCode = string.IsNullOrWhiteSpace(SecurityJsCode) ? null : SecurityJsCode.Trim(),
                lng = Longitude,
                lat = Latitude,
                zoom = Math.Clamp(Zoom, 3, 20),
                title = DisplayTitle,
                address = Address ?? string.Empty
            });
        }
        catch (JSDisconnectedException) { }
        catch (InvalidOperationException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_jsModule is null) return;
        try
        {
            await _jsModule.InvokeVoidAsync("disposeMap", _containerId);
            await _jsModule.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (InvalidOperationException) { }
    }
}
