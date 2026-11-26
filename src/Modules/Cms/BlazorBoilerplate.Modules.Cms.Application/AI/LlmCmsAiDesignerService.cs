using System.Text;
using System.Text.Json;
using BlazorBoilerplate.Modules.Cms.Application.DTOs;
using BlazorBoilerplate.Modules.Cms.Domain.ValueObjects;

namespace BlazorBoilerplate.Modules.Cms.Application.AI;

/// <summary>
/// 基于 Microsoft.Extensions.AI 的真实 LLM 官网设计器：
/// 生成时注入 CMS 硬约束（可用微件清单、布局骨架与叶子插槽、动画类型），
/// 输出严格符合 CmsAiSiteDraft 的 JSON；结构校验失败时带错误信息让模型自修复重试。
/// 配置未启用时模块不注册本实现，回退到 DefaultCmsAiDesignerService。
/// </summary>
public sealed class LlmCmsAiDesignerService : ICmsAiDesignerService, ICmsAiStreamingDesignerService
{
    private readonly ICmsAiChatClient _chat;
    private readonly CmsAiOptions _options;

    public LlmCmsAiDesignerService(ICmsAiChatClient chat, CmsAiOptions options)
    {
        _chat = chat;
        _options = options;
    }

    public async Task<CmsAiSiteDraft> GenerateSiteAsync(CmsAiGenerateSiteRequest request, CancellationToken ct = default)
    {
        var (system, user) = BuildSitePrompt(request);
        var draft = await GenerateWithRepairAsync(
            system, user, request.Model,
            ValidateSiteDraft,
            BuildSiteRepairPrompt,
            MapSite,
            ct: ct);
        return Normalize(draft, request);
    }

    public async Task<CmsAiSiteDraft> GenerateSiteStreamAsync(
        CmsAiGenerateSiteRequest request,
        Func<CmsAiStreamEvent, Task> onEvent,
        CancellationToken ct = default)
    {
        var (system, user) = BuildSitePrompt(request);
        var draft = await GenerateWithRepairAsync(
            system, user, request.Model,
            ValidateSiteDraft,
            BuildSiteRepairPrompt,
            MapSite,
            onEvent,
            ct);
        return Normalize(draft, request);
    }

