using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsArticlesWidget
{
    public sealed record ArticleItem(
        string Title = "企业数字化转型实践",
        string? Category = "公司动态",
        string? Date = "2026-11-22",
        string? Summary = "分享模块化平台架构在大型企业门户中的落地经验。",
        string? Content = null,
        string? CoverUrl = null);

    public sealed record ArticlesProps(
        string? Heading = "新闻动态与案例",
        string? Subheading = "了解我们的最新产品发布与行业实践",
        List<ArticleItem>? Items = null);

    [Parameter] public string PropsJson { get; set; } = "{}";

    private ArticlesProps _props = new();
    private ArticleItem? _reading;

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new ArticlesProps());
}
