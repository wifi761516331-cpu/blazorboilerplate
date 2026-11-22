using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsHeadingWidget
{
    public sealed record HeadingProps(
        string Title = "标题文字",
        string? Subtitle = null,
        string Level = "h2",
        string Align = "left");

    [Parameter] public string PropsJson { get; set; } = "{}";
    private HeadingProps _props = new();

    private string AlignStyle => $"text-align:{(string.IsNullOrWhiteSpace(_props.Align) ? "left" : _props.Align)};";

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new HeadingProps());
}
