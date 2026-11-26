using System.Net.Http.Json;
using System.Text.Json;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Cms.Frontend.Models;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Services;

public sealed class CmsApiClient(HttpClient http) : IClientService
{
    public async Task<CmsPublishedSnapshot?> GetPublicSiteAsync(string? siteKey = null, string? culture = null, CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(siteKey)) qs.Add($"siteKey={Uri.EscapeDataString(siteKey)}");
        if (!string.IsNullOrWhiteSpace(culture)) qs.Add($"culture={Uri.EscapeDataString(culture)}");
        var url = qs.Count == 0 ? "api/cms/public/site" : $"api/cms/public/site?{string.Join("&", qs)}";
        using var resp = await http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
            return null;
        return await resp.Content.ReadFromJsonAsync<CmsPublishedSnapshot>(CmsJson.Options, ct);
    }

    public async Task<bool> SubmitLeadAsync(SubmitCmsLeadRequest request, CancellationToken ct = default)
    {
        using var resp = await http.PostAsJsonAsync("api/cms/public/leads", request, CmsJson.Options, ct);
        return resp.IsSuccessStatusCode;
    }

    public Task<List<CmsSiteDto>?> ListSitesAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<CmsSiteDto>>("api/cms/sites", CmsJson.Options, ct);

    public async Task<CmsSiteDto> CreateSiteAsync(UpsertCmsSiteRequest req, CancellationToken ct = default)
    {
        var resp = await http.PostAsJsonAsync("api/cms/sites", req, CmsJson.Options, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CmsSiteDto>(CmsJson.Options, ct))!;
    }

    public async Task<CmsSiteDto> UpdateSiteAsync(Guid siteId, UpsertCmsSiteRequest req, CancellationToken ct = default)
    {
        var resp = await http.PutAsJsonAsync($"api/cms/sites/{siteId}", req, CmsJson.Options, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CmsSiteDto>(CmsJson.Options, ct))!;
    }

    public async Task ReorderSectionsAsync(Guid siteId, List<Guid> sectionIds, CancellationToken ct = default)
    {
        var resp = await http.PutAsJsonAsync($"api/cms/sites/{siteId}/sections/reorder", new { sectionIds }, CmsJson.Options, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task PublishSiteAsync(Guid siteId, CancellationToken ct = default)
    {
        var resp = await http.PostAsync($"api/cms/sites/{siteId}/publish", null, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task UnpublishSiteAsync(Guid siteId, CancellationToken ct = default)
    {
        var resp = await http.PostAsync($"api/cms/sites/{siteId}/unpublish", null, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task DeleteSiteAsync(Guid siteId, CancellationToken ct = default)
    {
        var resp = await http.DeleteAsync($"api/cms/sites/{siteId}", ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<CmsSiteDto> CloneSiteAsync(Guid siteId, string? name = null, string? siteKey = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(name)) query.Add($"name={Uri.EscapeDataString(name)}");
        if (!string.IsNullOrWhiteSpace(siteKey)) query.Add($"siteKey={Uri.EscapeDataString(siteKey)}");
        var url = query.Count == 0
            ? $"api/cms/sites/{siteId}/clone"
            : $"api/cms/sites/{siteId}/clone?{string.Join("&", query)}";
        var resp = await http.PostAsync(url, null, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CmsSiteDto>(CmsJson.Options, ct))!;
    }

    public Task<CmsSiteDesignDto?> GetDesignAsync(Guid siteId, string? culture = null, CancellationToken ct = default)
    {
        var url = string.IsNullOrWhiteSpace(culture)
            ? $"api/cms/sites/{siteId}/design"
            : $"api/cms/sites/{siteId}/design?culture={Uri.EscapeDataString(culture)}";
        return http.GetFromJsonAsync<CmsSiteDesignDto>(url, CmsJson.Options, ct);
    }

    public async Task<CmsSiteDesignDto> CloneCultureAsync(Guid siteId, string sourceCulture, string targetCulture, CancellationToken ct = default)
    {
        var resp = await http.PostAsync(
            $"api/cms/sites/{siteId}/cultures/clone?source={Uri.EscapeDataString(sourceCulture)}&target={Uri.EscapeDataString(targetCulture)}",
            null, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CmsSiteDesignDto>(CmsJson.Options, ct))!;
    }

    public async Task<CmsSiteDto> UpdateThemeAsync(Guid siteId, CmsThemeConfig theme, CancellationToken ct = default)
    {
        var resp = await http.PutAsJsonAsync($"api/cms/sites/{siteId}/theme", theme, CmsJson.Options, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CmsSiteDto>(CmsJson.Options, ct))!;
    }

    public Task<List<CmsLayoutDto>?> ListLayoutsAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<CmsLayoutDto>>("api/cms/layouts", CmsJson.Options, ct);

    public async Task<CmsLayoutDto> CreateLayoutAsync(UpsertCmsLayoutRequest req, CancellationToken ct = default)
    {
        var resp = await http.PostAsJsonAsync("api/cms/layouts", req, CmsJson.Options, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CmsLayoutDto>(CmsJson.Options, ct))!;
    }

    public async Task<CmsLayoutDto> UpdateLayoutAsync(Guid id, UpsertCmsLayoutRequest req, CancellationToken ct = default)
    {
        var resp = await http.PutAsJsonAsync($"api/cms/layouts/{id}", req, CmsJson.Options, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CmsLayoutDto>(CmsJson.Options, ct))!;
    }

    public async Task DeleteLayoutAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await http.DeleteAsync($"api/cms/layouts/{id}", ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<CmsSectionDto> CreateSectionAsync(Guid siteId, UpsertCmsSectionRequest req, CancellationToken ct = default)
    {
        var resp = await http.PostAsJsonAsync($"api/cms/sites/{siteId}/sections", req, CmsJson.Options, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CmsSectionDto>(CmsJson.Options, ct))!;
    }

    public async Task<CmsSectionDto> UpdateSectionAsync(Guid id, UpsertCmsSectionRequest req, CancellationToken ct = default)
    {
        var resp = await http.PutAsJsonAsync($"api/cms/sections/{id}", req, CmsJson.Options, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CmsSectionDto>(CmsJson.Options, ct))!;
    }

    public async Task DeleteSectionAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await http.DeleteAsync($"api/cms/sections/{id}", ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<CmsWidgetDto> CreateWidgetAsync(UpsertCmsWidgetRequest req, CancellationToken ct = default)
    {
        var resp = await http.PostAsJsonAsync("api/cms/widgets", req, CmsJson.Options, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CmsWidgetDto>(CmsJson.Options, ct))!;
    }

    public async Task<CmsWidgetDto> UpdateWidgetAsync(Guid id, UpsertCmsWidgetRequest req, CancellationToken ct = default)
    {
        var resp = await http.PutAsJsonAsync($"api/cms/widgets/{id}", req, CmsJson.Options, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CmsWidgetDto>(CmsJson.Options, ct))!;
    }

    public async Task<CmsWidgetDto> MoveWidgetAsync(Guid id, MoveCmsWidgetRequest req, CancellationToken ct = default)
    {
        var resp = await http.PutAsJsonAsync($"api/cms/widgets/{id}/move", req, CmsJson.Options, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CmsWidgetDto>(CmsJson.Options, ct))!;
    }

    public async Task DeleteWidgetAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await http.DeleteAsync($"api/cms/widgets/{id}", ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<CmsAiModelsDto> AiModelsAsync(CancellationToken ct = default)
    {
        var resp = await http.GetAsync("api/cms/ai/models", ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CmsAiModelsDto>(CmsJson.Options, ct))!;
    }

    public async Task<CmsAiSiteDraft> AiGenerateSiteAsync(CmsAiGenerateSiteRequest req, CancellationToken ct = default)
    {
        var resp = await http.PostAsJsonAsync("api/cms/ai/generate-site", req, CmsJson.Options, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CmsAiSiteDraft>(CmsJson.Options, ct))!;
    }

    /// <summary>流式生成：逐条接收 SSE 事件，前端可实时显示模型输出与阶段状态。</summary>
    public async Task AiGenerateSiteStreamAsync(
        CmsAiGenerateSiteRequest req,
        Func<CmsAiStreamEventDto, Task> onEvent,
        CancellationToken ct = default)
    {
        // 关键：必须用 ResponseHeadersRead。PostAsJsonAsync 默认 ResponseContentRead，
        // 会等整个 SSE 响应体读完才返回，逐段 flush 的事件会被 HttpClient 缓存在内存里，
        // 前端直到生成结束才能一次性看到内容，等价于没有流式。
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/cms/ai/generate-site/stream")
        {
            Content = JsonContent.Create(req, options: CmsJson.Options)
        };
        using var resp = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var json = line[5..].Trim();
            if (json.Length == 0) continue;
            var evt = JsonSerializer.Deserialize<CmsAiStreamEventDto>(json, CmsJson.Options);
            if (evt is null) continue;
            await onEvent(evt);
            if (evt.Kind is "done" or "error") break;
        }
    }

    /// <summary>分块生成：每块落库后服务端推 section + refreshed，前端逐块刷新画布。</summary>
    public async Task AiGenerateSiteChunkedAsync(
        CmsAiChunkedGenerateRequest req,
        Func<CmsAiStreamEventDto, Task> onEvent,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/cms/ai/generate-site/chunked")
        {
            Content = JsonContent.Create(req, options: CmsJson.Options)
        };
        using var resp = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var json = line[5..].Trim();
            if (json.Length == 0) continue;
            var evt = JsonSerializer.Deserialize<CmsAiStreamEventDto>(json, CmsJson.Options);
            if (evt is null) continue;
            await onEvent(evt);
            if (evt.Kind is "done" or "error") break;
        }
    }

    public async Task<CmsSiteDesignDto> AiApplySiteAsync(Guid siteId, CmsAiSiteDraft draft, string? culture = null, CancellationToken ct = default)
    {
        var url = string.IsNullOrWhiteSpace(culture)
            ? $"api/cms/ai/apply-site/{siteId}"
            : $"api/cms/ai/apply-site/{siteId}?culture={Uri.EscapeDataString(culture)}";
        var resp = await http.PostAsJsonAsync(url, draft, CmsJson.Options, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CmsSiteDesignDto>(CmsJson.Options, ct))!;
    }

    /// <summary>生成后微调：让 AI 重新生成一个区块草稿（不落库）。</summary>
    public async Task<CmsAiSectionDraft> AiGenerateSectionAsync(CmsAiGenerateSectionRequest req, CancellationToken ct = default)
    {
        var resp = await http.PostAsJsonAsync("api/cms/ai/generate-section", req, CmsJson.Options, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CmsAiSectionDraft>(CmsJson.Options, ct))!;
    }

    /// <summary>生成后微调：让 AI 按指令改写选中微件的 props（不落库）。</summary>
    public async Task<CmsAiWidgetDraft> AiRefineWidgetAsync(CmsAiRefineWidgetRequest req, CancellationToken ct = default)
    {
        var resp = await http.PostAsJsonAsync("api/cms/ai/refine-widget", req, CmsJson.Options, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CmsAiWidgetDraft>(CmsJson.Options, ct))!;
    }

    /// <summary>把 AI 区块草稿追加进当前站点，返回最新设计。</summary>
    public async Task<CmsSiteDesignDto> AiAppendSectionAsync(Guid siteId, CmsAiSectionDraft draft, string? culture = null, CancellationToken ct = default)
    {
        var url = string.IsNullOrWhiteSpace(culture)
            ? $"api/cms/ai/append-section/{siteId}"
            : $"api/cms/ai/append-section/{siteId}?culture={Uri.EscapeDataString(culture)}";
        var resp = await http.PostAsJsonAsync(url, draft, CmsJson.Options, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CmsSiteDesignDto>(CmsJson.Options, ct))!;
    }

    public Task<List<CmsLeadDto>?> ListLeadsAsync(Guid siteId, CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<CmsLeadDto>>($"api/cms/sites/{siteId}/leads", CmsJson.Options, ct);

    public async Task UpdateLeadStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        var resp = await http.PutAsJsonAsync($"api/cms/leads/{id}/status", new { status }, CmsJson.Options, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task DeleteLeadAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await http.DeleteAsync($"api/cms/leads/{id}", ct);
        resp.EnsureSuccessStatusCode();
    }
}
