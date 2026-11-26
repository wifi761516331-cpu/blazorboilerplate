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
    CmsSiteAppService siteAppService)
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

        // 嵌套布局示例（向后兼容：已有库也按需补种，按名称判重）：外层侧栏 4/8，主内容列内再嵌两栏 6/6。
        // 演示"先设计布局骨架，再往叶子插槽填内容"的工作流，叶子 ZoneId = sidebar / inner-left / inner-right。
        if (!await db.Layouts.AnyAsync(l => l.Name == CmsBuiltInLayoutNames.NestedExample, ct))
        {
            var nestedRows = new List<CmsLayoutRow>
            {
                new("row-1", null, [
                    new CmsLayoutColumn("sidebar", "侧栏区", 4),
                    new CmsLayoutColumn("main", "主内容容器", 8, null, [
                        new("inner-row", null, [
                            new CmsLayoutColumn("inner-left", "内嵌左栏", 6),
                            new CmsLayoutColumn("inner-right", "内嵌右栏", 6)
                        ])
                    ])
                ])
            };
            if (!CmsLayoutTree.Validate(nestedRows, out var nestedError))
                throw new InvalidOperationException($"内置布局「{CmsBuiltInLayoutNames.NestedExample}」结构非法：{nestedError}");
            db.Layouts.Add(new CmsLayout
            {
                Name = CmsBuiltInLayoutNames.NestedExample,
                Description = "嵌套布局演示：外层侧栏 + 主列内嵌两栏，先在布局阶段搭骨架，再到内容阶段填充",
                IsBuiltIn = true,
                SortOrder = 5,
                RowsJson = JsonSerializer.Serialize(nestedRows, JsonOptions)
            });
            await db.SaveChangesAsync(ct);
        }

        // 异型结构内置布局：拼贴错位 / 中心卫星 / 跨页阶梯 / 递归嵌套。
        // 按名称判重补种，已有库也能拿到新骨架；叶子 ZoneId 全部使用专属前缀，避免被固定四态预设的插槽推断误命中。
        var asymmetricLayouts = new (string Name, string Description, int SortOrder, List<CmsLayoutRow> Rows)[]
        {
            (CmsBuiltInLayoutNames.MosaicCollage, "7/5 不对称主视觉 + 下方横向通栏，适合作品画报与编辑式封面", 6,
            [
                new("row-1", "cms-mz-row-asym", [
                    new CmsLayoutColumn("mz-feature", "主视觉特写", 7, "cms-mz-cell-feature"),
                    new CmsLayoutColumn("mz-aside", "侧边摘要", 5, "cms-mz-cell-aside")
                ]),
                new("row-2", "cms-mz-row-band", [
                    new CmsLayoutColumn("mz-band", "横向通栏", 12, "cms-mz-cell-band")
                ])
            ]),
            (CmsBuiltInLayoutNames.MosaicNested, "通栏封面下嵌三列作品位，演示布局树两级骨架", 7,
            [
                new("row-1", null, [new CmsLayoutColumn("mz-cover", "封面标题", 12, "cms-mz-cell-cover")]),
                new("row-2", "cms-mz-row-nest", [
                    new CmsLayoutColumn("mz-nest", "三列容器", 12, "cms-mz-nest", [
                        new("nest-row", null, [
                            new CmsLayoutColumn("mz-work-1", "作品一", 4, "cms-mz-cell-work"),
                            new CmsLayoutColumn("mz-work-2", "作品二", 4, "cms-mz-cell-work"),
                            new CmsLayoutColumn("mz-work-3", "作品三", 4, "cms-mz-cell-work")
                        ])
                    ])
                ])
            ]),
            (CmsBuiltInLayoutNames.MosaicStairs, "上下两行跨度反转，形成错位阶梯节奏", 8,
            [
                new("row-1", "cms-mz-row-step", [
                    new CmsLayoutColumn("mz-step-a", "阶梯左一", 4, "cms-mz-cell-step"),
                    new CmsLayoutColumn("mz-step-b", "阶梯右一", 8, "cms-mz-cell-step-wide")
                ]),
                new("row-2", "cms-mz-row-step cms-mz-row-reverse", [
                    new CmsLayoutColumn("mz-step-c", "阶梯左二", 8, "cms-mz-cell-step-wide"),
                    new CmsLayoutColumn("mz-step-d", "阶梯右二", 4, "cms-mz-cell-step")
                ])
            ]),
            (CmsBuiltInLayoutNames.SignalStage, "中心主内容 + 两侧卫星栏，适合播客、发布会与直播页", 9,
            [
                new("row-1", "cms-sg-row-stage", [
                    new CmsLayoutColumn("sg-left", "左卫星", 3, "cms-sg-cell-satellite"),
                    new CmsLayoutColumn("sg-stage", "中心舞台", 6, "cms-sg-cell-stage"),
                    new CmsLayoutColumn("sg-right", "右卫星", 3, "cms-sg-cell-satellite")
                ])
            ]),
            (CmsBuiltInLayoutNames.SignalNested, "节目标题通栏，下方内嵌 5/7 双轨节目单", 10,
            [
                new("row-1", null, [new CmsLayoutColumn("sg-head", "节目标题", 12, "cms-sg-cell-head")]),
                new("row-2", "cms-sg-row-nest", [
                    new CmsLayoutColumn("sg-nest", "双轨容器", 12, "cms-sg-nest", [
                        new("nest-row", null, [
                            new CmsLayoutColumn("sg-track-a", "节目轨 A", 5, "cms-sg-cell-track"),
                            new CmsLayoutColumn("sg-track-b", "节目轨 B", 7, "cms-sg-cell-track-wide")
                        ])
                    ])
                ])
            ]),
            (CmsBuiltInLayoutNames.AtlasSpread, "图文左右互换的跨页叙事，适合旅行、画册与案例故事", 11,
            [
                new("row-1", "cms-at-row-spread", [
                    new CmsLayoutColumn("at-text-left", "叙事左页", 5, "cms-at-cell-text"),
                    new CmsLayoutColumn("at-visual-right", "影像右页", 7, "cms-at-cell-visual")
                ]),
                new("row-2", "cms-at-row-spread", [
                    new CmsLayoutColumn("at-visual-left", "影像左页", 7, "cms-at-cell-visual"),
                    new CmsLayoutColumn("at-text-right", "叙事右页", 5, "cms-at-cell-text")
                ])
            ]),
            (CmsBuiltInLayoutNames.AtlasNested, "地图横幅下嵌四列卡片，演示多层非对称骨架", 12,
            [
                new("row-1", null, [new CmsLayoutColumn("at-map", "地图横幅", 12, "cms-at-cell-map")]),
                new("row-2", "cms-at-row-nest", [
                    new CmsLayoutColumn("at-nest", "四列容器", 12, "cms-at-nest", [
                        new("nest-row", null, [
                            new CmsLayoutColumn("at-card-1", "目的地一", 3, "cms-at-cell-card"),
                            new CmsLayoutColumn("at-card-2", "目的地二", 3, "cms-at-cell-card"),
                            new CmsLayoutColumn("at-card-3", "目的地三", 3, "cms-at-cell-card"),
                            new CmsLayoutColumn("at-card-4", "目的地四", 3, "cms-at-cell-card")
                        ])
                    ])
                ])
            ])
        };
        // 一次查询取全部已存在的内置布局名，避免逐条 AnyAsync 往返。
        var existingNames = new HashSet<string>(
            await db.Layouts.Where(l => CmsBuiltInLayoutNames.All.Contains(l.Name))
                            .Select(l => l.Name).ToListAsync(ct),
            StringComparer.Ordinal);
        foreach (var (name, description, sortOrder, rows) in asymmetricLayouts)
        {
            if (existingNames.Contains(name)) continue;
            if (!CmsLayoutTree.Validate(rows, out var error))
                throw new InvalidOperationException($"内置布局「{name}」结构非法：{error}");
            db.Layouts.Add(new CmsLayout
            {
                Name = name,
                Description = description,
                IsBuiltIn = true,
                SortOrder = sortOrder,
                RowsJson = JsonSerializer.Serialize(rows, JsonOptions)
            });
        }
        await db.SaveChangesAsync(ct);

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

        // 种子阶段必须离线、确定：默认官网也走内置手写模板，不能依赖网关/大模型。
        // AI 生成仅在设计器里按需触发（即时生成草稿 → 人工微调 → 发布）。
        var zhDraft = CmsSiteTemplateLibrary.BuildSiteDraft(
            "default", "BlazorBoilerplate", "企业级数字化平台",
            "现代、极简、高可信度的模块化数字底座官网", "#0f6cbd", isEn: false);
        await siteAppService.ApplyAiDraftAsync(defaultSiteId, zhDraft, "zh-CN", ct);

        var enDraft = CmsSiteTemplateLibrary.BuildSiteDraft(
            "default", "BlazorBoilerplate", "Enterprise Digital Platform",
            "Modern, minimal, and trustworthy modular monolith platform", "#0f6cbd", isEn: true);
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