    /// <summary>
    /// 分块生成：骨架一次小调用 → 逐块生成 widgets。
    /// 每次调用输出小、不截断；每块产出后立即 onEvent(section) 让端点落库并刷新画布。
    /// 单块失败重试 2 次，仍失败跳过（端点用内置模板兜底）。
    /// </summary>
    public async Task GenerateSiteChunkedAsync(
        CmsAiGenerateSiteRequest request,
        Func<CmsAiStreamEvent, Task> onEvent,
        CancellationToken ct = default)
    {
        var lang = string.IsNullOrWhiteSpace(request.Language) ? "zh-CN" : request.Language.Trim();
        var primary = string.IsNullOrWhiteSpace(request.PrimaryColor) ? "#0f6cbd" : request.PrimaryColor.Trim();
        var isEn = lang.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        // ---- 阶段 1：站点骨架（小输出，种子区块大纲）----
        var planSystem = BuildPlanSystemPrompt(lang, isEn);
        var planUser = $"""
            请为以下需求输出站点骨架 JSON（CmsAiSitePlan 结构）：
            - 品牌：{request.BrandName}
            - 行业：{request.Industry}
            - 风格：{(string.IsNullOrWhiteSpace(request.StylePrompt) ? "现代简洁企业官网" : request.StylePrompt.Trim())}
            - 主色：{primary}
            - 语言：{lang}
            要求：sections 4-7 个；首块 Hero（layoutPreset=single-column）、末块 Footer；
            title/description/theme 完整；每个 section 的 brief 用一句话说明该区块要放什么内容（供下一步生成微件）。
            只输出 JSON，不要解释。
            """;
        await onEvent(new CmsAiStreamEvent("status", "正在规划站点骨架…", _chat.ModelName));
        var planRaw = await _chat.CompleteAsync(
            [new CmsChatMessage("system", planSystem), new CmsChatMessage("user", planUser)],
            request.Model, ct);
        var plan = ParsePlan(ExtractJson(planRaw), request, primary, isEn);
        await onEvent(new CmsAiStreamEvent(
            "plan", $"骨架就绪：{plan.Sections.Count} 个区块", _chat.ModelName,
            DraftJson: JsonSerializer.Serialize(plan, CmsDtoMapper.JsonOptions)));

        // ---- 阶段 2：逐块生成 widgets ----
        var planned = plan.Sections;
        for (var i = 0; i < planned.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var spec = planned[i];
            await onEvent(new CmsAiStreamEvent(
                "status", $"正在生成第 {i + 1}/{planned.Count} 块：{spec.Title}", _chat.ModelName,
                Index: i + 1, Total: planned.Count));

            CmsAiSectionDraft? section = null;
            var maxAttempts = Math.Max(2, 3); // 首次 + 2 次重试
            for (var attempt = 0; attempt < maxAttempts && section is null; attempt++)
            {
                try
                {
                    var sectionSystem = BuildSectionSystemPrompt(lang);
                    var prior = planned.Take(i).Select(p => $"- {p.Title}（{p.LayoutPreset}）").ToArray();
                    var sectionUser = $"""
                        请为站点的第 {i + 1}/{planned.Count} 个区块生成 widgets（CmsAiSectionDraft 结构：title/anchorSlug/navVisible/layoutPreset/widgets）。
                        - 品牌：{request.BrandName}，行业：{request.Industry}
                        - 本区块标题：{spec.Title}
                        - 本区块锚点：{spec.AnchorSlug}
                        - 本区块用途：{spec.Brief}
                        - 本区块布局：{spec.LayoutPreset}
                        - 全站主题主色：{primary}
                        - 已完成区块：{(prior.Length == 0 ? "（这是首块）" : string.Join("\n", prior))}
                        - 语言：{lang}
                        规则：widgets 必须填满布局的所有叶子插槽；widgetType 只能用白名单里的值；
                        props 是原生 JSON 对象；每个 widget 的 style 里给一个动画；只输出 JSON。
                        """;
                    var raw = await _chat.CompleteAsync(
                        [new CmsChatMessage("system", sectionSystem), new CmsChatMessage("user", sectionUser)],
                        request.Model, ct);
                    var json = ExtractJson(raw);
                    var llmSection = JsonSerializer.Deserialize<LlmDraftSection>(json, CmsDtoMapper.JsonOptions);
                    if (llmSection is null) continue;
                    // 骨架字段由阶段一权威决定：模型只允许补 widgets。
                    // 之前允许模型覆盖 layoutPreset，会产出「骨架说两栏、实际落库用别的布局」的错位页面。
                    var candidate = LlmDraftMapper.ToSection(llmSection) with
                    {
                        Title = spec.Title,
                        AnchorSlug = spec.AnchorSlug,
                        LayoutPreset = spec.LayoutPreset,
                        NavVisible = spec.NavVisible
                    };
                    var errors = new List<string>();
                    ValidateSection(i, candidate, errors);
                    if (errors.Count == 0)
                        section = candidate;
                    else if (attempt == maxAttempts - 1)
                        await onEvent(new CmsAiStreamEvent("status", $"第 {i + 1} 块校验失败：{string.Join("；", errors.Take(2))}，将用模板兜底", _chat.ModelName, Index: i + 1, Total: planned.Count));
                }
                catch (JsonException) { }
                catch (InvalidOperationException) { }
            }

            section ??= FallbackSection(request, primary, isEn, i);

            await onEvent(new CmsAiStreamEvent(
                "section", $"第 {i + 1}/{planned.Count} 块就绪：{section.Title}", _chat.ModelName,
                DraftJson: JsonSerializer.Serialize(section, CmsDtoMapper.JsonOptions),
                Index: i + 1, Total: planned.Count));
        }
    }

    private static CmsAiSectionDraft FallbackSection(CmsAiGenerateSiteRequest request, string primary, bool isEn, int index)
    {
        var sections = CmsSiteTemplateLibrary.BuildSiteDraft("saas", request.BrandName, request.Industry,
            request.StylePrompt, primary, isEn).Sections;
        return sections[index % sections.Count];
    }

