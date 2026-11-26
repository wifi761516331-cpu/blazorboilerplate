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

    // 与 AppIcon 的内置 SVG 名称一致。AI/历史数据里出现 Cpu、HighPerformance 这类
    // 不存在的图标名时，BitIcon 会渲染成空白方块；这里兜底成可见图标，避免空卡片。
    private static readonly HashSet<string> KnownIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        "Chart", "Shield", "Check", "Globe", "Puzzle", "Grid", "People",
        "Home", "Edit", "Info", "Clock", "Mail", "Contact", "Settings",
        "Bell", "Upload", "List", "Product", "Shop", "Module", "Sparkle"
    };

    private static string ResolveIcon(string? icon) =>
        !string.IsNullOrWhiteSpace(icon) && KnownIcons.Contains(icon.Trim()) ? icon.Trim() : "Check";

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new FeaturesProps());
}
