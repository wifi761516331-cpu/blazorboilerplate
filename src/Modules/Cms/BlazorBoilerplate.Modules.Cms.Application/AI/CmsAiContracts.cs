using BlazorBoilerplate.Modules.Cms.Domain.ValueObjects;

namespace BlazorBoilerplate.Modules.Cms.Application.AI;

/// <summary>AI 一键生成单页官网请求。</summary>
public sealed record CmsAiGenerateSiteRequest(
    string BrandName,
    string Industry,
    string StylePrompt,
    string? PrimaryColor = null,
    string Language = "zh-CN",
    string? Model = null);

/// <summary>分块生成请求：在整站生成参数基础上带目标 SiteId 与语言，供端点逐块落库。</summary>
public sealed record CmsAiChunkedGenerateRequest(
    Guid SiteId,
    string BrandName,
    string Industry,
    string StylePrompt,
    string? PrimaryColor = null,
    string Language = "zh-CN",
    string? Model = null)
{
    public CmsAiGenerateSiteRequest ToSiteRequest() =>
        new(BrandName, Industry, StylePrompt, PrimaryColor, Language, Model);
}

/// <summary>AI 模型下拉列表：DefaultModel 默认 auto，Models 来自网关 /v1/models。</summary>
public sealed record CmsAiModelsDto(string DefaultModel, IReadOnlyList<string> Models);

/// <summary>
/// 生成过程的 SSE 事件。Kind: delta（模型原文片段）/ status（阶段提示）/
/// plan（站点骨架就绪，DraftJson=CmsAiSitePlan）/ section（单块就绪，DraftJson=CmsAiSectionDraft）/
/// done（草稿就绪）/ error。
/// </summary>
public sealed record CmsAiStreamEvent(
    string Kind,
    string Text,
    string? Model = null,
    string? DraftJson = null,
    int? Index = null,
    int? Total = null);

/// <summary>分块生成：站点骨架中的一个区块大纲（尚无微件）。</summary>
public sealed record CmsAiSectionPlan(
    string Title,
    string AnchorSlug,
    bool NavVisible,
    string LayoutPreset,
    string Brief);

/// <summary>分块生成：站点骨架（标题/描述/主题/区块大纲），一次小调用产出。</summary>
public sealed record CmsAiSitePlan(
    string Title,
    string Description,
    CmsThemeConfig Theme,
    List<CmsAiSectionPlan> Sections);

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

/// <summary>可选能力：流式生成整站草稿。仅 LLM 实现提供，占位设计器保持原接口不变。</summary>
public interface ICmsAiStreamingDesignerService
{
    Task<CmsAiSiteDraft> GenerateSiteStreamAsync(
        CmsAiGenerateSiteRequest request,
        Func<CmsAiStreamEvent, Task> onEvent,
        CancellationToken ct = default);

    /// <summary>
    /// 分块生成：先产出站点骨架（plan），再逐块产出区块（section），每块落库后前端即时刷新。
    /// 单块失败重试 2 次，仍失败则跳过（由端点用内置模板兜底）。
    /// </summary>
    Task GenerateSiteChunkedAsync(
        CmsAiGenerateSiteRequest request,
        Func<CmsAiStreamEvent, Task> onEvent,
        CancellationToken ct = default);
}
