using System.Text;
using System.Text.Json;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Cms.Application.AI;
using BlazorBoilerplate.Modules.Cms.Application.DTOs;
using BlazorBoilerplate.Modules.Cms.Application.Services;
using BlazorBoilerplate.Modules.Cms.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Cms;

public sealed class CmsEndpoints(IServiceProvider moduleServices)
{
    private static readonly JsonSerializerOptions SseJson = new(JsonSerializerDefaults.Web);

    public void Map(IModuleEndpointBuilder builder)
    {
        // ---- 公开单页官网接口（无需登录）----
        builder.Map("/api/cms/public/site", "GET", "anonymous", async ctx => await WithScope(ctx, async s =>
        {
            var siteKey = ctx.Request.Query["siteKey"].FirstOrDefault();
            var culture = ctx.Request.Query["culture"].FirstOrDefault();
            var snapshot = await s.ServiceProvider.GetRequiredService<CmsSiteAppService>().GetPublicSnapshotAsync(siteKey, culture, ctx.RequestAborted);
            if (snapshot is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                await ctx.Response.WriteAsJsonAsync(new { error = "站点尚未发布或不存在。" }, ctx.RequestAborted);
                return;
            }
            await ctx.Response.WriteAsJsonAsync(snapshot, ctx.RequestAborted);
        }));

        builder.Map("/api/cms/public/leads", "POST", "anonymous", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<SubmitCmsLeadRequest>(ctx);
            var ip = ctx.Connection.RemoteIpAddress?.ToString();
            var created = await s.ServiceProvider.GetRequiredService<CmsLeadAppService>().SubmitAsync(req, ip, ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(created, ctx.RequestAborted);
        }));

