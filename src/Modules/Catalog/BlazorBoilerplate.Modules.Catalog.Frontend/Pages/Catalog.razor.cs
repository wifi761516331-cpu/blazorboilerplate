using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Modules.Catalog.OData;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Catalog.Pages;

public partial class Catalog
{
    [Inject]
    private HttpClient Http { get; set; } = default!;

    [Inject]
    private ILogger<Catalog> Logger { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private FrontendLocalizationService FrontendLoc { get; set; } = default!;

    // 模块私有投影与查询构造器（见 OData/），不暴露给 Host 或其他模块。
    private string _search = "";
    private string _minPrice = "";
    private string _category = "";
    private string _sortField = "price";
    private bool _sortDesc = true;

    private List<ProductDto> _items = [];
    private long _total;
    private int _page = 1;
    private const int PageSize = 10;
    private bool _loading;
    private string? _loadError;

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    protected override async Task OnInitializedAsync()
    {
        await FrontendLoc.EnsureLoadedAsync();
        await QueryAsync();
    }

    private async Task OnFilterChanged() => await QueryAsync(resetPage: true);

    private async Task PrevAsync()
    {
        if (_page <= 1) return;
        _page--;
        await QueryAsync();
    }

    private async Task NextAsync()
    {
        _page++;
        await QueryAsync();
    }

    private async Task QueryAsync(bool resetPage = false)
    {
        if (resetPage) _page = 1;
        _loading = true;
        _loadError = null;
        try
        {
            var builder = new ODataQueryBuilder().Top(PageSize).Skip((_page - 1) * PageSize).Count();
            if (!string.IsNullOrWhiteSpace(_search)) builder.Contains("name", _search);
            if (!string.IsNullOrWhiteSpace(_category)) builder.Contains("category", _category);
            if (decimal.TryParse(_minPrice, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var min))
                builder.GreaterThanOrEqual("price", min);
            builder.OrderBy(_sortField, _sortDesc);

            var qs = BuildQueryString(builder.Build());
            var response = await Http.GetAsync($"api/catalog/products{qs}");
            if (!response.IsSuccessStatusCode)
            {
                _loadError = L.GetString("catalog.error.queryFailedStatus", (int)response.StatusCode, await response.Content.ReadAsStringAsync());
                _items = [];
                _total = 0;
                return;
            }
            var page = await response.Content.ReadFromJsonAsync<BlazorBoilerplate.Application.OData.ODataPage<ProductDto>>(JsonOptions);
            _items = page?.Value.ToList() ?? [];
            _total = page?.Count ?? _items.Count;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "OData 查询失败");
            _loadError = L.GetString("catalog.error.queryFailed", ex.Message);
            _items = [];
            _total = 0;
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private static string BuildQueryString(BlazorBoilerplate.Application.OData.ODataQuery query)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.Filter)) parts.Add("$filter=" + Uri.EscapeDataString(query.Filter));
        if (!string.IsNullOrWhiteSpace(query.OrderBy)) parts.Add("$orderby=" + Uri.EscapeDataString(query.OrderBy));
        if (query.Top is { } top) parts.Add("$top=" + top);
        if (query.Skip is { } skip) parts.Add("$skip=" + skip);
        if (query.Count) parts.Add("$count=true");
        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }
}
