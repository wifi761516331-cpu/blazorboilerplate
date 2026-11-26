using System.Text.Json;
using BlazorBoilerplate.Modules.Cms.Application.DTOs;
using BlazorBoilerplate.Modules.Cms.Domain.ValueObjects;

namespace BlazorBoilerplate.Modules.Cms.Application.AI;

/// <summary>
/// LLM 输出专用的中间结构：props/style 是原生 JSON 对象（不是字符串化的 JSON）。
/// 这样模型无需做双重转义，解析失败率大幅下降；转换到正式草稿时由代码序列化成字符串。
/// </summary>
internal sealed record LlmDraftSite(
    string? Title,
    string? Description,
    LlmDraftTheme? Theme,
    List<LlmDraftSection>? Sections,
    string? ProviderNote);

internal sealed record LlmDraftTheme(
    string? PrimaryColor,
    string? AccentColor,
    string? BackgroundColor,
    string? SurfaceColor,
    string? TextColor,
    string? FontFamily,
    string? BorderRadius,
    string? StylePreset,
    string? CustomCss);

internal sealed record LlmDraftSection(
    string? Title,
    string? AnchorSlug,
    bool NavVisible,
    string? LayoutPreset,
    List<LlmDraftWidget>? Widgets);

internal sealed record LlmDraftWidget(
    string ZoneId,
    string WidgetType,
    string Title,
    JsonElement Props,
    JsonElement? Style);

internal static class LlmDraftMapper
{
    public static CmsAiSiteDraft ToSite(LlmDraftSite d) => new(
        d.Title ?? string.Empty,
        d.Description ?? string.Empty,
        ToTheme(d.Theme),
        (d.Sections ?? []).Select(ToSection).ToList(),
        d.ProviderNote ?? string.Empty);

    public static CmsAiSectionDraft ToSection(LlmDraftSection s) => new(
        s.Title ?? string.Empty, s.AnchorSlug ?? string.Empty, s.NavVisible, s.LayoutPreset ?? string.Empty,
        (s.Widgets ?? []).Select(w => new CmsAiWidgetDraft(
            w.ZoneId, w.WidgetType, w.Title,
            Serialize(w.Props), w.Style is { } st ? Serialize(st) : null)).ToList());

    public static CmsThemeConfig ToTheme(LlmDraftTheme? t)
    {
        var fallback = new CmsThemeConfig();
        return t is null
            ? fallback
            : new CmsThemeConfig(
                t.PrimaryColor ?? fallback.PrimaryColor,
                t.AccentColor ?? fallback.AccentColor,
                t.BackgroundColor ?? fallback.BackgroundColor,
                t.SurfaceColor ?? fallback.SurfaceColor,
                t.TextColor ?? fallback.TextColor,
                t.FontFamily ?? fallback.FontFamily,
                t.BorderRadius ?? fallback.BorderRadius,
                t.CustomCss ?? fallback.CustomCss,
                t.StylePreset ?? fallback.StylePreset);
    }

    private static string Serialize(JsonElement e) =>
        e.ValueKind is JsonValueKind.Object or JsonValueKind.Array
            ? e.GetRawText()
            : "{}";
}
