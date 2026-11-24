using BlazorBoilerplate.Modules.Cms.Application.AI;
using BlazorBoilerplate.Modules.Cms.Domain.Entities;
using BlazorBoilerplate.Modules.Cms.Domain.ValueObjects;

namespace BlazorBoilerplate.Modules.Cms.Application.DTOs;

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
    DateTimeOffset? UpdatedAtUtc,
    bool IsReadOnly = false);

public sealed record UpsertCmsSiteRequest(
    string Name,
    string SiteKey,
    string Title,
    string? Description = null,
    string? LogoUrl = null,
    string? FaviconUrl = null,
    string? DefaultCulture = null,
    List<string>? SupportedCultures = null);

public sealed record UpdateSiteLanguagesRequest(string DefaultCulture, List<string> SupportedCultures);

public sealed record CmsLayoutDto(
    Guid Id,
    string Name,
    string? Description,
    string? ThumbnailUrl,
    List<CmsLayoutRow> Rows,
    string? CustomCss,
    bool IsBuiltIn,
    int SortOrder);

public sealed record UpsertCmsLayoutRequest(
    string Name,
    string? Description,
    List<CmsLayoutRow> Rows,
    string? CustomCss = null,
    string? ThumbnailUrl = null,
    int SortOrder = 0);

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

public sealed record UpsertCmsSectionRequest(
    Guid LayoutId,
    string Title,
    string AnchorSlug,
    bool NavVisible = true,
    int SortOrder = 0,
    string? BackgroundStyle = null,
    bool IsEnabled = true,
    string? Culture = null);

public sealed record ReorderSectionsRequest(List<Guid> SectionIds);

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

public sealed record UpsertCmsWidgetRequest(
    Guid SiteId,
    Guid SectionId,
    string ZoneId,
    string WidgetType,
    string Title,
    string PropsJson,
    string? StyleJson = null,
    int SortOrder = 0,
    string? Culture = null);

public sealed record MoveCmsWidgetRequest(Guid SectionId, string ZoneId, int SortOrder);

public sealed record SaveWidgetTemplateRequest(string TemplateName);

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

public sealed record UpdateLeadStatusRequest(string Status);

internal static class CmsDtoMapper
{
    internal static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static CmsThemeConfig DeserializeTheme(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new CmsThemeConfig();
        try { return System.Text.Json.JsonSerializer.Deserialize<CmsThemeConfig>(json, JsonOptions) ?? new CmsThemeConfig(); }
        catch { return new CmsThemeConfig(); }
    }

    public static List<CmsLayoutRow> DeserializeRows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try { return System.Text.Json.JsonSerializer.Deserialize<List<CmsLayoutRow>>(json, JsonOptions) ?? []; }
        catch { return []; }
    }

    public static List<string> DeserializeCultures(string? json, string defaultCulture)
    {
        List<string>? list = null;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try { list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json, JsonOptions); }
            catch { list = null; }
        }
        list ??= [];
        if (list.Count == 0)
            list.Add(string.IsNullOrWhiteSpace(defaultCulture) ? "zh-CN" : defaultCulture);
        return list;
    }

    public static CmsSiteDto ToDto(this CmsSite s) => new(
        s.Id, s.Name, s.SiteKey, s.Title, s.Description, s.LogoUrl, s.FaviconUrl,
        s.DefaultCulture, DeserializeCultures(s.SupportedCulturesJson, s.DefaultCulture),
        DeserializeTheme(s.ThemeJson), s.IsPublished, s.PublishedAtUtc, s.CreatedAtUtc, s.ModifiedAtUtc,
        CmsSiteTemplateLibrary.IsBuiltInTemplateKey(s.SiteKey));

    public static CmsLayoutDto ToDto(this CmsLayout l) => new(
        l.Id, l.Name, l.Description, l.ThumbnailUrl, DeserializeRows(l.RowsJson), l.CustomCss, l.IsBuiltIn, l.SortOrder);

    public static CmsSectionDto ToDto(this CmsSection s) => new(
        s.Id, s.SiteId, s.LayoutId, s.Culture, s.Title, s.AnchorSlug, s.NavVisible, s.SortOrder, s.BackgroundStyle, s.IsEnabled);

    public static CmsWidgetDto ToDto(this CmsWidgetInstance w) => new(
        w.Id, w.SiteId, w.SectionId, w.Culture, w.ZoneId, w.WidgetType, w.Title, w.PropsJson, w.StyleJson, w.SortOrder, w.IsTemplate, w.TemplateName);

    public static CmsLeadDto ToDto(this CmsLeadSubmission l) => new(
        l.Id, l.SiteId, l.SectionId, l.Name, l.Email, l.Phone, l.Company, l.Message, l.ExtraJson, l.Status, l.CreatedAtUtc);
}
