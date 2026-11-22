using BlazorBoilerplate.Modules.Cms.Domain.ValueObjects;

namespace BlazorBoilerplate.Modules.Cms.Application.AI;

/// <summary>AI 一键生成单页官网请求。</summary>
public sealed record CmsAiGenerateSiteRequest(
    string BrandName,
    string Industry,
    string StylePrompt,
    string? PrimaryColor = null,
    string Language = "zh-CN");

/// <summary>AI 生成单个区块请求。</summary>
public sealed record CmsAiGenerateSectionRequest(
    Guid SiteId,
    string Prompt,
    string? TargetSlug = null,
    string Language = "zh-CN");

/// <summary>AI 润色/改写单个微件请求。</summary>
public sealed record CmsAiRefineWidgetRequest(
    Guid WidgetId,
    string WidgetType,
    string CurrentPropsJson,
    string Instruction,
    string Language = "zh-CN");

/// <summary>AI 生成微件草稿。</summary>
public sealed record CmsAiWidgetDraft(
    string ZoneId,
    string WidgetType,
    string Title,
    string PropsJson,
    string? StyleJson = null);

/// <summary>AI 生成区块草稿。</summary>
public sealed record CmsAiSectionDraft(
    string Title,
    string AnchorSlug,
    bool NavVisible,
    string LayoutPreset,
    List<CmsAiWidgetDraft> Widgets);

/// <summary>AI 生成整站单页草稿。</summary>
public sealed record CmsAiSiteDraft(
    string Title,
    string Description,
    CmsThemeConfig Theme,
    List<CmsAiSectionDraft> Sections,
    string ProviderNote);

/// <summary>
/// 预留的 AI 辅助官网设计契约：
/// 后续接入 OpenAI / DeepSeek / Gemini 等大模型时，只需注册自定义实现替换默认实现即可，
/// 输出结构严格遵循 CMS Layout + Widget Schema，可直接进入可视化设计器进行微调与发布。
/// </summary>
public interface ICmsAiDesignerService
{
    Task<CmsAiSiteDraft> GenerateSiteAsync(CmsAiGenerateSiteRequest request, CancellationToken ct = default);
    Task<CmsAiSectionDraft> GenerateSectionAsync(CmsAiGenerateSectionRequest request, CancellationToken ct = default);
    Task<CmsAiWidgetDraft> RefineWidgetAsync(CmsAiRefineWidgetRequest request, CancellationToken ct = default);
}
