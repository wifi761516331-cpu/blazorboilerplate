using System.Net.Http.Json;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Demo.Widgets;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Demo;

/// <summary>
/// 前端模块启动器示例（实现 <see cref="IModuleClientStartup"/>）。
/// WebHost 加载前端程序集时自动调用，用于注册前端服务与微件插槽。
/// </summary>
public sealed class DemoClientStartup : IModuleClientStartup
{
    public int Order => 3100;

    public void ConfigureServices(IServiceCollection services)
    {
        WidgetZoneRegistry.Register("header_actions", typeof(DemoHeaderActionWidget), "demo", order: 100);
        WidgetZoneRegistry.Register("account_menu_items", typeof(DemoAccountMenuWidget), "demo", order: 100);
        WidgetZoneRegistry.Register("home_widgets_top", typeof(DemoHomeWidget), "demo", order: 100);
        WidgetZoneRegistry.Register("home_widgets_bottom", typeof(DemoHomeBottomWidget), "demo", order: 100);
        WidgetZoneRegistry.Register("layout_body_bottom", typeof(DemoLayoutBottomWidget), "demo", order: 100);
        WidgetZoneRegistry.Register("page_catalog_top", typeof(DemoCatalogWidget), "demo", order: 100);
    }
}

/// <summary>
/// 标记接口 <see cref="IClientService"/> 示例：无需手动写 AddScoped，WebHost 会自动扫描注册。
/// </summary>
public sealed class DemoClientService(HttpClient http) : IClientService
{
    public async Task<DemoOverviewModel?> GetOverviewAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<DemoOverviewModel>("api/demo/overview", ct);

    public async Task<DemoShowcaseResultModel?> RunShowcaseAsync(CancellationToken ct = default)
    {
        var resp = await http.PostAsync("api/demo/run-showcase", null, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<DemoShowcaseResultModel>(ct);
    }

    public async Task SaveSettingsAsync(DemoSettingsModel settings, CancellationToken ct = default)
    {
        var resp = await http.PostAsJsonAsync("api/demo/settings", settings, ct);
        resp.EnsureSuccessStatusCode();
    }
}

public sealed class DemoSettingsModel
{
    public string GreetingPrefix { get; set; } = "Hello from DemoSettings";
    public bool EnableAutoAudit { get; set; } = true;
    public int MaxBatchSize { get; set; } = 20;
}

public sealed record DemoNoteDto(Guid Id, string Title, DateTimeOffset CreatedAtUtc);

public sealed record DemoOverviewModel(
    DemoSettingsModel Settings,
    IReadOnlyList<string> RegisteredProviders,
    IReadOnlyList<string> RegisteredRuleKeys,
    bool AdminRuleMatched,
    string CacheStatus,
    int DatabaseNoteCount);

public sealed record DemoShowcaseResultModel(
    bool LockAcquired,
    string CacheValue,
    string TemplateSubject,
    string TemplateBody,
    Guid QueuedEmailId,
    bool RuleMatched,
    int SearchHits,
    int ExportCsvBytes,
    string ProviderPreview,
    string EventPublished,
    string ChannelPublished,
    Guid SavedNoteId,
    int TotalNotes);

