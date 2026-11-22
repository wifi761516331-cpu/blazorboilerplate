using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsHeroWidget
{
    public sealed record HeroProps(
        string? Badge = null,
        string Headline = "欢迎来到我们的官方网站",
        string? Subheadline = null,
        string? PrimaryButtonText = null,
        string? PrimaryButtonHref = null,
        string? SecondaryButtonText = null,
        string? SecondaryButtonHref = null);

    [Parameter]
    public string PropsJson { get; set; } = "{}";

    [Parameter]
    public EventCallback<string> OnNavigateAnchor { get; set; }

    private HeroProps _props = new();

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new HeroProps());

    private Task NavigateAsync(string? href) =>
        string.IsNullOrWhiteSpace(href) ? Task.CompletedTask : OnNavigateAnchor.InvokeAsync(href);
}
