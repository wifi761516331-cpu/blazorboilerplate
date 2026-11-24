namespace BlazorBoilerplate.Modules.Cms.Domain.ValueObjects;

/// <summary>站点主题视觉配置（映射为单页前端 CSS 变量）。</summary>
public sealed record CmsThemeConfig(
    string PrimaryColor = "#0f6cbd",
    string AccentColor = "#115ea3",
    string BackgroundColor = "#ffffff",
    string SurfaceColor = "#f5f7fa",
    string TextColor = "#1f2328",
    string FontFamily = "system-ui, -apple-system, 'Segoe UI', sans-serif",
    string BorderRadius = "8px",
    string? CustomCss = null,
    string StylePreset = "classic");

/// <summary>布局页中的单行网格定义（12 栅格）。</summary>
public sealed record CmsLayoutRow(
    string Id,
    string? CssClass,
    List<CmsLayoutColumn> Columns);

/// <summary>
/// 布局行中的列与命名插槽（Zone）。
/// Children 不为空时，本列作为嵌套容器渲染子 Rows（子行内可继续嵌套），否则作为叶子插槽承载 widgets。
/// 整棵布局树序列化在 CmsLayout.RowsJson 中，无需额外数据库表。
/// </summary>
public sealed record CmsLayoutColumn(
    string ZoneId,
    string Label,
    int Span = 12,
    string? CssClass = null,
    List<CmsLayoutRow>? Children = null);

/// <summary>递归布局工具：收集所有叶子 ZoneId、校验层级深度。</summary>
public static class CmsLayoutTree
{
    public const int MaxDepth = 4;

    /// <summary>收集整棵布局树中所有可承载 widget 的叶子 ZoneId（有 Children 的列本身不是叶子）。</summary>
    public static HashSet<string> CollectLeafZones(IEnumerable<CmsLayoutRow> rows)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(rows, result, 0);
        return result;
    }

    private static void Collect(IEnumerable<CmsLayoutRow> rows, HashSet<string> zones, int depth)
    {
        if (depth > MaxDepth) return;
        foreach (var col in rows.SelectMany(r => r.Columns))
        {
            if (col.Children is { Count: > 0 })
                Collect(col.Children, zones, depth + 1);
            else if (!string.IsNullOrWhiteSpace(col.ZoneId))
                zones.Add(col.ZoneId.Trim());
        }
    }

    /// <summary>布局树是否合法：ZoneId 唯一、Span 在 1..12、层级不超过 MaxDepth。</summary>
    public static bool Validate(IEnumerable<CmsLayoutRow> rows, out string? error)
    {
        var zones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return ValidateCore(rows, zones, 0, out error);
    }

    private static bool ValidateCore(IEnumerable<CmsLayoutRow> rows, HashSet<string> zones, int depth, out string? error)
    {
        error = null;
        if (depth > MaxDepth) { error = $"布局嵌套层级不能超过 {MaxDepth} 层。"; return false; }
        foreach (var col in rows.SelectMany(r => r.Columns))
        {
            if (string.IsNullOrWhiteSpace(col.ZoneId)) { error = "插槽标识 ZoneId 不能为空。"; return false; }
            if (!zones.Add(col.ZoneId.Trim())) { error = $"插槽标识重复：{col.ZoneId}。"; return false; }
            if (col.Span is < 1 or > 12) { error = $"插槽 {col.ZoneId} 的栅格宽度必须在 1–12 之间。"; return false; }
            if (col.Children is { Count: > 0 } && !ValidateCore(col.Children, zones, depth + 1, out error)) return false;
        }
        return true;
    }
}

/// <summary>微件级外观样式配置。</summary>
public sealed record CmsWidgetStyle(
    string? BackgroundColor = null,
    string? BackgroundImageUrl = null,
    string? TextColor = null,
    string? Padding = null,
    string? Margin = null,
    string? TextAlign = null,
    string? BorderRadius = null,
    string? CustomCssClass = null);

/// <summary>已发布站点的不可变单页快照（按语言隔离）。</summary>
public sealed record CmsPublishedSnapshot(
    Guid SiteId,
    string SiteKey,
    string Culture,
    string DefaultCulture,
    List<string> SupportedCultures,
    string Title,
    string? LogoUrl,
    string? FaviconUrl,
    CmsThemeConfig Theme,
    DateTimeOffset PublishedAtUtc,
    List<CmsPublishedSection> Sections);

/// <summary>快照中的单页逻辑区块。</summary>
public sealed record CmsPublishedSection(
    Guid Id,
    string Title,
    string AnchorSlug,
    bool NavVisible,
    int SortOrder,
    string? BackgroundStyle,
    List<CmsLayoutRow> Rows,
    string? LayoutCustomCss,
    List<CmsPublishedWidget> Widgets);

/// <summary>快照中的微件实例。</summary>
public sealed record CmsPublishedWidget(
    Guid Id,
    string ZoneId,
    string WidgetType,
    string Title,
    string PropsJson,
    string? StyleJson,
    int SortOrder);
