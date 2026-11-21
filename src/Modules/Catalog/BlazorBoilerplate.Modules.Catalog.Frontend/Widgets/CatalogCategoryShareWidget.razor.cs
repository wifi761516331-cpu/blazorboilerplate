using System.Globalization;
using BlazorBoilerplate.Application.Localization;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Catalog.Widgets;

public partial class CatalogCategoryShareWidget
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
            _categories = stats.Categories.Take(5).ToList();
        }
    }

    private string BuildConicGradient()
    {
        if (_total <= 0 || _categories.Count == 0)
            return "var(--bb-bg-ter)";

        var parts = new List<string>();
        double acc = 0;
        for (var i = 0; i < _categories.Count; i++)
        {
            var pct = _categories[i].Count * 100.0 / _total;
            var end = i == _categories.Count - 1 ? 100.0 : Math.Min(100.0, acc + pct);
            var color = CatalogStatsClient.Palette[i % CatalogStatsClient.Palette.Length];
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{color} {acc:0.##}% {end:0.##}%"));
            acc = end;
        }
        return $"conic-gradient({string.Join(", ", parts)})";
    }
}