    private static CmsAiSitePlan ParsePlan(string json, CmsAiGenerateSiteRequest request, string primary, bool isEn)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var theme = new CmsThemeConfig(PrimaryColor: primary, AccentColor: primary);
        if (root.TryGetProperty("theme", out var t) && t.ValueKind == JsonValueKind.Object)
        {
            var llmTheme = t.Deserialize<LlmDraftTheme>(CmsDtoMapper.JsonOptions);
            if (llmTheme is not null)
            {
                var mapped = LlmDraftMapper.ToTheme(llmTheme);
                theme = mapped with { PrimaryColor = primary, AccentColor = primary };
            }
        }
        var sections = new List<CmsAiSectionPlan>();
        if (root.TryGetProperty("sections", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var secTitle = el.TryGetProperty("title", out var tv) ? tv.GetString() ?? string.Empty : string.Empty;
                var brief = el.TryGetProperty("brief", out var bv) ? bv.GetString() ?? string.Empty : string.Empty;
                var slug = el.TryGetProperty("anchorSlug", out var sv) ? sv.GetString() ?? string.Empty : string.Empty;
                var layout = el.TryGetProperty("layoutPreset", out var lv) ? lv.GetString() ?? "single-column" : "single-column";
                if (string.IsNullOrWhiteSpace(layout) || !LayoutZoneTable.ContainsKey(layout)) layout = "single-column";
                var nav = el.TryGetProperty("navVisible", out var nv) && nv.ValueKind == JsonValueKind.True;
                sections.Add(new CmsAiSectionPlan(
                    string.IsNullOrWhiteSpace(secTitle) ? (isEn ? "Section" : "区块") : secTitle,
                    string.IsNullOrWhiteSpace(slug) ? $"section-{sections.Count + 1}" : slug,
                    nav, layout, brief));
            }
        }
        if (sections.Count == 0)
            sections = CmsSiteTemplateLibrary.BuildSiteDraft("saas", request.BrandName, request.Industry,
                    request.StylePrompt, primary, isEn).Sections
                .Select(s => new CmsAiSectionPlan(s.Title, s.AnchorSlug, s.NavVisible, s.LayoutPreset, s.Title)).ToList();

