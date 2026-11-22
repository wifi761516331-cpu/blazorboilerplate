using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsFooterWidget
{
    public sealed record FooterProps(string Brand = "企业官网", string Copyright = "© 2026 All rights reserved.", string? Tagline = null);

    [Parameter]
    public string PropsJson { get; set; } = "{}";

    private FooterProps _props = new();

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new FooterProps());
}
