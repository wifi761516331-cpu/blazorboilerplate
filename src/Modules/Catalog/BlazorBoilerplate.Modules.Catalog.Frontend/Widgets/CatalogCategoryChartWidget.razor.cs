using BlazorBoilerplate.Application.Localization;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Catalog.Widgets;

public partial class CatalogCategoryChartWidget
{
    [Inject] private CatalogStatsClient StatsClient { get; set; } = default!;
    [Inject] private ILocalizationService L { get; set; } = default!;

    private int _total;
    private List<CatalogCategoryStatItem> _categories = [];

    protected override async Task OnInitializedAsync()
    {
        var stats = await StatsClient.GetStatsAsync();
        if (stats is not null)
        {
            _total = stats.TotalCount;
            _categories = stats.Categories.Take(6).ToList();
        }
    }
}
