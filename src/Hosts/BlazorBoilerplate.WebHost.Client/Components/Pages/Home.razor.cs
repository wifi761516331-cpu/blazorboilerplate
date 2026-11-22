using System.Net.Http.Json;
using System.Reflection;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Web.Shared.Services;
using BlazorBoilerplate.WebHost.Client.Services;
using BlazorBoilerplate.WebHost.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.WebHost.Components.Pages;

public partial class Home
{
    private sealed record ActivityWire(string Comment, DateTimeOffset CreatedAtUtc);
    private sealed record QuickMenuWire(string Name, string Path, string? Icon, string? Kind, int Sort, string? ModuleId, string? PermissionCode);

    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private HttpClient Http { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IModuleCatalogControl ModuleCatalogControl { get; set; } = default!;

    [Inject]
    private AccountState Account { get; set; } = default!;

    [Inject]
    private SystemSettingsState SysState { get; set; } = default!;

    [Inject]
    private ILogger<Home> Logger { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    private SetupStatus? _status;
    private int _moduleCount;
    private List<ActivityWire> _activities = [];
    private List<QuickMenuWire> _quickLinks = [];

    // 域名入口组件；URL 保持域名根路径，不暴露 /site?siteKey=...
    private Type? _domainComponentType;
    private Type? _domainLayoutType;

    protected override async Task OnInitializedAsync()
    {
        if (DomainRouteResolverRegistry.Current is { } resolver)
        {
            var match = await resolver.ResolveAsync(Http, new Uri(Navigation.BaseUri).Host);
            if (match is not null && !string.IsNullOrWhiteSpace(match.EntryPath) && match.EntryPath != "/")
            {
                var componentType = ModuleCatalogControl.ResolveComponent(match.EntryPath);
                if (componentType is not null)
                {
                    _domainComponentType = componentType;
                    // RouteView 会读取页面上的 [LayoutAttribute]，DynamicComponent 不会。
                    // 这里显式取出布局交给 LayoutView，避免域名访问绕过 CmsPortalLayout。
                    _domainLayoutType = componentType.GetCustomAttribute<LayoutAttribute>()?.LayoutType;
                    return;
                }
            }
        }

        try
        {
            await Account.EnsureLoadedAsync();

            var statusTask = Api.GetSetupStatusAsync();
            var modulesTask = Api.GetModulesAsync();
            var activitiesTask = Account.HasPermission("system.settings.read")
                ? FetchActivitiesAsync()
                : Task.FromResult(new List<ActivityWire>());
            var menusTask = FetchMenusAsync();

            await Task.WhenAll(statusTask, modulesTask, activitiesTask, menusTask);

            var modules = modulesTask.Result;
            if (modules.Count > 0)
            {
                var runningIds = modules
                    .Where(m => m.State == ModuleState.Running)
                    .Select(m => m.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                WidgetZoneRegistry.UnregisterInactiveModules(runningIds);
            }
            _status = statusTask.Result;
            _moduleCount = modules.Count(m => m.State == ModuleState.Running);
            _activities = activitiesTask.Result;
            _quickLinks = FilterQuickLinks(menusTask.Result, modules);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Home 页面加载失败");
            Toast.Error(L.GetString("platform.home.error.apiHost", ex.Message));
        }
    }

    private async Task<List<ActivityWire>> FetchActivitiesAsync()
    {
        try
        {
            return await Http.GetFromJsonAsync<List<ActivityWire>>(
                "api/v1/PlatformExtensions/GetActivityLogs?take=5") ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<QuickMenuWire>> FetchMenusAsync()
    {
        try
        {
            return await Api.GetListAsync<QuickMenuWire>("api/identity/menus/visible");
        }
        catch
        {
            return [];
        }
    }

    private List<QuickMenuWire> FilterQuickLinks(
        IReadOnlyList<QuickMenuWire> menus,
        IReadOnlyList<ModuleDescriptor> modules)
    {
        var runningIds = modules
            .Where(m => m.State == ModuleState.Running)
            .Select(m => m.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nonRunningRoutes = modules
            .Where(m => m.State != ModuleState.Running)
            .SelectMany(m => m.Routes)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.TrimEnd('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return menus
            .Where(m => !string.IsNullOrWhiteSpace(m.Path)
                        && m.Path != "/"
                        && !string.Equals(m.Kind, "group", StringComparison.OrdinalIgnoreCase)
                        && (string.IsNullOrWhiteSpace(m.PermissionCode) || Account.HasPermission(m.PermissionCode))
                        && (string.IsNullOrWhiteSpace(m.ModuleId) || runningIds.Contains(m.ModuleId))
                        && !nonRunningRoutes.Contains(m.Path.TrimEnd('/')))
            .OrderBy(m => m.Sort)
            .DistinctBy(m => m.Path.TrimEnd('/'), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
