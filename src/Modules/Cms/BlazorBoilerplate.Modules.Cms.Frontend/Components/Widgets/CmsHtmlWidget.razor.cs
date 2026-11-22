using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsHtmlWidget
{
    public sealed record HtmlProps(string Html = "<div style=\"padding:1rem;border:1px dashed #cbd5e1;border-radius:8px;\">自定义 HTML 内容区</div>");

    [Parameter] public string PropsJson { get; set; } = "{}";
    private HtmlProps _props = new();

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new HtmlProps());
}
