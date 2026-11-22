using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsFeaturesWidget
{
    public sealed record FeatureItem(string? Icon, string Title, string Description);
    public sealed record FeaturesProps(string? Heading = null, string? Subheading = null, List<FeatureItem>? Items = null)
    {
        public List<FeatureItem> Items { get; init; } = Items ?? [];
    }

    [Parameter]
    public string PropsJson { get; set; } = "{}";

    private FeaturesProps _props = new();

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new FeaturesProps());
}
