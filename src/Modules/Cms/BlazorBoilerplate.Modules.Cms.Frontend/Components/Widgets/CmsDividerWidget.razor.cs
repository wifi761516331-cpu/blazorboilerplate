using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsDividerWidget
{
    public sealed record DividerProps(
        int Height = 24,
        bool ShowLine = true,
        string? Label = null);

    [Parameter] public string PropsJson { get; set; } = "{}";
    private DividerProps _props = new();

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new DividerProps());
}
