using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsTabsWidget
{
    public sealed record TabItem(string Title = "标签页", string Content = "标签页内容说明。", string? ImageUrl = null);
    public sealed record TabsProps(string? Heading = null, List<TabItem>? Items = null);

    [Parameter] public string PropsJson { get; set; } = "{}";

    private TabsProps _props = new();
    private List<TabItem> _items = [];
    private int _activeIndex;

    protected override void OnParametersSet()
    {
        _props = CmsJson.DeserializeOrDefault(PropsJson, new TabsProps());
        _items = _props.Items ?? [];
        if (_activeIndex >= _items.Count)
            _activeIndex = 0;
    }
}
