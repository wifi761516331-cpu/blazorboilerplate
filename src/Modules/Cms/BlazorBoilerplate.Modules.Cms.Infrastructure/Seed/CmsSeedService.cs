using System.Text.Json;
using BlazorBoilerplate.Modules.Cms.Application.AI;
using BlazorBoilerplate.Modules.Cms.Application.Services;
using BlazorBoilerplate.Modules.Cms.Domain.Entities;
using BlazorBoilerplate.Modules.Cms.Domain.ValueObjects;
using BlazorBoilerplate.Modules.Cms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Modules.Cms.Infrastructure.Seed;

public sealed class CmsSeedService(
    CmsDbContext db,
    CmsSiteAppService siteAppService,
    ICmsAiDesignerService aiDesigner)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        if (!await db.Layouts.AnyAsync(ct))
        {
            var layouts = new List<CmsLayout>
            {
                new()
                {
                    Name = "单栏通栏布局",
                    Description = "适合 Hero 横幅、特性网格、全宽内容区块",
                    IsBuiltIn = true,
                    SortOrder = 1,
                    RowsJson = JsonSerializer.Serialize(new List<CmsLayoutRow>
                    {
                        new("row-1", null, [new CmsLayoutColumn("main", "主内容区", 12)])
                    }, JsonOptions)
                },
                new()
                {
                    Name = "对称两栏布局 (6/6)",
                    Description = "适合左图右文、品牌故事与数据对比",
                    IsBuiltIn = true,
                    SortOrder = 2,
                    RowsJson = JsonSerializer.Serialize(new List<CmsLayoutRow>
                    {
                        new("row-1", null, [
                            new CmsLayoutColumn("left", "左侧区", 6),
                            new CmsLayoutColumn("right", "右侧区", 6)
                        ])
                    }, JsonOptions)
                },
                new()
                {
                    Name = "经典三栏布局 (4/4/4)",
                    Description = "适合多卡片并排展示或页脚多栏",
                    IsBuiltIn = true,
                    SortOrder = 3,
                    RowsJson = JsonSerializer.Serialize(new List<CmsLayoutRow>
                    {
                        new("row-1", null, [
                            new CmsLayoutColumn("col-1", "左栏", 4),
                            new CmsLayoutColumn("col-2", "中栏", 4),
                            new CmsLayoutColumn("col-3", "右栏", 4)
                        ])
                    }, JsonOptions)
                },
                new()
                {
                    Name = "侧栏内容布局 (4/8)",
                    Description = "适合左侧导航/简介 + 右侧主体内容",
                    IsBuiltIn = true,
                    SortOrder = 4,
                    RowsJson = JsonSerializer.Serialize(new List<CmsLayoutRow>
                    {
                        new("row-1", null, [
                            new CmsLayoutColumn("sidebar", "侧栏区", 4),
                            new CmsLayoutColumn("main", "主内容区", 8)
                        ])
                    }, JsonOptions)
                }
            };
            await db.Layouts.AddRangeAsync(layouts, ct);
            await db.SaveChangesAsync(ct);
        }

        // 默认企业官网与内置行业示例站均为只读模板：每次启动同步最新内置结构并重新发布（保留 SiteId，不影响用户复制或自建站点）。
        var defaultSite = await db.Sites.FirstOrDefaultAsync(s => s.SiteKey == "default", ct);
        Guid defaultSiteId;
        if (defaultSite is null)
        {
            var created = await siteAppService.CreateAsync(new(
                Name: "默认企业官网",
                SiteKey: "default",
                Title: "企业官方网站",
                Description: "由 BlazorBoilerplate CMS 构建的单页企业官网"), ct);
            defaultSiteId = created.Id;
        }
        else
        {
            defaultSiteId = defaultSite.Id;
            await ResetSiteContentAsync(defaultSiteId, ct);
        }

        var zhDraft = await aiDesigner.GenerateSiteAsync(new(
            BrandName: "BlazorBoilerplate",
            Industry: "企业级数字化平台",
            StylePrompt: "现代、极简、高可信度的模块化数字底座官网",
            PrimaryColor: "#0f6cbd",
            Language: "zh-CN"), ct);
        await siteAppService.ApplyAiDraftAsync(defaultSiteId, zhDraft, "zh-CN", ct);

        var enDraft = await aiDesigner.GenerateSiteAsync(new(
            BrandName: "BlazorBoilerplate",
            Industry: "Enterprise Digital Platform",
            StylePrompt: "Modern, minimal, and trustworthy modular monolith platform",
            PrimaryColor: "#0f6cbd",
            Language: "en-US"), ct);
        await siteAppService.ApplyAiDraftAsync(defaultSiteId, enDraft, "en-US", ct);
        await siteAppService.PublishAsync(defaultSiteId, ct);

        // 行业示例站点：全部来自内置手写模板库（不经 AI/LLM），每个站点中英双语并发布。
        foreach (var demo in CmsSiteTemplateLibrary.SeedSites)
        {
            var existingDemo = await db.Sites.FirstOrDefaultAsync(s => s.SiteKey == demo.SiteKey, ct);
            Guid demoSiteId;
            if (existingDemo is null)
            {
                var created = await siteAppService.CreateAsync(new(
                    Name: demo.Name,
                    SiteKey: demo.SiteKey,
                    Title: demo.Title,
                    Description: demo.Description), ct);
                demoSiteId = created.Id;
            }
            else
            {
                existingDemo.UpdateInfo(demo.Name, demo.SiteKey, demo.Title, demo.Description, existingDemo.LogoUrl, existingDemo.FaviconUrl);
                await db.SaveChangesAsync(ct);
                demoSiteId = existingDemo.Id;
                await ResetSiteContentAsync(demoSiteId, ct);
            }

            var zh = CmsSiteTemplateLibrary.BuildSiteDraft(
                demo.TemplateKey, demo.BrandName, demo.IndustryZh, demo.StylePromptZh, demo.PrimaryColor, isEn: false);
            await siteAppService.ApplyAiDraftAsync(demoSiteId, zh, "zh-CN", ct);

            var en = CmsSiteTemplateLibrary.BuildSiteDraft(
                demo.TemplateKey, demo.BrandName, demo.IndustryEn, demo.StylePromptEn, demo.PrimaryColor, isEn: true);
            await siteAppService.ApplyAiDraftAsync(demoSiteId, en, "en-US", ct);

            await siteAppService.PublishAsync(demoSiteId, ct);
        }
    }

    private async Task ResetSiteContentAsync(Guid siteId, CancellationToken ct)
    {
        await db.Widgets.Where(w => w.SiteId == siteId && !w.IsTemplate).ExecuteDeleteAsync(ct);
        await db.Sections.Where(s => s.SiteId == siteId).ExecuteDeleteAsync(ct);
    }
}
