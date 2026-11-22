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

        if (!await db.Sites.AnyAsync(ct))
        {
            var site = await siteAppService.CreateAsync(new(
                Name: "默认企业官网",
                SiteKey: "default",
                Title: "企业官方网站",
                Description: "由 BlazorBoilerplate CMS 构建的单页企业官网"), ct);

            var zhDraft = await aiDesigner.GenerateSiteAsync(new(
                BrandName: "BlazorBoilerplate",
                Industry: "企业级数字化平台",
                StylePrompt: "现代、极简、高可信度的模块化数字底座官网",
                PrimaryColor: "#0f6cbd",
                Language: "zh-CN"), ct);

            await siteAppService.ApplyAiDraftAsync(site.Id, zhDraft, "zh-CN", ct);

            var enDraft = await aiDesigner.GenerateSiteAsync(new(
                BrandName: "BlazorBoilerplate",
                Industry: "Enterprise Digital Platform",
                StylePrompt: "Modern, minimal, and trustworthy modular monolith platform",
                PrimaryColor: "#0f6cbd",
                Language: "en-US"), ct);

            await siteAppService.ApplyAiDraftAsync(site.Id, enDraft, "en-US", ct);
            await siteAppService.PublishAsync(site.Id, ct);
        }
    }
}
