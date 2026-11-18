using System.Globalization;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Infrastructure.Localization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorBoilerplate.ModuleRuntime.Tests;

public class LocalizationTests
{
    private static (InMemoryLocalizationRegistry Registry, PlatformLocalizationService Service) CreateService(
        ILocalizationStore? store = null)
    {
        var registry = new InMemoryLocalizationRegistry();
        PlatformBuiltInResources.Register(registry);

        var services = new ServiceCollection();
        if (store is not null)
            services.AddSingleton(store);
        var sp = services.BuildServiceProvider();

        return (registry, new PlatformLocalizationService(registry, sp));
    }

    [Fact]
    public void BuiltInResources_IncludeEnUsAndZhCn()
    {
        var (_, service) = CreateService();

        Assert.Equal("Sign In", service.GetStringForCulture("platform.auth.signIn", "en-US"));
        Assert.Equal("登录", service.GetStringForCulture("platform.auth.signIn", "zh-CN"));
    }

    [Fact]
    public void MissingKeyInTargetCulture_FallsBackToEnUs()
    {
        var (registry, service) = CreateService();
        registry.RegisterModuleResources("catalog", "en-US", new Dictionary<string, string>
        {
            ["catalog.onlyEnglish"] = "Only In English"
        });
        registry.RegisterModuleResources("catalog", "zh-CN", new Dictionary<string, string>
        {
            ["catalog.title"] = "商品目录"
        });

        // zh-CN 缺少 catalog.onlyEnglish 时回退中间语言 en-US
        Assert.Equal("Only In English", service.GetStringForCulture("catalog.onlyEnglish", "zh-CN"));
        // 未知语言 ja-JP 同样回退 en-US
        Assert.Equal("Only In English", service.GetStringForCulture("catalog.onlyEnglish", "ja-JP"));
        // 都不存在时返回 Key 本身
        Assert.Equal("catalog.unknown", service.GetStringForCulture("catalog.unknown", "zh-CN"));
    }

    [Fact]
    public void UnregisterModule_RemovesModuleResources()
    {
        var (registry, service) = CreateService();
        registry.RegisterModuleResources("orders", "en-US", new Dictionary<string, string>
        {
            ["orders.title"] = "Orders"
        });

        Assert.Equal("Orders", service.GetStringForCulture("orders.title", "en-US"));

        registry.UnregisterModule("orders");
        Assert.Equal("orders.title", service.GetStringForCulture("orders.title", "en-US"));
    }

    [Fact]
    public async Task LocalizationStore_OverridesRegistryWhenRegistered()
    {
        var (_, service) = CreateService(new StubLocalizationStore());

        var prev = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            var val = await service.GetStringAsync("platform.auth.signIn");
            Assert.Equal("数据库覆盖登录", val);
        }
        finally
        {
            CultureInfo.CurrentUICulture = prev;
        }
    }

    [Fact]
    public async Task SyncIndexer_ReadsStoreValuesCachedByAsyncPath()
    {
        var (_, service) = CreateService(new StubLocalizationStore());
        var prev = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            // 异步路径先从 Store 读取并回填快照。
            Assert.Equal("数据库覆盖登录", await service.GetStringAsync("platform.auth.signIn"));
            // 同步索引器随后应读到同一份 Store 覆盖值，而不是内置注册表的"登录"。
            Assert.Equal("数据库覆盖登录", service["platform.auth.signIn"]);
        }
        finally
        {
            CultureInfo.CurrentUICulture = prev;
        }
    }

    [Fact]
    public async Task DelegatingLocalizationStore_BridgesScopedModuleStoreAndDetachesCleanly()
    {
        var bridge = new DelegatingLocalizationStore();
        ILocalizationStore hostStore = bridge;

        // 未挂载模块时返回空，不阻断内置回退
        Assert.False(bridge.IsAttached);
        Assert.Null(bridge.AttachedModuleId);
        Assert.Empty(await hostStore.GetLanguagesAsync());
        Assert.Null(await hostStore.TryGetResourceAsync("zh-CN", "platform.auth.signIn"));
        Assert.Empty(await hostStore.GetResourcesAsync("zh-CN"));

        // 模拟模块独立 DI 容器（ValidateScopes = true + Scoped Store）
        var moduleServices = new ServiceCollection();
        moduleServices.AddScoped<ILocalizationStore, StubLocalizationStore>();
        using var moduleProvider = moduleServices.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        bridge.Attach("localization", moduleProvider);
        Assert.True(bridge.IsAttached);
        Assert.Equal("localization", bridge.AttachedModuleId);
        Assert.Equal("数据库覆盖登录", await hostStore.TryGetResourceAsync("zh-CN", "platform.auth.signIn"));
        Assert.NotEmpty(await hostStore.GetLanguagesAsync());

        // 非匹配模块 Detach 不应误解绑
        bridge.Detach("catalog");
        Assert.True(bridge.IsAttached);

        // 匹配模块 Detach 后立即回到未挂载状态
        bridge.Detach("localization");
        Assert.False(bridge.IsAttached);
        Assert.Null(bridge.AttachedModuleId);
        Assert.Null(await hostStore.TryGetResourceAsync("zh-CN", "platform.auth.signIn"));
    }

    private sealed class StubLocalizationStore : ILocalizationStore
    {
        public Task<IReadOnlyList<LanguageInfo>> GetLanguagesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(LocalizationDefaults.BuiltInLanguages);

        public Task<string?> TryGetResourceAsync(string culture, string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(culture == "zh-CN" && key == "platform.auth.signIn" ? "数据库覆盖登录" : null);

        public Task<IReadOnlyDictionary<string, string>> GetResourcesAsync(string culture, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public Task SyncModuleResourcesAsync(string moduleId, IReadOnlyList<LocaleResourceEntry> entries, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

