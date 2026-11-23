using System.Text.Json;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Models;

/// <summary>内置只读官网模板标识集合（不可直接编辑/删除，仅可复制为新站点）。</summary>
public static class CmsBuiltInTemplates
{
    public static readonly IReadOnlySet<string> ReadOnlyKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "default",
        "restaurant-demo",
        "saas-demo",
        "education-demo",
        "portfolio-demo",
        "umbraco-demo"
    };

    public static bool IsReadOnly(string? siteKey) =>
        !string.IsNullOrWhiteSpace(siteKey) && ReadOnlyKeys.Contains(siteKey.Trim());
}

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

public sealed record CmsLayoutRow(string Id, string? CssClass, List<CmsLayoutColumn> Columns);

public sealed record CmsLayoutColumn(string ZoneId, string Label, int Span = 12, string? CssClass = null);

public sealed record CmsWidgetStyle(
    string? BackgroundColor = null,
    string? BackgroundImageUrl = null,
    string? TextColor = null,
    string? Padding = null,
    string? Margin = null,
    string? TextAlign = null,
    string? BorderRadius = null,
    string? CustomCssClass = null,
    string? Animation = null,
    string? Width = null,
    string? Height = null);

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

public sealed record CmsPublishedWidget(
    Guid Id,
    string ZoneId,
    string WidgetType,
    string Title,
    string PropsJson,
    string? StyleJson,
    int SortOrder);

public sealed record CmsSiteDto(
    Guid Id,
    string Name,
    string SiteKey,
    string Title,
    string? Description,
    string? LogoUrl,
    string? FaviconUrl,
    string DefaultCulture,
    List<string> SupportedCultures,
    CmsThemeConfig Theme,
    bool IsPublished,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ModifiedAtUtc);

public sealed record CmsLayoutDto(
    Guid Id,
    string Name,
    string? Description,
    string? ThumbnailUrl,
    List<CmsLayoutRow> Rows,
    string? CustomCss,
    bool IsBuiltIn,
    int SortOrder);

public sealed record CmsSectionDto(
    Guid Id,
    Guid SiteId,
    Guid LayoutId,
    string Culture,
    string Title,
    string AnchorSlug,
    bool NavVisible,
    int SortOrder,
    string? BackgroundStyle,
    bool IsEnabled);

public sealed record CmsWidgetDto(
    Guid Id,
    Guid SiteId,
    Guid SectionId,
    string Culture,
    string ZoneId,
    string WidgetType,
    string Title,
    string PropsJson,
    string? StyleJson,
    int SortOrder,
    bool IsTemplate,
    string? TemplateName);

public sealed record CmsSiteDesignDto(
    CmsSiteDto Site,
    string ActiveCulture,
    List<CmsLayoutDto> Layouts,
    List<CmsSectionDto> Sections,
    List<CmsWidgetDto> Widgets);

public sealed record CmsLeadDto(
    Guid Id,
    Guid SiteId,
    Guid? SectionId,
    string Name,
    string? Email,
    string? Phone,
    string? Company,
    string Message,
    string? ExtraJson,
    string Status,
    DateTimeOffset CreatedAtUtc);

public sealed record SubmitCmsLeadRequest(
    string SiteKey,
    Guid? SectionId,
    string Name,
    string? Email,
    string? Phone,
    string? Company,
    string Message,
    string? ExtraJson = null);

public sealed record UpsertCmsSiteRequest(string Name, string SiteKey, string Title, string? Description = null, string? LogoUrl = null, string? FaviconUrl = null, string? DefaultCulture = null, List<string>? SupportedCultures = null);
public sealed record UpsertCmsLayoutRequest(string Name, string? Description, List<CmsLayoutRow> Rows, string? CustomCss = null, string? ThumbnailUrl = null, int SortOrder = 0);
public sealed record UpsertCmsSectionRequest(Guid LayoutId, string Title, string AnchorSlug, bool NavVisible = true, int SortOrder = 0, string? BackgroundStyle = null, bool IsEnabled = true, string? Culture = null);
public sealed record UpsertCmsWidgetRequest(Guid SiteId, Guid SectionId, string ZoneId, string WidgetType, string Title, string PropsJson, string? StyleJson = null, int SortOrder = 0, string? Culture = null);
public sealed record MoveCmsWidgetRequest(Guid SectionId, string ZoneId, int SortOrder);

public sealed record CmsAiGenerateSiteRequest(string BrandName, string Industry, string StylePrompt, string? PrimaryColor = null, string Language = "zh-CN");
public sealed record CmsAiWidgetDraft(string ZoneId, string WidgetType, string Title, string PropsJson, string? StyleJson = null);
public sealed record CmsAiSectionDraft(string Title, string AnchorSlug, bool NavVisible, string LayoutPreset, List<CmsAiWidgetDraft> Widgets);
public sealed record CmsAiSiteDraft(string Title, string Description, CmsThemeConfig Theme, List<CmsAiSectionDraft> Sections, string ProviderNote);

public static class CmsJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static T DeserializeOrDefault<T>(string? json, T fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
            return fallback;
        try { return JsonSerializer.Deserialize<T>(json, Options) ?? fallback; }
        catch { return fallback; }
    }
}
