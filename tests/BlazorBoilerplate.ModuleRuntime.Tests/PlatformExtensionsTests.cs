using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Application.Caching;
using BlazorBoilerplate.Application.DataExchange;
using BlazorBoilerplate.Application.Platform;
using BlazorBoilerplate.Application.Rules;
using BlazorBoilerplate.Application.Search;
using BlazorBoilerplate.Domain;
using BlazorBoilerplate.Domain.Events;
using BlazorBoilerplate.Infrastructure.Caching;
using BlazorBoilerplate.Infrastructure.DataExchange;
using BlazorBoilerplate.Infrastructure.Events;
using BlazorBoilerplate.Infrastructure.Platform;
using BlazorBoilerplate.Infrastructure.Rules;
using BlazorBoilerplate.Infrastructure.Search;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.ModuleRuntime.Runtime;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BlazorBoilerplate.ModuleRuntime.Tests;

public class PlatformExtensionsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"bb-nop-parity-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private sealed class DemoSettings : ISettings
    {
        public string ApiTitle { get; set; } = "Default";
        public int PageSize { get; set; } = 20;
    }

    private sealed class DemoTask(Action onRun) : IScheduleTask
    {
        public string Name => "DemoTask";
        public int Seconds => 60;
        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            onRun();
            return Task.CompletedTask;
        }
    }

    private sealed class DummyWidget;

    [Fact]
    public async Task SettingService_SupportsGlobalAndTenantOverrides()
    {
        var svc = new JsonSettingService(_tempDir);
        var tenantId = Guid.NewGuid();

        await svc.SaveSettingAsync(new DemoSettings { ApiTitle = "GlobalTitle", PageSize = 50 });
        await svc.SaveSettingAsync(new DemoSettings { ApiTitle = "TenantTitle", PageSize = 100 }, tenantId);

        var globalLoaded = await svc.LoadSettingAsync<DemoSettings>();
        var tenantLoaded = await svc.LoadSettingAsync<DemoSettings>(tenantId);

        Assert.Equal("GlobalTitle", globalLoaded.ApiTitle);
        Assert.Equal(50, globalLoaded.PageSize);
        Assert.Equal("TenantTitle", tenantLoaded.ApiTitle);
        Assert.Equal(100, tenantLoaded.PageSize);

        await svc.DeleteSettingAsync<DemoSettings>(tenantId);
        var fallback = await svc.LoadSettingAsync<DemoSettings>(tenantId);
        Assert.Equal("GlobalTitle", fallback.ApiTitle);
    }

    [Fact]
    public async Task ScheduleTaskManager_RunsAndUnregistersByModule()
    {
        var manager = new ModuleScheduleTaskManager(NullLogger<ModuleScheduleTaskManager>.Instance);
        var count = 0;

        manager.Register("demo", "demo:task1", new DemoTask(() => count++));
        Assert.Single(manager.List());

        var result = await manager.RunNowAsync("demo:task1");
        Assert.NotNull(result);
        Assert.Equal(1, count);
        Assert.NotNull(result!.LastSuccessUtc);

        manager.UnregisterModule("demo");
        Assert.Empty(manager.List());
    }

    [Fact]
    public void WidgetZoneRegistry_SortsByOrderAndUnregistersByModule()
    {
        WidgetZoneRegistry.Register("zone_test", typeof(DummyWidget), "mod_b", 3000);
        WidgetZoneRegistry.Register("zone_test", typeof(PlatformExtensionsTests), "mod_a", 100);

        var widgets = WidgetZoneRegistry.GetWidgets("zone_test");
        Assert.Equal(2, widgets.Count);
        Assert.Equal("mod_a", widgets[0].ModuleId);

        WidgetZoneRegistry.UnregisterModule("mod_a");
        WidgetZoneRegistry.UnregisterModule("mod_b");
        Assert.Empty(WidgetZoneRegistry.GetWidgets("zone_test"));
    }

    [Fact]
    public async Task ActivityLogAndMessageTemplate_WorkEndToEnd()
    {
        var logSvc = new JsonActivityLogService(_tempDir);
        await logSvc.LogAsync("User.Login", "管理员登录", "User", "admin");
        var logs = await logSvc.QueryAsync("管理员");
        Assert.Single(logs);
        Assert.Equal("User.Login", logs[0].SystemKeyword);

        var tplSvc = new JsonMessageTemplateService(_tempDir);
        var rendered = await tplSvc.RenderAsync("Auth.EmailCode", new Dictionary<string, string?>
        {
            ["Site.Name"] = "MyPlatform",
            ["Auth.Code"] = "123456",
            ["Auth.ExpireMinutes"] = "10"
        });
        Assert.NotNull(rendered);
        Assert.Contains("MyPlatform", rendered!.Value.Subject);
        Assert.Contains("123456", rendered.Value.Body);
    }

    [Fact]
    public void EntityEvents_ImplementDomainEventContract()
    {
        var inserted = new EntityInsertedEvent<string>("row1");
        var updated = new EntityUpdatedEvent<string>("row1");
        var deleted = new EntityDeletedEvent<string>("row1");
        Assert.IsAssignableFrom<IDomainEvent>(inserted);
        Assert.IsAssignableFrom<IDomainEvent>(updated);
        Assert.IsAssignableFrom<IDomainEvent>(deleted);
    }

    private sealed class StubEmailSender(bool shouldFail) : IEmailSender
    {
        public int SentCount { get; private set; }
        public bool Fail { get; set; } = shouldFail;

        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("SMTP down");
            SentCount++;
            return Task.CompletedTask;
        }
    }

    private sealed record SampleCacheEntity(Guid Id, string Name);

    [Fact]
    public async Task QueuedEmailService_SupportsRetryAndRequeue()
    {
        var sender = new StubEmailSender(shouldFail: true);
        var queue = new JsonQueuedEmailService(_tempDir, sender, NullLogger<JsonQueuedEmailService>.Instance);

        var item = await queue.EnqueueAsync(new EnqueueEmailRequest("test@example.com", "Hi", "Body"));
        var sentFirst = await queue.ProcessPendingAsync(maxBatch: 10, maxTries: 1);
        Assert.Equal(0, sentFirst);

        var failed = (await queue.QueryAsync(pendingOnly: true)).Single();
        Assert.Equal(1, failed.SentTries);
        Assert.Equal("SMTP down", failed.LastError);

        // 达到 maxTries=1 不再自动发，需 RequeueAsync 重置
        Assert.Equal(0, await queue.ProcessPendingAsync(maxBatch: 10, maxTries: 1));

        sender.Fail = false;
        Assert.True(await queue.RequeueAsync(item.Id));
        Assert.Equal(1, await queue.ProcessPendingAsync(maxBatch: 10, maxTries: 1));
        Assert.Equal(1, sender.SentCount);
        Assert.Empty(await queue.QueryAsync(pendingOnly: true));
    }

    [Fact]
    public async Task EntityCacheInvalidator_ClearsIdAndTagOnEntityEvents()
    {
        using var mem = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryCacheService(mem);
        var bus = new EventBus(NullLogger<EventBus>.Instance);
        var invalidator = new EntityCacheInvalidator<SampleCacheEntity>(cache);
        bus.SubscribeAll(invalidator);

        var entity = new SampleCacheEntity(Guid.NewGuid(), "ItemA");
        await cache.SetAsync(EntityCacheDefaults<SampleCacheEntity>.ByIdKey(entity.Id), entity, TimeSpan.FromMinutes(5), EntityCacheDefaults<SampleCacheEntity>.AllTag, default);
        await cache.SetAsync(EntityCacheDefaults<SampleCacheEntity>.Prefix + "list", new[] { entity }, TimeSpan.FromMinutes(5), EntityCacheDefaults<SampleCacheEntity>.AllTag, default);

        Assert.NotNull(await cache.GetAsync<SampleCacheEntity>(EntityCacheDefaults<SampleCacheEntity>.ByIdKey(entity.Id), default));

        await bus.PublishAsync(new EntityUpdatedEvent<SampleCacheEntity>(entity), default);

        Assert.Null(await cache.GetAsync<SampleCacheEntity>(EntityCacheDefaults<SampleCacheEntity>.ByIdKey(entity.Id), default));
        Assert.Null(await cache.GetAsync<SampleCacheEntity[]>(EntityCacheDefaults<SampleCacheEntity>.Prefix + "list", default));
    }

    private interface ISampleStrategy : IProvider
    {
        string Execute();
    }

    [SystemName("strategy.fast")]
    [FriendlyName("快速策略")]
    [ProviderOrder(100)]
    private sealed class FastStrategy : ISampleStrategy
    {
        public static int ConstructionCount;
        public FastStrategy() => ConstructionCount++;
        public string Execute() => "fast";
    }

    [SystemName("strategy.safe")]
    [FriendlyName("安全策略")]
    [ProviderOrder(200)]
    private sealed class SafeStrategy : ISampleStrategy
    {
        public string Execute() => "safe";
    }

    [Fact]
    public async Task ProviderManager_LazyActivatesAndSupportsTenantToggle()
    {
        FastStrategy.ConstructionCount = 0;
        var pm = new ModuleProviderManager(_tempDir);
        using var sp = new ServiceCollection().BuildServiceProvider();

        pm.RegisterModuleProviders("demo", typeof(FastStrategy).Assembly, sp);
        var list = pm.GetAllProviders<ISampleStrategy>();

        Assert.True(list.Count >= 2);
        Assert.Equal("strategy.fast", list[0].Metadata.SystemName);
        Assert.Equal(0, FastStrategy.ConstructionCount); // 未访问 Value 前绝不实例化

        Assert.Equal("fast", list[0].Value.Execute());
        Assert.Equal(1, FastStrategy.ConstructionCount);

        var tenantId = Guid.NewGuid();
        await pm.SetEnabledAsync("strategy.fast", false, tenantId);
        Assert.Null(pm.GetProvider<ISampleStrategy>("strategy.fast", tenantId));
        Assert.NotNull(pm.GetProvider<ISampleStrategy>("strategy.fast"));

        pm.UnregisterModule("demo");
        Assert.Empty(pm.GetAllProviders<ISampleStrategy>());
    }

    [Fact]
    public void RuleEngine_EvaluatesAllAndAnyRules()
    {
        var engine = new PlatformRuleEngine();
        var ctx = new RuleContext(
            UserId: Guid.NewGuid(),
            Roles: new HashSet<string>(["Admin", "Editor"]),
            Culture: "zh-CN");

        var allowSet = new RuleSet("AllowAdminCn", RuleLogic.All,
        [
            new RuleCondition("user.authenticated", "eq", "true"),
            new RuleCondition("user.role", "in", "Admin,Manager"),
            new RuleCondition("culture", "eq", "zh-CN")
        ]);
        Assert.True(engine.Evaluate(allowSet, ctx));

        var denySet = new RuleSet("DenyEn", RuleLogic.All,
        [
            new RuleCondition("culture", "eq", "en-US")
        ]);
        Assert.False(engine.Evaluate(denySet, ctx));
    }

    private sealed class CsvSampleRow
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    [Fact]
    public void DataExchange_ExportsAndImportsCsvWithErrorCollection()
    {
        var svc = new PlatformDataExchangeService();
        var rows = new[]
        {
            new CsvSampleRow { Name = "Alpha, Inc", Count = 12 },
            new CsvSampleRow { Name = "Beta", Count = 34 }
        };

        var bytes = svc.ExportCsv(rows,
        [
            new DataColumnMap(nameof(CsvSampleRow.Name), "名称"),
            new DataColumnMap(nameof(CsvSampleRow.Count), "数量")
        ]);

        using var ms = new MemoryStream(bytes);
        var imported = svc.ImportCsv<CsvSampleRow>(ms,
        [
            new DataColumnMap(nameof(CsvSampleRow.Name), "名称"),
            new DataColumnMap(nameof(CsvSampleRow.Count), "数量")
        ]);

        Assert.Equal(2, imported.SuccessCount);
        Assert.Empty(imported.Errors);
        Assert.Equal("Alpha, Inc", imported.Items[0].Name);
        Assert.Equal(34, imported.Items[1].Count);
    }

    [Fact]
    public void ModularAssetManager_ResolvesSafeFilesAndBlocksTraversal()
    {
        var pkgDir = Path.Combine(_tempDir, "pkg");
        var assetsDir = Path.Combine(pkgDir, "assets");
        Directory.CreateDirectory(assetsDir);
        File.WriteAllText(Path.Combine(assetsDir, "site.css"), "body{}");
        File.WriteAllText(Path.Combine(_tempDir, "secret.txt"), "nope");

        var mgr = new ModularAssetManager();
        mgr.RegisterModule("demo", pkgDir);

        Assert.True(mgr.TryResolveFile("demo", "site.css", out var fullPath, out var ct));
        Assert.StartsWith("text/css", ct);
        Assert.True(File.Exists(fullPath));

        // 路径穿越必须被拦截
        Assert.False(mgr.TryResolveFile("demo", "../../secret.txt", out _, out _));

        mgr.UnregisterModule("demo");
        Assert.False(mgr.TryResolveFile("demo", "site.css", out _, out _));
    }

    [Fact]
    public async Task SearchService_IndexesAndRanksHits()
    {
        var search = new InMemorySearchService();
        await search.IndexAsync(new SearchDocument("1", "docs", "用户管理接口", "提供用户列表与权限管理", "/api-docs"));
        await search.IndexAsync(new SearchDocument("2", "docs", "商品目录", "管理商品价格与分类", "/catalog"));

        var hits = await search.SearchAsync(new SearchQuery("用户"));
        Assert.Single(hits);
        Assert.Equal("1", hits[0].Id);
        Assert.True(hits[0].Score >= 5);
    }

    [Fact]
    public async Task DistributedLock_PreventsConcurrentLocalAcquisition()
    {
        var lockSvc = new RedisDistributedLockService();
        await using var first = await lockSvc.TryAcquireAsync("lock:job:1", TimeSpan.FromSeconds(10));
        Assert.NotNull(first);

        var second = await lockSvc.TryAcquireAsync("lock:job:1", TimeSpan.FromSeconds(10));
        Assert.Null(second);

        await first!.DisposeAsync();
        await using var third = await lockSvc.TryAcquireAsync("lock:job:1", TimeSpan.FromSeconds(10));
        Assert.NotNull(third);
    }

    private sealed class DemoClientStartup(int order, List<int> log) : IModuleClientStartup
    {
        public int Order => order;
        public void ConfigureServices(IServiceCollection services) => log.Add(Order);
    }

    [ModuleResource(ModuleResourceType.Stylesheet, "/_modules/assets/demo/chart.css")]
    [ModuleResource(ModuleResourceType.Script, "/_modules/assets/demo/chart.js")]
    private sealed class DemoResourceComponent;

    [Fact]
    public void ClientStartupAndModuleResources_WorkAsExpected()
    {
        var log = new List<int>();
        var startups = new IModuleClientStartup[]
        {
            new DemoClientStartup(3000, log),
            new DemoClientStartup(100, log)
        };
        var services = new ServiceCollection();
        foreach (var s in startups.OrderBy(x => x.Order))
            s.ConfigureServices(services);
        Assert.Equal([100, 3000], log);

        var attrs = typeof(DemoResourceComponent)
            .GetCustomAttributes(typeof(ModuleResourceAttribute), inherit: false)
            .OfType<ModuleResourceAttribute>()
            .ToArray();
        Assert.Equal(2, attrs.Length);
        Assert.Equal(ModuleResourceType.Stylesheet, attrs[0].Type);
        Assert.Equal("/_modules/assets/demo/chart.js", attrs[1].Url);
    }

    private sealed class DemoLifecycleHook(List<string> events) : IModuleLifecycleHooks
    {
        public Task InstallAsync(IModuleContext context, CancellationToken cancellationToken)
        {
            events.Add("install");
            return Task.CompletedTask;
        }

        public Task UpdateAsync(IModuleContext context, string previousVersion, string targetVersion, CancellationToken cancellationToken)
        {
            events.Add($"update:{previousVersion}->{targetVersion}");
            return Task.CompletedTask;
        }

        public Task UninstallAsync(IModuleContext context, CancellationToken cancellationToken)
        {
            events.Add("uninstall");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ModuleLifecycleHooks_CoverInstallUpdateAndUninstall()
    {
        var events = new List<string>();
        IModuleLifecycleHooks hook = new DemoLifecycleHook(events);

        await hook.InstallAsync(null!, CancellationToken.None);
        await hook.UpdateAsync(null!, "1.0.0", "1.1.0", CancellationToken.None);
        await hook.UninstallAsync(null!, CancellationToken.None);

        Assert.Equal(["install", "update:1.0.0->1.1.0", "uninstall"], events);
    }
}
