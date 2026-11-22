using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsImageWidget
{
    public sealed record ImageProps(
        string? ImageUrl = null,
        string? Alt = null,
        string? Caption = null,
        string? LinkUrl = null,
        int MaxHeight = 420,
        bool Rounded = true,
        bool Shadow = true);

    [Parameter] public string PropsJson { get; set; } = "{}";
    private ImageProps _props = new();

    private string ImgStyle => _props.MaxHeight > 0 ? $"max-height:{_props.MaxHeight}px;" : string.Empty;

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new ImageProps());
}
