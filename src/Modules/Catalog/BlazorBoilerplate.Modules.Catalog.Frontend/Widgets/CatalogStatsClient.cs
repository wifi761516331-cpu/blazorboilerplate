using System.Net.Http.Json;
using BlazorBoilerplate.ModuleAbstractions;

namespace BlazorBoilerplate.Modules.Catalog.Widgets;

public sealed record CatalogCategoryStatItem(string Category, int Count);
public sealed record CatalogStatsSnapshot(int TotalCount, List<CatalogCategoryStatItem> Categories);

/// <summary>
/// Scoped 统计缓存服务：Home 页多个 Catalog 微件共享同一次 api/catalog/stats 请求，避免重复发包。
/// </summary>
public sealed class CatalogStatsClient(HttpClient http) : IClientService
{
    public static readonly string[] Palette = ["#0078d4", "#4bc0c0", "#ffcd56", "#ff6384", "#ff9124", "#8764b8"];

    private Task<CatalogStatsSnapshot?>? _pending;
    private long _expiresAtTicks;

    public Task<CatalogStatsSnapshot?> GetStatsAsync(CancellationToken ct = default)
    {
        var now = Environment.TickCount64;
        if (_pending is not null && _expiresAtTicks > now)
            return _pending;

        _expiresAtTicks = now + 5_000;
        _pending = FetchCoreAsync(ct);
        return _pending;
    }

    private async Task<CatalogStatsSnapshot?> FetchCoreAsync(CancellationToken ct)
    {
        try
        {
            return await http.GetFromJsonAsync<CatalogStatsSnapshot>("api/catalog/stats", ct);
        }
        catch
        {
            _expiresAtTicks = 0;
            return null;
        }
    }
}
