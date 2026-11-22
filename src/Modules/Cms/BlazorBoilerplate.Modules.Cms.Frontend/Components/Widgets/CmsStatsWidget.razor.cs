using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsStatsWidget
{
    public sealed record StatItem(string Value, string Label);
    public sealed record StatsProps(List<StatItem>? Items = null)
    {
        public List<StatItem> Items { get; init; } = Items ?? [];
    }

    [Parameter]
    public string PropsJson { get; set; } = "{}";

    private StatsProps _props = new();

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new StatsProps());
}
