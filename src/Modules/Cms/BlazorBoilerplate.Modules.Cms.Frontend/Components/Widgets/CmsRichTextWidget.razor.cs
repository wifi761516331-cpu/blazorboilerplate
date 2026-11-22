using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsRichTextWidget
{
    public sealed record RichTextProps(string? Heading = null, string? Body = null, string? ImageUrl = null);

    [Parameter]
    public string PropsJson { get; set; } = "{}";

    private RichTextProps _props = new();

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new RichTextProps());
}
