using System.Globalization;
using System.Text.Json;
using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Application.DataExchange;
using BlazorBoilerplate.Application.OData;
using BlazorBoilerplate.Application.Platform;
using BlazorBoilerplate.Application.Rules;
using BlazorBoilerplate.Application.Search;
using BlazorBoilerplate.Domain.Events;
using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Demo;

public sealed record DemoSampleRow(string Code, string Name, int Score);
public sealed record DemoNoteDto(Guid Id, string Title, DateTimeOffset CreatedAtUtc);

public sealed record DemoOverviewDto(
    DemoSettings Settings,
    IReadOnlyList<string> RegisteredProviders,
    IReadOnlyList<string> RegisteredRuleKeys,
    bool AdminRuleMatched,
    string CacheStatus,
    int DatabaseNoteCount);

public sealed record DemoShowcaseResultDto(
    bool LockAcquired,
    string CacheValue,
    string TemplateSubject,
    string TemplateBody,
    Guid QueuedEmailId,
    bool RuleMatched,
    int SearchHits,
    int ExportCsvBytes,
    string ProviderPreview,
    string EventPublished,
    string ChannelPublished,
    Guid SavedNoteId,
    int TotalNotes);

/// <summary>
/// 汇总演示所有平台底层能力的服务与端点。
/// </summary>
public sealed class DemoShowcaseService(
    DemoDbContext db,
    ISettingService settings,
    ICacheService cache,
    IDistributedLockService distributedLock,
    IActivityLogService activityLog,
    IMessageTemplateService templates,
    IQueuedEmailService emailQueue,
    IRuleEngine ruleEngine,
    IDataExchangeService dataExchange,
    ISearchService search,
    IProviderManager providers,
    IChannelBus channelBus)
{
    private const string CacheKey = "demo:overview:sample";
    private const string CacheTag = "demo";

    public async Task<DemoOverviewDto> GetOverviewAsync(CancellationToken ct)
    {
        var currentSettings = await settings.LoadSettingAsync<DemoSettings>(cancellationToken: ct);
        var notifierList = providers.GetAllProviders<IDemoNotifierProvider>()
            .Select(p => $"{p.Metadata.SystemName} ({p.Metadata.FriendlyName})")
            .ToArray();

        var cached = await cache.GetAsync<string>(CacheKey, ct);
        var ruleSet = new RuleSet(
            "DemoAdminCheck",
            RuleLogic.All,
            [new RuleCondition("user.role", "contains", "Admin")]);
        var matched = ruleEngine.Evaluate(ruleSet, new RuleContext(Roles: new HashSet<string>(["Admin"])));
        var noteCount = await db.Notes.CountAsync(ct);

        return new DemoOverviewDto(
            currentSettings,
            notifierList,
            ruleEngine.GetRegisteredKeys(),
            matched,
            cached ?? "未缓存（点击「运行演示」后写入）",
            noteCount);
    }

    public async Task<DemoShowcaseResultDto> RunShowcaseAsync(CancellationToken ct)
    {
        await using var lockHandle = await distributedLock.TryAcquireAsync("demo:showcase:lock", TimeSpan.FromSeconds(10), ct);
        var lockAcquired = lockHandle is not null;

        var currentSettings = await settings.LoadSettingAsync<DemoSettings>(cancellationToken: ct);
        var cacheValue = $"{currentSettings.GreetingPrefix} @ {DateTimeOffset.Now:HH:mm:ss}";
        await cache.SetAsync(CacheKey, cacheValue, TimeSpan.FromMinutes(5), CacheTag, ct);

        if (currentSettings.EnableAutoAudit)
        {
            await activityLog.LogAsync(
                "Demo.Showcase",
                "开发者在 Demo 工作台执行了一键全能力演示。",
                entityName: nameof(DemoNote),
                cancellationToken: ct);
        }

        if (await templates.GetByNameAsync(DemoLifecycleHooks.WelcomeTemplateName, ct) is null)
        {
            await templates.SaveAsync(new MessageTemplateItem(
                DemoLifecycleHooks.WelcomeTemplateName,
                "欢迎使用 %ModuleName% (v%Version%)",
                "您好 %UserName%，模块 %ModuleName% 已就绪。",
                true,
                DateTimeOffset.UtcNow), ct);
        }

        var rendered = await templates.RenderAsync(
            DemoLifecycleHooks.WelcomeTemplateName,
            new Dictionary<string, string?>
            {
                ["ModuleName"] = "Demo 模块",
                ["Version"] = "1.0.0",
                ["UserName"] = "Developer"
            },
            ct) ?? ("Demo 通知", "模板渲染示例");

        var queued = await emailQueue.EnqueueAsync(
            new EnqueueEmailRequest("dev@example.com", rendered.Subject, rendered.Body, Priority: 5),
            ct);

        var ruleSet = new RuleSet("DemoRule", RuleLogic.All, [new RuleCondition("user.role", "contains", "Admin")]);
        var ruleMatched = ruleEngine.Evaluate(ruleSet, new RuleContext(Roles: new HashSet<string>(["Admin"])));

        await search.IndexAsync(new SearchDocument(
            Id: "demo-doc-1",
            IndexName: "demo",
            Title: "Demo 模块开发模板指南",
            Content: "包含 IModuleStartup、ISettings、IScheduleTask、IProvider、IWidget、IConsumer 等全部平台扩展能力。",
            Url: "/demo"), ct);
        var hits = await search.SearchAsync(new SearchQuery("Demo", "demo", Top: 5), ct);

        var csvBytes = dataExchange.ExportCsv(
        [
            new DemoSampleRow("D001", "示例项 A", 95),
            new DemoSampleRow("D002", "示例项 B", 88)
        ]);

        var firstProvider = providers.GetAllProviders<IDemoNotifierProvider>().FirstOrDefault();
        var preview = firstProvider?.Value.SendPreview("Developer", rendered.Subject) ?? "无可用 Provider";

        // 1) 写入私有数据库表 DemoNotes：DomainEventPublishingInterceptor 会自动发布 EntityInsertedEvent<DemoNote>（模块内联动）
        var note = new DemoNote { Title = $"Demo 演示记录 @ {DateTimeOffset.Now:HH:mm:ss}" };
        db.Notes.Add(note);
        await db.SaveChangesAsync(ct);
        var totalNotes = await db.Notes.CountAsync(ct);

        // 2) 发布 IChannelBus 跨模块字符串主题广播（无需在共享 Domain 定义强类型事件）
        await channelBus.PublishAsync(
            DemoModule.ChannelTopic,
            JsonSerializer.Serialize(new { note.Id, note.Title }),
            ct);

        return new DemoShowcaseResultDto(
            lockAcquired,
            cacheValue,
            rendered.Subject,
            rendered.Body,
            queued.Id,
            ruleMatched,
            hits.Count,
            csvBytes.Length,
            preview,
            nameof(EntityInsertedEvent<DemoNote>),
            DemoModule.ChannelTopic,
            note.Id,
            totalNotes);
    }

    public byte[] ExportSampleCsv() =>
        dataExchange.ExportCsv(
        [
            new DemoSampleRow("D001", "模块启动器 IModuleStartup", 100),
            new DemoSampleRow("D002", "强类型配置 ISettings", 100),
            new DemoSampleRow("D003", "计划任务 IScheduleTask", 100),
            new DemoSampleRow("D004", "策略提供者 IProvider", 100),
            new DemoSampleRow("D005", "微件插槽 WidgetZone", 100)
        ]);

    public async Task SaveSettingsAsync(DemoSettings next, CancellationToken ct)
    {
        await settings.SaveSettingAsync(next, cancellationToken: ct);
        await cache.RemoveByTagAsync(CacheTag, ct);
    }

    private static readonly ODataFieldMap NoteFieldMap = new(["id", "title", "createdatutc"]);
    private static readonly ODataQueryApplier<DemoNoteDto> NoteApplier = new(
        NoteFieldMap,
        (n, f) => f.ToLowerInvariant() switch
        {
            "id" => n.Id,
            "title" => n.Title,
            "createdatutc" => n.CreatedAtUtc,
            _ => null
        },
        (f, raw) => f.ToLowerInvariant() switch
        {
            "id" => Guid.Parse(raw),
            "createdatutc" => DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture),
            _ => raw
        });

    public async Task<ODataPage<DemoNoteDto>> QueryNotesAsync(ODataQuery query, CancellationToken ct)
    {
        var items = (await db.Notes.AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct))
            .Select(x => new DemoNoteDto(x.Id, x.Title, x.CreatedAtUtc));
        return NoteApplier.Apply(items, query);
    }
}

