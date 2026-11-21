using BlazorBoilerplate.Application.Localization;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Catalog.Widgets;

public partial class CatalogStatWidget
{
    [Inject] private CatalogStatsClient StatsClient { get; set; } = default!;
    [Inject] private ILocalizationService L { get; set; } = default!;

    private int? _count;

    protected override async Task OnInitializedAsync()
    {
        var stats = await StatsClient.GetStatsAsync();
        _count = stats?.TotalCount;
    }
}
