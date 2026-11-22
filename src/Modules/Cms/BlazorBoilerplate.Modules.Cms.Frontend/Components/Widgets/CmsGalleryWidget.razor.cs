using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsGalleryWidget
{
    public sealed record GalleryItem(string Url, string? Caption = null);
    public sealed record GalleryProps(string? Heading = null, List<GalleryItem>? Items = null)
    {
        public List<GalleryItem> Items { get; init; } = Items ?? [];
    }

    [Parameter]
    public string PropsJson { get; set; } = "{}";

    private GalleryProps _props = new();

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new GalleryProps());
}
