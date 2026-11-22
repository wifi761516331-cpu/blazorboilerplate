using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsTextWidget
{
    public sealed record TextProps(
        string Text = "在这里填写正文段落内容，支持多段描述与对齐方式设置。",
        string Align = "left",
        string FontSize = "1rem");

    [Parameter] public string PropsJson { get; set; } = "{}";
    private TextProps _props = new();

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new TextProps());
}
