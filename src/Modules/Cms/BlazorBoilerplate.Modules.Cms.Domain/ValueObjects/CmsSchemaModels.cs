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

/// <summary>布局行中的列与命名插槽（Zone）。</summary>
public sealed record CmsLayoutColumn(
    string ZoneId,
    string Label,
    int Span = 12,
    string? CssClass = null);

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
