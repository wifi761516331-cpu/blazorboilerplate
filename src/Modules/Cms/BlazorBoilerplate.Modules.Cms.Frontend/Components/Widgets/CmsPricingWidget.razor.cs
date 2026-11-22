using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsPricingWidget
{
    public sealed record PricingPlan(
        string Name = "标准版",
        string Price = "¥1,999",
        string? Period = "/ 年",
        string? Badge = null,
        bool Highlighted = false,
        List<string>? Features = null,
        string? ButtonText = "立即咨询",
        string? ButtonHref = "#contact");

    public sealed record PricingProps(
        string? Heading = "灵活的方案定价",
        string? Subheading = "按企业规模按需选择，随时平滑升级",
        List<PricingPlan>? Plans = null);

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Parameter] public string PropsJson { get; set; } = "{}";
    [Parameter] public EventCallback<string> OnNavigateAnchor { get; set; }
    private PricingProps _props = new();

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new PricingProps());

    private async Task HandlePlanClickAsync(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return;
        if (href.StartsWith('#'))
        {
            await OnNavigateAnchor.InvokeAsync(href[1..]);
            return;
        }
        Navigation.NavigateTo(href);
    }
}
