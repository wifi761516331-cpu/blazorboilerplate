using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using BlazorBoilerplate.WebHost.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.WebHost.Components.Pages;

public partial class Home
{
    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private SystemSettingsState SysState { get; set; } = default!;

    [Inject]
    private ILogger<Home> Logger { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    private SetupStatus? _status;
    private int _moduleCount;
    private int? _productCount;
    private string? _loadError;

    protected override async Task OnInitializedAsync()
    {
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
        }
    }
}
