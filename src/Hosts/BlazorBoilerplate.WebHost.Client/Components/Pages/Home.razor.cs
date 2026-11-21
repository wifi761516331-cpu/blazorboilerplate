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
    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private HttpClient Http { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IModuleCatalogControl ModuleCatalogControl { get; set; } = default!;

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
    private int? _productCount;
    private string? _loadError;
    private Type? _domainComponentType;

    protected override async Task OnInitializedAsync()
    {
        if (DomainRouteResolverRegistry.Current is { } resolver)
        {
            var match = await resolver.ResolveAsync(Http, new Uri(Navigation.BaseUri).Host);
            if (match is not null && !string.IsNullOrWhiteSpace(match.EntryPath) && match.EntryPath != "/")
            {
                _domainComponentType = ModuleCatalogControl.ResolveComponent(match.EntryPath);
                if (_domainComponentType is not null)
                    return;
            }
        }

        try
        {
            _status = await Api.GetSetupStatusAsync();
            _moduleCount = (await Api.GetModulesAsync()).Count;

            try
            {
                var page = await Api.GetODataAsync<System.Text.Json.JsonElement>(
                    "api/catalog/products", new BlazorBoilerplate.Application.OData.ODataQuery { Top = 1, Count = true });
                _productCount = (int?)page.Count;
            }
            catch
            {
                _productCount = null;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Home 页面加载失败");
            _loadError = ex.Message;
            Toast.Error(L.GetString("platform.home.error.apiHost", ex.Message));
        }
    }
}
