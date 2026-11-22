using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsFaqWidget
{
    public sealed record FaqItem(string Question, string Answer);
    public sealed record FaqProps(string? Heading = null, List<FaqItem>? Items = null)
    {
        public List<FaqItem> Items { get; init; } = Items ?? [];
    }

    [Parameter]
    public string PropsJson { get; set; } = "{}";

    private FaqProps _props = new();
    private int? _expandedIndex = 0;

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new FaqProps());

    private void Toggle(int idx) =>
        _expandedIndex = _expandedIndex == idx ? null : idx;
}