public sealed class DemoEndpoints(IServiceProvider moduleServices)
{
    public void Map(IModuleEndpointBuilder builder)
    {
        builder.Map("/api/demo/overview", "GET", "demo.read", async ctx =>
        {
            using var scope = moduleServices.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<DemoShowcaseService>();
            var dto = await service.GetOverviewAsync(ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(dto, ctx.RequestAborted);
        });

        builder.Map("/api/demo/notes", "GET", "demo.read", async ctx =>
        {
            using var scope = moduleServices.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<DemoShowcaseService>();
            var q = ctx.Request.Query;
            int? ParseInt(string key) =>
                int.TryParse(q[key].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
            var filter = q["$filter"].ToString();
            if (filter.Length > ODataLimits.MaxFilterLength)
                throw new InvalidOperationException($"$filter exceeds {ODataLimits.MaxFilterLength} characters.");
            var odataQuery = new ODataQuery
            {
                Filter = string.IsNullOrWhiteSpace(filter) ? null : filter,
                OrderBy = q["$orderby"].ToString() is { Length: > 0 } ob ? ob : null,
                Top = ParseInt("$top"),
                Skip = ParseInt("$skip"),
                Count = q["$count"].ToString() == "true"
            };
            var page = await service.QueryNotesAsync(odataQuery, ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(page, ctx.RequestAborted);
        });

        builder.Map("/api/demo/run-showcase", "POST", "demo.write", async ctx =>
        {
            using var scope = moduleServices.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<DemoShowcaseService>();
            var result = await service.RunShowcaseAsync(ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(result, ctx.RequestAborted);
        });

        builder.Map("/api/demo/export", "GET", "demo.read", async ctx =>
        {
            using var scope = moduleServices.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<DemoShowcaseService>();
            var bytes = service.ExportSampleCsv();
            ctx.Response.ContentType = "text/csv; charset=utf-8";
            ctx.Response.Headers.ContentDisposition = "attachment; filename=\"demo-export.csv\"";
            await ctx.Response.Body.WriteAsync(bytes, ctx.RequestAborted);
        });

        builder.Map("/api/demo/settings", "POST", "demo.write", async ctx =>
        {
            var next = await JsonSerializer.DeserializeAsync<DemoSettings>(
                ctx.Request.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                ctx.RequestAborted);
            if (next is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            using var scope = moduleServices.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<DemoShowcaseService>();
            await service.SaveSettingsAsync(next, ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(next, ctx.RequestAborted);
        });
    }
}
