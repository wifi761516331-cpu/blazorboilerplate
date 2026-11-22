using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsVideoWidget
{
    public sealed record VideoProps(
        string? VideoUrl = null,
        string? PosterUrl = null,
        string? Caption = null,
        bool Controls = true,
        bool AutoPlay = false,
        bool Loop = false,
        int MaxHeight = 460);

    [Parameter] public string PropsJson { get; set; } = "{}";
    private VideoProps _props = new();

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new VideoProps());
}