        // ---- 站点管理 ----
        builder.Map("/api/cms/sites", "GET", "cms.site.read", async ctx => await WithScope(ctx, async s =>
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsSiteAppService>().ListAsync(ctx.RequestAborted), ctx.RequestAborted)));

        builder.Map("/api/cms/sites", "POST", "cms.site.write", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<UpsertCmsSiteRequest>(ctx);
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsSiteAppService>().CreateAsync(req, ctx.RequestAborted), ctx.RequestAborted);
        }));

        builder.Map("/api/cms/sites/{id}", "PUT", "cms.site.write", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<UpsertCmsSiteRequest>(ctx);
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsSiteAppService>().UpdateAsync(GetGuid(ctx, "id"), req, ctx.RequestAborted), ctx.RequestAborted);
        }));

        builder.Map("/api/cms/sites/{id}", "DELETE", "cms.site.write", async ctx => await WithScope(ctx, async s =>
        {
            await s.ServiceProvider.GetRequiredService<CmsSiteAppService>().DeleteAsync(GetGuid(ctx, "id"), ctx.RequestAborted);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }));

        builder.Map("/api/cms/sites/{id}/clone", "POST", "cms.site.write", async ctx => await WithScope(ctx, async s =>
        {
            var name = ctx.Request.Query["name"].FirstOrDefault();
            var siteKey = ctx.Request.Query["siteKey"].FirstOrDefault();
            var cloned = await s.ServiceProvider.GetRequiredService<CmsSiteAppService>().CloneSiteAsync(GetGuid(ctx, "id"), name, siteKey, ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(cloned, ctx.RequestAborted);
        }));

        builder.Map("/api/cms/sites/{id}/design", "GET", "cms.site.read", async ctx => await WithScope(ctx, async s =>
        {
            var culture = ctx.Request.Query["culture"].FirstOrDefault();
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsSiteAppService>().GetDesignAsync(GetGuid(ctx, "id"), culture, ctx.RequestAborted), ctx.RequestAborted);
        }));

        builder.Map("/api/cms/sites/{id}/languages", "PUT", "cms.site.write", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<UpdateSiteLanguagesRequest>(ctx);
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsSiteAppService>().UpdateLanguagesAsync(GetGuid(ctx, "id"), req, ctx.RequestAborted), ctx.RequestAborted);
        }));

        builder.Map("/api/cms/sites/{id}/cultures/clone", "POST", "cms.site.write", async ctx => await WithScope(ctx, async s =>
        {
            var source = ctx.Request.Query["source"].FirstOrDefault() ?? string.Empty;
            var target = ctx.Request.Query["target"].FirstOrDefault() ?? throw new InvalidOperationException("目标语言不能为空。");
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsSiteAppService>().CloneCultureAsync(GetGuid(ctx, "id"), source, target, ctx.RequestAborted), ctx.RequestAborted);
        }));

        builder.Map("/api/cms/sites/{id}/theme", "PUT", "cms.site.write", async ctx => await WithScope(ctx, async s =>
        {
            var theme = await ReadJsonAsync<CmsThemeConfig>(ctx);
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsSiteAppService>().UpdateThemeAsync(GetGuid(ctx, "id"), theme, ctx.RequestAborted), ctx.RequestAborted);
        }));

        builder.Map("/api/cms/sites/{id}/publish", "POST", "cms.site.publish", async ctx => await WithScope(ctx, async s =>
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsSiteAppService>().PublishAsync(GetGuid(ctx, "id"), ctx.RequestAborted), ctx.RequestAborted)));

        builder.Map("/api/cms/sites/{id}/unpublish", "POST", "cms.site.publish", async ctx => await WithScope(ctx, async s =>
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsSiteAppService>().UnpublishAsync(GetGuid(ctx, "id"), ctx.RequestAborted), ctx.RequestAborted)));

        // ---- 布局页管理 ----
        builder.Map("/api/cms/layouts", "GET", "cms.site.read", async ctx => await WithScope(ctx, async s =>
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsLayoutAppService>().ListAsync(ctx.RequestAborted), ctx.RequestAborted)));

        builder.Map("/api/cms/layouts", "POST", "cms.layout.write", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<UpsertCmsLayoutRequest>(ctx);
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsLayoutAppService>().CreateAsync(req, ctx.RequestAborted), ctx.RequestAborted);
        }));

        builder.Map("/api/cms/layouts/{id}", "PUT", "cms.layout.write", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<UpsertCmsLayoutRequest>(ctx);
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsLayoutAppService>().UpdateAsync(GetGuid(ctx, "id"), req, ctx.RequestAborted), ctx.RequestAborted);
        }));

        builder.Map("/api/cms/layouts/{id}", "DELETE", "cms.layout.write", async ctx => await WithScope(ctx, async s =>
        {
            await s.ServiceProvider.GetRequiredService<CmsLayoutAppService>().DeleteAsync(GetGuid(ctx, "id"), ctx.RequestAborted);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }));

        // ---- 区块（单页逻辑页）----
        builder.Map("/api/cms/sites/{siteId}/sections", "POST", "cms.site.write", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<UpsertCmsSectionRequest>(ctx);
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsSectionAppService>().CreateAsync(GetGuid(ctx, "siteId"), req, ctx.RequestAborted), ctx.RequestAborted);
        }));

        builder.Map("/api/cms/sections/{id}", "PUT", "cms.site.write", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<UpsertCmsSectionRequest>(ctx);
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsSectionAppService>().UpdateAsync(GetGuid(ctx, "id"), req, ctx.RequestAborted), ctx.RequestAborted);
        }));

        builder.Map("/api/cms/sections/{id}", "DELETE", "cms.site.write", async ctx => await WithScope(ctx, async s =>
        {
            await s.ServiceProvider.GetRequiredService<CmsSectionAppService>().DeleteAsync(GetGuid(ctx, "id"), ctx.RequestAborted);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }));

        builder.Map("/api/cms/sites/{siteId}/sections/reorder", "PUT", "cms.site.write", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<ReorderSectionsRequest>(ctx);
            await s.ServiceProvider.GetRequiredService<CmsSectionAppService>().ReorderAsync(GetGuid(ctx, "siteId"), req.SectionIds, ctx.RequestAborted);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }));

        // ---- 微件实例与模板 ----
        builder.Map("/api/cms/widgets", "POST", "cms.site.write", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<UpsertCmsWidgetRequest>(ctx);
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsWidgetAppService>().CreateAsync(req, ctx.RequestAborted), ctx.RequestAborted);
        }));

        builder.Map("/api/cms/widgets/{id}", "PUT", "cms.site.write", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<UpsertCmsWidgetRequest>(ctx);
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsWidgetAppService>().UpdateAsync(GetGuid(ctx, "id"), req, ctx.RequestAborted), ctx.RequestAborted);
        }));

        builder.Map("/api/cms/widgets/{id}/move", "PUT", "cms.site.write", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<MoveCmsWidgetRequest>(ctx);
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsWidgetAppService>().MoveAsync(GetGuid(ctx, "id"), req, ctx.RequestAborted), ctx.RequestAborted);
        }));

        builder.Map("/api/cms/widgets/{id}", "DELETE", "cms.site.write", async ctx => await WithScope(ctx, async s =>
        {
            await s.ServiceProvider.GetRequiredService<CmsWidgetAppService>().DeleteAsync(GetGuid(ctx, "id"), ctx.RequestAborted);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }));

        builder.Map("/api/cms/widgets/templates", "GET", "cms.site.read", async ctx => await WithScope(ctx, async s =>
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsWidgetAppService>().ListTemplatesAsync(ctx.RequestAborted), ctx.RequestAborted)));

        builder.Map("/api/cms/widgets/{id}/save-template", "POST", "cms.site.write", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<SaveWidgetTemplateRequest>(ctx);
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsWidgetAppService>().SaveAsTemplateAsync(GetGuid(ctx, "id"), req.TemplateName, ctx.RequestAborted), ctx.RequestAborted);
        }));

        // ---- 留资记录 ----
        builder.Map("/api/cms/sites/{siteId}/leads", "GET", "cms.site.read", async ctx => await WithScope(ctx, async s =>
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsLeadAppService>().ListBySiteAsync(GetGuid(ctx, "siteId"), ctx.RequestAborted), ctx.RequestAborted)));

        builder.Map("/api/cms/leads/{id}/status", "PUT", "cms.site.write", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<UpdateLeadStatusRequest>(ctx);
            await ctx.Response.WriteAsJsonAsync(await s.ServiceProvider.GetRequiredService<CmsLeadAppService>().UpdateStatusAsync(GetGuid(ctx, "id"), req.Status, ctx.RequestAborted), ctx.RequestAborted);
        }));

        builder.Map("/api/cms/leads/{id}", "DELETE", "cms.site.write", async ctx => await WithScope(ctx, async s =>
        {
            await s.ServiceProvider.GetRequiredService<CmsLeadAppService>().DeleteAsync(GetGuid(ctx, "id"), ctx.RequestAborted);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }));

        // ---- AI 帮设计官网接口 ----
        builder.Map("/api/cms/ai/models", "GET", "cms.ai.generate", async ctx => await WithScope(ctx, async s =>
        {
            var options = s.ServiceProvider.GetRequiredService<CmsAiOptions>();
            var models = s.ServiceProvider.GetService<ICmsAiChatClient>() is { } chat
                ? await chat.ListModelsAsync(ctx.RequestAborted)
                : (IReadOnlyList<string>)[];
            await ctx.Response.WriteAsJsonAsync(new CmsAiModelsDto(options.GenerateModel, models), ctx.RequestAborted);
        }));

        builder.Map("/api/cms/ai/generate-site", "POST", "cms.ai.generate", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<CmsAiGenerateSiteRequest>(ctx);
            var draft = await s.ServiceProvider.GetRequiredService<ICmsAiDesignerService>().GenerateSiteAsync(req, ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(draft, ctx.RequestAborted);
        }));

        // SSE 流式生成：逐段下发模型原文与阶段状态，最后一条 done 携带完整草稿。
        // BFF 已使用 ResponseHeadersRead + CopyToAsync，事件可实时透传到浏览器。
        builder.Map("/api/cms/ai/generate-site/stream", "POST", "cms.ai.generate", async ctx =>
        {
            var req = await ReadJsonAsync<CmsAiGenerateSiteRequest>(ctx);
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";

            async Task SendAsync(CmsAiStreamEvent evt)
            {
                var json = JsonSerializer.Serialize(evt, SseJson);
                var bytes = Encoding.UTF8.GetBytes($"data: {json}\n\n");
                await ctx.Response.Body.WriteAsync(bytes, ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }

            try
            {
                using var scope = moduleServices.CreateScope();
                var designer = scope.ServiceProvider.GetRequiredService<ICmsAiDesignerService>();
                CmsAiSiteDraft draft;
                if (designer is ICmsAiStreamingDesignerService streaming)
                {
                    draft = await streaming.GenerateSiteStreamAsync(req, SendAsync, ctx.RequestAborted);
                }
                else
                {
                    await SendAsync(new CmsAiStreamEvent("status", "当前设计器不支持流式，等待一次性生成…"));
                    draft = await designer.GenerateSiteAsync(req, ctx.RequestAborted);
                }
                await SendAsync(new CmsAiStreamEvent(
                    "done", "生成完成", DraftJson: JsonSerializer.Serialize(draft, SseJson)));
            }
            catch (Exception ex)
            {
                await SendAsync(new CmsAiStreamEvent("error", ex.Message));
            }
        });

        // 分块生成：骨架就绪后清空画布，每块生成完立即落库并推 refreshed 事件，
        // 前端收到后刷新设计，实现页面一块块长出来。单块失败由服务层用模板兜底。
        builder.Map("/api/cms/ai/generate-site/chunked", "POST", "cms.ai.generate", async ctx =>
        {
            var req = await ReadJsonAsync<CmsAiChunkedGenerateRequest>(ctx);
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";

            async Task SendAsync(CmsAiStreamEvent evt)
            {
                var json = JsonSerializer.Serialize(evt, SseJson);
                await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"data: {json}\n\n"), ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }

            try
            {
                using var scope = moduleServices.CreateScope();
                var designer = scope.ServiceProvider.GetRequiredService<ICmsAiDesignerService>();
                var siteApp = scope.ServiceProvider.GetRequiredService<CmsSiteAppService>();
                if (designer is not ICmsAiStreamingDesignerService streaming)
                {
                    await SendAsync(new CmsAiStreamEvent("error", "当前设计器不支持分块生成。"));
                    return;
                }

                var culture = string.IsNullOrWhiteSpace(req.Language) ? "zh-CN" : req.Language.Trim();
                CmsAiSitePlan? plan = null;
                await streaming.GenerateSiteChunkedAsync(req.ToSiteRequest(), async evt =>
                {
                    if (evt.Kind == "plan")
                    {
                        plan = JsonSerializer.Deserialize<CmsAiSitePlan>(evt.DraftJson ?? string.Empty, SseJson);
                        if (plan is not null)
                            await siteApp.BeginAiDraftAsync(req.SiteId, plan.Theme, culture, ctx.RequestAborted);
                        await SendAsync(evt);
                        return;
                    }

                    if (evt.Kind == "section")
                    {
                        var section = JsonSerializer.Deserialize<CmsAiSectionDraft>(evt.DraftJson ?? string.Empty, SseJson);
                        if (section is not null)
                        {
                            var design = await siteApp.AppendAiSectionAsync(req.SiteId, section, culture, ctx.RequestAborted);
                            await SendAsync(evt);
                            // refreshed 事件带最新设计快照，前端可直接替换，避免再发一次 GET。
                            await SendAsync(new CmsAiStreamEvent(
                                "refreshed", "画布已更新", Index: evt.Index, Total: evt.Total,
                                DraftJson: JsonSerializer.Serialize(design, SseJson)));
                            return;
                        }
                    }

                    await SendAsync(evt);
                }, ctx.RequestAborted);

                await SendAsync(new CmsAiStreamEvent("done", "分块生成完成"));
            }
            catch (Exception ex)
            {
                await SendAsync(new CmsAiStreamEvent("error", ex.Message));
            }
        });

        builder.Map("/api/cms/ai/apply-site/{siteId}", "POST", "cms.ai.generate", async ctx => await WithScope(ctx, async s =>
        {
            var draft = await ReadJsonAsync<CmsAiSiteDraft>(ctx);
            var culture = ctx.Request.Query["culture"].FirstOrDefault();
            var design = await s.ServiceProvider.GetRequiredService<CmsSiteAppService>().ApplyAiDraftAsync(GetGuid(ctx, "siteId"), draft, culture, ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(design, ctx.RequestAborted);
        }));

        builder.Map("/api/cms/ai/generate-section", "POST", "cms.ai.generate", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<CmsAiGenerateSectionRequest>(ctx);
            var draft = await s.ServiceProvider.GetRequiredService<ICmsAiDesignerService>().GenerateSectionAsync(req, ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(draft, ctx.RequestAborted);
        }));

        builder.Map("/api/cms/ai/refine-widget", "POST", "cms.ai.generate", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<CmsAiRefineWidgetRequest>(ctx);
            var draft = await s.ServiceProvider.GetRequiredService<ICmsAiDesignerService>().RefineWidgetAsync(req, ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(draft, ctx.RequestAborted);
        }));

        // 生成后的局部调整：把 AI 生成的新区块草稿直接追加进当前站点，返回最新设计供画布刷新。
        builder.Map("/api/cms/ai/append-section/{siteId}", "POST", "cms.ai.generate", async ctx => await WithScope(ctx, async s =>
        {
            var draft = await ReadJsonAsync<CmsAiSectionDraft>(ctx);
            var culture = ctx.Request.Query["culture"].FirstOrDefault();
            var design = await s.ServiceProvider.GetRequiredService<CmsSiteAppService>()
                .AppendAiSectionAsync(GetGuid(ctx, "siteId"), draft, culture, ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(design, ctx.RequestAborted);
        }));
    }

    private async Task WithScope(HttpContext ctx, Func<IServiceScope, Task> handler)
    {
        try
        {
            using var scope = moduleServices.CreateScope();
            await handler(scope);
        }
        catch (KeyNotFoundException ex)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }, ctx.RequestAborted);
        }
        catch (InvalidOperationException ex)
        {
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }, ctx.RequestAborted);
        }
    }

    private static async Task<T> ReadJsonAsync<T>(HttpContext ctx) where T : class =>
        await ctx.Request.ReadFromJsonAsync<T>(ctx.RequestAborted)
        ?? throw new InvalidOperationException("请求体不能为空。");

    private static Guid GetGuid(HttpContext ctx, string name) =>
        Guid.TryParse(ctx.Request.RouteValues[name]?.ToString(), out var id)
            ? id
            : throw new InvalidOperationException($"无效的 {name}。");
}
