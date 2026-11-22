using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsMapWidget
{
    public sealed record MapProps(
        string? Heading = null,
        string? Subheading = null,
        string? Title = null,
        string? Address = null,
        double Longitude = 113.6401,
        double Latitude = 34.72468,
        int Zoom = 12,
        int Height = 360,
        string? AmapKey = "adc139d56406f3844c8f1cf1c6b65c41",
        string? SecurityJsCode = null,
        string? MapEmbedUrl = null);

    [Parameter]
    public string PropsJson { get; set; } = "{}";

    private MapProps _props = new();

    protected override void OnParametersSet()
    {
        _props = CmsJson.DeserializeOrDefault(PropsJson, new MapProps());
    }
}