        var title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
        var desc = root.TryGetProperty("description", out var descEl) ? descEl.GetString() : null;
        return new CmsAiSitePlan(
            string.IsNullOrWhiteSpace(title) ? $"{request.BrandName} 官方网站" : title!,
            string.IsNullOrWhiteSpace(desc) ? $"{request.BrandName} - {request.Industry}" : desc!,
            theme, sections);
    }

    private static string BuildPlanSystemPrompt(string language, bool isEn)
    {
        var jsonShape = """
            { "title":"站点标题", "description":"站点描述", "theme":{"primaryColor":"#hex","accentColor":"#hex"},
              "sections":[{"title":"区块标题","anchorSlug":"kebab-slug","navVisible":true,"layoutPreset":"布局名","brief":"该区块要放什么内容的一句话说明"}] }
            """;
        return $"""
            你是资深网站信息架构师，为 CMS 可视化设计器输出单页官网**骨架**（不是完整页面）。
            只输出严格合法 JSON（CmsAiSitePlan 结构，camelCase），形如：
            {jsonShape}
            可用布局：{string.Join("、", LayoutZoneTable.Keys)}。
            生成 4-7 个区块，首块 Hero、末块 Footer，相邻布局不重复。所有文案使用{language}。

            布局与微件匹配是硬约束（写错会导致页面挤压错位）：
            - {string.Join("、", CmsAiWidgetSchema.FullWidthWidgets)} 这些微件内部自带多列网格，只能放在 single-column 布局的 main 插槽。
            - two-column / sidebar / 三栏布局只能放 heading、text、image、button、divider、map 这类窄微件。
            - 每个区块的 brief 必须写清楚本区块用哪一个微件（从上面的微件清单选），以及它是整宽还是窄栏。
            - layoutPreset 必须原样取自「可用布局」，禁止自造布局名（如 hero-layout / footer-layout）。
            只输出 JSON，不要解释。
            """;
    }

    private static string BuildSectionSystemPrompt(string language)
    {
        var jsonShape = """
            { "title":"区块标题","anchorSlug":"slug","navVisible":true,"layoutPreset":"布局名",
              "widgets":[{"zoneId":"插槽","widgetType":"cms.xxx","title":"微件名","props":{},"style":{"animation":"fade-up"}}] }
            """;
        var widgetLines = string.Join("\n", CmsAiWidgetSchema.All.Select(w => $"- `{w.Type}`：{w.Description}；props 示例：{w.ExamplePropsJson}"));
        var layoutLines = string.Join("\n", LayoutZoneTable.Select(kv => $"- `{kv.Key}`：{string.Join(", ", kv.Value.Select(z => z.Id))}"));
        var animList = string.Join("、", CmsAiWidgetSchema.AnimationTypes);
        return $"""
            你是 CMS 官网区块内容设计师，只输出一个严格合法的 JSON 对象（CmsAiSectionDraft 结构，camelCase），形如：
            {jsonShape}
            可用微件类型（widgetType 只能取这些值）：
            {widgetLines}
            可用布局与叶子插槽（括号内为栅格宽度）：
            {layoutLines}
            可用动画：{animList}
            规则：widgets 必须填满布局的全部叶子插槽（一插槽一微件）；props 是原生 JSON 对象不要写成字符串；
            {string.Join("、", CmsAiWidgetSchema.FullWidthWidgets)} 只能放进 12 栅格插槽，绝不能放进 6/8/4 栅格插槽；
            cms.features 的 items[].icon 只能取：{string.Join("、", CmsAiWidgetSchema.Icons)}；
            style 里给动画；图片仅用 https://images.pexels.com/photos/<id>/pexels-photo-<id>.jpeg?auto=compress&cs=tinysrgb&w=1200；
            文案使用{language}，禁止占位符。只输出 JSON，不要解释。
            """;
    }

    private static (string System, string User) BuildSitePrompt(CmsAiGenerateSiteRequest request)
    {
        var lang = string.IsNullOrWhiteSpace(request.Language) ? "zh-CN" : request.Language.Trim();
        var system = BuildSystemPrompt(lang);
        var user = $"""
            请为以下需求生成完整的单页官网草稿 JSON：
            - 品牌名称：{request.BrandName}
            - 所属行业：{request.Industry}
            - 风格补充说明：{(string.IsNullOrWhiteSpace(request.StylePrompt) ? "（无，使用现代简洁企业官网风格）" : request.StylePrompt.Trim())}
            - 主色：{(string.IsNullOrWhiteSpace(request.PrimaryColor) ? "由你根据行业决定" : request.PrimaryColor.Trim())}
            - 语言：{lang}（全部文案使用该语言；若为 en-US 则全英文）
            只输出一个 JSON 对象（CmsAiSiteDraft 结构），不要任何解释。
            """;
        return (system, user);
    }

    private static string BuildSiteRepairPrompt(List<string> errors) => $"""
        你上一次输出的 JSON 存在以下问题：
        {string.Join("\n", errors.Take(8))}

        请重新输出修正后的完整 CmsAiSiteDraft JSON（同样只输出 JSON）。
        """;

    public async Task<CmsAiSectionDraft> GenerateSectionAsync(CmsAiGenerateSectionRequest request, CancellationToken ct = default)
    {
        var system = BuildSystemPrompt(request.Language);
        var user = $"""
            请为站点的一个新区块生成内容。只输出一个 JSON 对象（CmsAiSectionDraft 结构，即含 title/anchorSlug/navVisible/layoutPreset/widgets 的对象），不要数组也不要解释。
            - 区块需求描述：{request.Prompt}
            - 目标锚点 slug：{(string.IsNullOrWhiteSpace(request.TargetSlug) ? "由你根据内容拟定" : request.TargetSlug.Trim())}
            - 语言：{request.Language}
            """;

        return await GenerateWithRepairAsync(
            system, user, null,
            ValidateSectionDraft,
            errors => $"你上一次输出的 JSON 存在以下问题：\n{string.Join("\n", errors.Take(8))}\n请重新输出修正后的完整 CmsAiSectionDraft JSON（只输出 JSON）。",
            json => JsonSerializer.Deserialize<LlmDraftSection>(json, CmsDtoMapper.JsonOptions) is { } s
                ? LlmDraftMapper.ToSection(s)
                : null,
            ct: ct);
    }

    private static CmsAiSiteDraft? MapSite(string json)
    {
        var draft = JsonSerializer.Deserialize<LlmDraftSite>(json, CmsDtoMapper.JsonOptions);
        return draft is null ? null : LlmDraftMapper.ToSite(draft);
    }

    public async Task<CmsAiWidgetDraft> RefineWidgetAsync(CmsAiRefineWidgetRequest request, CancellationToken ct = default)
    {
        var system = "你是 CMS 微件内容润色助手。根据指令改写微件属性 JSON，保持原有字段结构与类型不变，只输出修改后的 JSON 对象。";
        var user = $"""
            微件类型：{request.WidgetType}
            当前属性 JSON：
            {request.CurrentPropsJson}
            润色指令：{request.Instruction}
            语言：{request.Language}
            只输出修改后的完整属性 JSON。
            """;

        var raw = await _chat.CompleteAsync(
            [new CmsChatMessage("system", system), new CmsChatMessage("user", user)], ct: ct);
        var json = ExtractJson(raw);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("根节点不是对象");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"AI 返回的属性 JSON 无法解析：{ex.Message}");
        }
        return new CmsAiWidgetDraft(ZoneId: "main", WidgetType: request.WidgetType, Title: "润色后的微件", PropsJson: json);
    }

    private static string BuildSystemPrompt(string language)
    {
        var widgetLines = CmsAiWidgetSchema.All
            .Select(w => $"- `{w.Type}`（{w.Name}）：{w.Description}；props 示例：{w.ExamplePropsJson}")
            .ToArray();
        var layoutLines = string.Join("\n", LayoutZoneTable.Select(kv =>
            $"- `{kv.Key}`：叶子插槽 ZoneId = {string.Join(", ", kv.Value.Select(z => z.Id))}"));
        var animList = string.Join("、", CmsAiWidgetSchema.AnimationTypes);

        return $$"""
            你是资深的单页官网设计师，为 BlazorBoilerplate CMS 可视化设计器生成整站草稿。
            你必须输出**严格合法的 JSON**（CmsAiSiteDraft 结构），字段全部使用 camelCase：

            ```
            {
              "title": "站点标题",
              "description": "站点描述",
              "theme": { "primaryColor": "#xxxxxx", "accentColor": "#xxxxxx" },
              "sections": [
                { "title": "区块标题", "anchorSlug": "kebab-slug", "navVisible": true, "layoutPreset": "布局名",
                  "widgets": [ { "zoneId": "插槽", "widgetType": "cms.xxx", "title": "微件名", "props": { ... }, "style": { ... } } ] }
              ],
              "providerNote": "生成说明"
            }
            ```
            重要：props 与 style 必须是**原生 JSON 对象**，绝不要写成字符串或转义后的文本。

            ## 可用微件类型（widgetType 只能取这些值）
            {{string.Join("\n", widgetLines)}}

            ## 可用布局（layoutPreset 只能取这些名称，且 zoneId 必须来自对应布局的叶子插槽）
            {{layoutLines}}

            ## 结构与设计规则
            1. 整站 5-9 个区块；首个区块是首页 Hero（cms.hero 放 main），最后一个区块含 cms.footer。
            2. 相邻区块不要使用相同 layoutPreset；全站至少使用 2 种不同布局。
            3. 每个区块 1-3 个微件，微件必须填满区块布局的全部叶子插槽（一个插槽一个微件）。
            4. 每个微件的 props 必须符合所选 widgetType 的 props 结构，不得发明不存在的字段。
            5. 图片 URL 仅可使用 https://images.pexels.com/photos/<id>/pexels-photo-<id>.jpeg?auto=compress&cs=tinysrgb&w=1200 格式的真实图片。
            6. 所有文案使用{{language}}撰写，内容贴合品牌与行业，禁止出现占位符（如「待补充」）。
            7. {{string.Join("、", CmsAiWidgetSchema.FullWidthWidgets)}} 必须放在 12 栅格插槽；半栏只能放 heading/text/image/button/divider/map。
            8. cms.features 的 items[].icon 只能取：{{string.Join("、", CmsAiWidgetSchema.Icons)}}，其他名字渲染为空白。

            ## 动画（每个微件通过 widgets[].style 对象提供，不写入 props）
            style 的值形如 {"animation":"fade-up"}。
            可用动画：{{animList}}。遵循：左右相邻插槽用 slide-right（左列）与 slide-left（右列）形成对向呼应，中央主区用 zoom-in；同一区块内动画不重复。
            """;
    }

    private sealed record LayoutZoneSpec(string Id, int Span);

    /// <summary>布局 → 叶子插槽及栅格宽度。宽度用于阻止整宽微件被塞进半栏。</summary>
    private static readonly Dictionary<string, LayoutZoneSpec[]> LayoutZoneTable = new()
    {
        ["single-column"] = [new("main", 12)],
        ["two-column"] = [new("left", 6), new("right", 6)],
        ["three-column"] = [new("col-1", 4), new("col-2", 4), new("col-3", 4)],
        ["sidebar"] = [new("sidebar", 4), new("main", 8)],
        [CmsBuiltInLayoutNames.NestedExample] = [new("sidebar", 4), new("main", 8), new("inner-left", 6), new("inner-right", 6)],
        [CmsBuiltInLayoutNames.MosaicCollage] = [new("mz-feature", 7), new("mz-aside", 5), new("mz-band", 12)],
        [CmsBuiltInLayoutNames.MosaicNested] = [new("mz-cover", 12), new("mz-work-1", 4), new("mz-work-2", 4), new("mz-work-3", 4)],
        [CmsBuiltInLayoutNames.MosaicStairs] = [new("mz-step-a", 4), new("mz-step-b", 8), new("mz-step-c", 8), new("mz-step-d", 4)],
        [CmsBuiltInLayoutNames.SignalStage] = [new("sg-left", 3), new("sg-stage", 6), new("sg-right", 3)],
        [CmsBuiltInLayoutNames.SignalNested] = [new("sg-head", 12), new("sg-track-a", 5), new("sg-track-b", 7)],
        [CmsBuiltInLayoutNames.AtlasSpread] = [new("at-text-left", 5), new("at-visual-right", 7), new("at-visual-left", 7), new("at-text-right", 5)],
        [CmsBuiltInLayoutNames.AtlasNested] = [new("at-map", 12), new("at-card-1", 3), new("at-card-2", 3), new("at-card-3", 3), new("at-card-4", 3)]
    };

    private async Task<T> GenerateWithRepairAsync<T>(
        string system, string user, string? model,
        Func<T, List<string>> validate,
        Func<List<string>, string> buildRepairPrompt,
        Func<string, T?> deserialize,
        Func<CmsAiStreamEvent, Task>? onEvent = null,
        CancellationToken ct = default)
    {
        var messages = new List<CmsChatMessage>
        {
            new("system", system),
            new("user", user)
        };
        List<string> errors = ["（首轮生成）"];
        var maxRounds = Math.Max(1, _options.MaxRepairRounds);

        for (var attempt = 0; attempt < maxRounds; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (onEvent is not null)
                await onEvent(new CmsAiStreamEvent("status", $"第 {attempt + 1}/{maxRounds} 轮生成中（模型 {_chat.ModelName}）…", _chat.ModelName));

            var raw = await CompleteOrStreamAsync(messages, model, onEvent, ct);
            var json = ExtractJson(raw);
            var truncated = IsTruncatedJson(json);
            T? draft;
            try
            {
                draft = deserialize(json);
            }
            catch (JsonException ex)
            {
                // 截断是「输出 token 用尽」，不是模型写错；修复提示必须明确要求精简，
                // 否则模型在下一轮继续输出长 JSON，只会再次被截断。
                errors = [truncated
                    ? $"输出被截断（已达 token 上限）：{ex.Message}。请大幅精简内容：区块数控制在 5 个以内，每个区块 1-2 个微件，props 只保留必要字段，确保 JSON 完整闭合。"
                    : $"JSON 解析失败：{ex.Message}。请输出完整且合法的 JSON。"];
                draft = default;
            }

            if (draft is not null)
            {
                errors = validate(draft);
                if (errors.Count == 0)
                {
                    if (onEvent is not null)
                        await onEvent(new CmsAiStreamEvent("status", "结构校验通过，正在整理草稿…", _chat.ModelName));
                    return draft;
                }
            }

            if (onEvent is not null)
                await onEvent(new CmsAiStreamEvent("status", $"第 {attempt + 1} 轮未通过校验：{string.Join("；", errors.Take(3))}", _chat.ModelName));

            messages.Add(new CmsChatMessage("assistant", json));
            messages.Add(new CmsChatMessage("user", buildRepairPrompt(errors)));
        }

        throw new InvalidOperationException(
            $"AI 生成的草稿连续 {maxRounds} 次未通过结构校验，最后问题：{string.Join("；", errors.Take(5))}");
    }

    private async Task<string> CompleteOrStreamAsync(
        IReadOnlyList<CmsChatMessage> messages,
        string? model,
        Func<CmsAiStreamEvent, Task>? onEvent,
        CancellationToken ct)
    {
        if (onEvent is null)
            return await _chat.CompleteAsync(messages, model, ct);

        var buffer = new StringBuilder();
        await _chat.StreamAsync(messages, async chunk =>
        {
            buffer.Append(chunk);
            await onEvent(new CmsAiStreamEvent("delta", chunk, _chat.ModelName));
        }, model, ct);
        return buffer.ToString();
    }

    private static List<string> ValidateSiteDraft(CmsAiSiteDraft draft)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(draft.Title)) errors.Add("缺少 title");
        if (draft.Sections.Count is < 1 or > 12) errors.Add($"sections 数量异常：{draft.Sections.Count}");
        for (var i = 0; i < draft.Sections.Count; i++)
            ValidateSection(i, draft.Sections[i], errors);
        return errors;
    }

    private static List<string> ValidateSectionDraft(CmsAiSectionDraft section)
    {
        var errors = new List<string>();
        ValidateSection(0, section, errors);
        return errors;
    }

    private static void ValidateSection(int index, CmsAiSectionDraft section, List<string> errors)
    {
        var n = index + 1;
        if (string.IsNullOrWhiteSpace(section.Title)) errors.Add($"区块{n}缺少 title");
        if (string.IsNullOrWhiteSpace(section.AnchorSlug)) errors.Add($"区块{n}缺少 anchorSlug");
        if (section.Widgets.Count is < 1 or > 8) errors.Add($"区块{n} 微件数量异常：{section.Widgets.Count}");

        var knownZones = LayoutZoneTable.TryGetValue(section.LayoutPreset?.Trim() ?? string.Empty, out var zones)
            ? zones : null;
        // 之前未知布局会跳过全部插槽校验，落库时再静默回退到第一个布局，是错位页面的根源。
        if (knownZones is null)
            errors.Add($"区块{n} 布局「{section.LayoutPreset}」不在可用清单（可选：{string.Join("、", LayoutZoneTable.Keys)}）");

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var w = 0; w < section.Widgets.Count; w++)
        {
            var widget = section.Widgets[w];
            var wn = $"{n}.{w + 1}";
            if (string.IsNullOrWhiteSpace(widget.WidgetType) || CmsAiWidgetSchema.All.All(x => x.Type != widget.WidgetType))
                errors.Add($"微件{wn} widgetType 非法：{widget.WidgetType}");
            if (string.IsNullOrWhiteSpace(widget.ZoneId))
                errors.Add($"微件{wn}缺少 zoneId");
            else if (knownZones is not null)
            {
                var zone = knownZones.FirstOrDefault(z => string.Equals(z.Id, widget.ZoneId.Trim(), StringComparison.OrdinalIgnoreCase));
                if (zone is null)
                    errors.Add($"微件{wn} zoneId「{widget.ZoneId}」不属于布局「{section.LayoutPreset}」（可选：{string.Join(", ", knownZones.Select(z => z.Id))}）");
                else if (CmsAiWidgetSchema.FullWidthWidgets.Contains(widget.WidgetType.Trim(), StringComparer.OrdinalIgnoreCase) && zone.Span < 12)
                    errors.Add($"微件{wn}「{widget.WidgetType}」需要 12 栅格整宽插槽，「{zone.Id}」只有 {zone.Span} 栅格；请改用 single-column 的 main 插槽");
            }
            if (string.IsNullOrWhiteSpace(widget.PropsJson))
                errors.Add($"微件{wn}缺少 propsJson");
            else
            {
                JsonDocument? doc = null;
                try
                {
                    doc = JsonDocument.Parse(widget.PropsJson);
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                        errors.Add($"微件{wn} propsJson 根节点必须是对象");
                }
                catch (JsonException ex)
                {
                    errors.Add($"微件{wn} propsJson 不是合法 JSON：{ex.Message}");
                }
                finally
                {
                    if (doc is not null && string.Equals(widget.WidgetType, "cms.features", StringComparison.OrdinalIgnoreCase))
                        ValidateFeatureIcons(doc.RootElement, wn, errors);
                    doc?.Dispose();
                }
            }
            if (!used.Add(widget.ZoneId))
                errors.Add($"区块{n} 中 zoneId「{widget.ZoneId}」出现多个微件（允许，但请确认是刻意的堆叠）");
        }
    }

    private static void ValidateFeatureIcons(JsonElement props, string widgetLabel, List<string> errors)
    {
        if (!props.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("icon", out var iconEl)) continue;
            var icon = iconEl.GetString();
            if (string.IsNullOrWhiteSpace(icon)) continue;
            if (!CmsAiWidgetSchema.Icons.Contains(icon.Trim(), StringComparer.OrdinalIgnoreCase))
                errors.Add($"微件{widgetLabel} 图标「{icon}」不在白名单（可选：{string.Join("、", CmsAiWidgetSchema.Icons)}）");
        }
    }

    private CmsAiSiteDraft Normalize(CmsAiSiteDraft draft, CmsAiGenerateSiteRequest request)
    {
        var primary = request.PrimaryColor;
        if (string.IsNullOrWhiteSpace(primary))
            primary = draft.Theme is null || string.IsNullOrWhiteSpace(draft.Theme.PrimaryColor)
                ? "#0f6cbd" : draft.Theme.PrimaryColor;

        var theme = draft.Theme ?? new CmsThemeConfig(PrimaryColor: primary, AccentColor: primary);
        if (string.IsNullOrWhiteSpace(theme.PrimaryColor)) theme = theme with { PrimaryColor = primary };
        if (string.IsNullOrWhiteSpace(theme.AccentColor)) theme = theme with { AccentColor = primary };

        return draft with
        {
            Title = string.IsNullOrWhiteSpace(draft.Title) ? $"{request.BrandName} 官方网站" : draft.Title,
            Theme = theme,
            ProviderNote = $"由 LLM 生成（Microsoft.Extensions.AI，model={_chat.ModelName}）。"
        };
    }

    /// <summary>JSON 是否因 token 用尽而截断（大括号未闭合 / 停在对象中途）。</summary>
    private static bool IsTruncatedJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;
        var trimmed = json.TrimEnd();
        if (trimmed.Length == 0) return true;
        // 完整 JSON 必以 } 或 ] 收尾；以逗号、冒号、引号或字面量中途结尾都视为截断。
        if (trimmed[^1] is '}' or ']') return false;
        return true;
    }

    /// <summary>剥离 ```json 围栏，定位首个平衡的 JSON 对象。</summary>
    private static string ExtractJson(string raw)
    {
        var text = raw.Trim();
        var fenceStart = text.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var firstLineEnd = text.IndexOf('\n', fenceStart);
            if (firstLineEnd > 0) text = text[(firstLineEnd + 1)..];
            var fenceEnd = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceEnd > 0) text = text[..fenceEnd];
            text = text.Trim();
        }
        var open = text.AsSpan().IndexOfAny('{', '[');
        if (open < 0) return text;
        var openChar = text[open];
        var closeChar = openChar == '{' ? '}' : ']';
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') inString = !inString;
            else if (!inString)
            {
                if (c == openChar) depth++;
                else if (c == closeChar && --depth == 0) return text[open..(i + 1)];
            }
        }
        return text[open..].Trim();
    }
}
