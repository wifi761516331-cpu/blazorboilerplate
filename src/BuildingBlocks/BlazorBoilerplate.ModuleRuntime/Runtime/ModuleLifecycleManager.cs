using System.Collections.Concurrent;
using System.Text.Json;
using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Application.DataExchange;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Application.Platform;
using BlazorBoilerplate.Application.Rules;
using BlazorBoilerplate.Application.Search;
using BlazorBoilerplate.Domain.Events;
using BlazorBoilerplate.Infrastructure.Events;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.ModuleRuntime.Endpoints;
using BlazorBoilerplate.ModuleRuntime.LoadContext;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlazorBoilerplate.ModuleRuntime.Runtime;

/// <summary>
/// 一个模块版本的运行时句柄。Host 只通过稳定接口持有它；
/// 卸载完成必须把所有字段清空，否则 ALC 无法回收。
/// </summary>
internal sealed class ModuleHandle : IModuleContext
{
    public required string ModuleId { get; init; }
    public required string Version { get; init; }
    public required IServiceProvider HostServices { get; init; }
    public required ILoggerFactory LoggerFactory { get; init; }
    public required IModuleAudit Audit { get; init; }
    public required CancellationToken HostStopping { get; init; }

    public ModulePackage? Package;
    public ModuleLoadContext? Alc;
    public ServiceProvider? Services;
    public IModule? Module;
    public IReadOnlyList<ModuleEndpointDefinition>? Definitions;
    public List<object>? EventHandlers;
    public WeakReference? AlcReference;
    public int ActiveRequests;
    public int Accepting = 1;

    public bool TryEnter()
    {
        if (Volatile.Read(ref Accepting) == 0)
            return false;
        Interlocked.Increment(ref ActiveRequests);
        if (Volatile.Read(ref Accepting) == 0)
        {
            Interlocked.Decrement(ref ActiveRequests);
            return false;
        }
        return true;
    }

    public void Exit() => Interlocked.Decrement(ref ActiveRequests);

    public void Clear()
    {
        Package = null;
        Alc = null;
        Services = null;
        Module = null;
        Definitions = null;
        EventHandlers = null;
    }
}

internal sealed class EndpointCollector : IModuleEndpointBuilder
{
    public List<ModuleEndpointDefinition> Definitions { get; } = [];

    public void Map(string pattern, string httpMethod, string permission, RequestDelegate handler) =>
        Definitions.Add(new ModuleEndpointDefinition
        {
            Pattern = pattern,
            HttpMethod = httpMethod,
            Permission = permission,
            Handler = handler
        });
}

public sealed class ModuleLifecycleManager(
    IOptions<ModuleRuntimeOptions> options,
    ModulePackageManager packages,
    ModuleDiscovery discovery,
    ModuleCatalog catalog,
    ModuleLoadContextFactory loadContextFactory,
    ModuleEndpointRegistry registry,
    IServiceProvider hostServices,
    ILoggerFactory loggerFactory,
    IModuleAudit audit,
    ILogger<ModuleLifecycleManager> logger,
    ModuleStateStore? stateStore = null)
{
    private readonly ConcurrentDictionary<string, ModuleHandle> _handles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WeakReference> _lastUnloaded = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ModuleDescriptor> List() => catalog.List();

    public IReadOnlyList<ModuleDescriptor> ListOrdered() => catalog.ListOrdered();

    public IReadOnlyList<string> ResolveLoadOrder() => catalog.ResolveLoadOrder();

    public LoadGraphValidationResult ValidateLoadGraph() => catalog.ValidateLoadGraph();

    public WeakReference? LastUnloaded(string id) =>
        _lastUnloaded.TryGetValue(id, out var reference) ? reference : null;

    /// <summary>列出某模块的全部版本（语义版本降序）。</summary>
    public IReadOnlyList<ModuleCatalogEntry> ListVersions(string id) => catalog.ListVersions(id);

    /// <summary>启动时扫描受控目录，只做发现与登记，不自动加载；若存在持久化状态则恢复各模块上次激活版本。</summary>
    public async Task<int> RefreshCatalogAsync(CancellationToken cancellationToken)
    {
        var found = await discovery.DiscoverAsync(cancellationToken);
        foreach (var package in found)
            // 按 (id, version) 去重而非按 id：同一模块的磁盘多版本都要注册，
            // 否则版本下拉/切换（load?version=）会对未注册版本报 not found。
            if (!catalog.HasVersion(package.Manifest.Id, package.Manifest.Version))
                catalog.Register(package, ModuleState.Installed);

        if (stateStore is not null)
        {
            var saved = stateStore.Load();
            foreach (var (id, persisted) in saved)
            {
                if (catalog.HasVersion(id, persisted.ActiveVersion))
                {
                    catalog.SetActiveVersion(id, persisted.ActiveVersion);
                    logger.LogInformation(
                        "Restored active version from state store. Id={Id}, Version={Version}, WasRunning={IsRunning}",
                        id, persisted.ActiveVersion, persisted.IsRunning);
                }
            }
        }
        return found.Count;
    }

    public async Task<ModuleDescriptor> InstallAsync(string packageDirectory, CancellationToken cancellationToken)
    {
        var package = await packages.ReadAsync(packageDirectory, cancellationToken);
        var id = package.Manifest.Id;
        var version = package.Manifest.Version;

        if (catalog.HasVersion(id, version))
        {
            var existing = catalog.ListVersions(id)
                .First(e => string.Equals(e.Manifest.Version, version, StringComparison.OrdinalIgnoreCase));
            if (existing.Descriptor.State is not (ModuleState.Uninstalled or ModuleState.Failed or ModuleState.Quarantined))
                throw new InvalidOperationException(
                    $"Module '{id}' v{version} is already installed (state={existing.Descriptor.State}).");
        }

        catalog.Register(package, ModuleState.Discovered);
        // 状态流转必须针对新注册的版本条目，而非当前激活版本（激活版本可能正在 Running）。
        catalog.SetState(id, ModuleState.Verifying, error: null, version);
        catalog.SetState(id, ModuleState.Staging, error: null, version);
        catalog.SetState(id, ModuleState.Installed, error: null, version);

        // 模块当前未加载（无运行中版本）时，新安装版本自动成为激活版本，达到「上传即可加载」。
        if (!_handles.ContainsKey(id))
        {
            catalog.SetActiveVersion(id, version);
            stateStore?.Save(id, version, isRunning: false);
        }

        return catalog.GetVersionDescriptor(id, version);
    }

    public ModuleDescriptor Get(string id) =>
        catalog.TryGet(id, out var entry)
            ? entry.Descriptor
            : throw new KeyNotFoundException($"Module '{id}' is not in the catalog.");

    /// <summary>
    /// 运行中模块的私有 DI 容器（供宿主解析模块内注册的共享契约实现，如 IModuleDatabaseMigrations）。
    /// 未加载返回 null。注意：返回的是 collectible ALC 中的服务提供者，调用方不得长期持有模块私有类型。
    /// </summary>
    public IServiceProvider? GetModuleServices(string id) =>
        _handles.TryGetValue(id, out var handle) ? handle.Services : null;

    /// <summary>
    /// 按 ResolveLoadOrder 确定的顺序批量加载全部模块。
    /// nopCommerce 风格：被依赖模块先加载；若某模块的依赖未能成功加载，
    /// 该模块标记为 Failed 并跳过，不阻断其余模块的加载。已运行的模块直接计入。
    /// </summary>
    public async Task<IReadOnlyList<ModuleDescriptor>> LoadAllAsync(
        CancellationToken cancellationToken,
        bool respectPersistedState = false)
    {
        var loadOrder = catalog.ResolveLoadOrder();
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<ModuleDescriptor>();
        var saved = respectPersistedState ? stateStore?.Load() : null;

        foreach (var id in loadOrder)
        {
            if (!catalog.TryGet(id, out var entry))
            {
                results.Add(new ModuleDescriptor(id, id, "-", "-", ModuleState.Failed, "", null,
                    "Not in catalog.", [], 0));
                continue;
            }

            if (_handles.ContainsKey(id))
            {
                loaded.Add(id);
                results.Add(Get(id));
                continue;
            }

            if (saved is not null && saved.TryGetValue(id, out var persisted) && !persisted.IsRunning)
            {
                logger.LogInformation(
                    "Module '{Id}' v{Version} was stopped on previous run; skipping startup auto-load.",
                    id, entry.Descriptor.Version);
                results.Add(Get(id));
                continue;
            }

            var unmet = entry.Manifest.Dependencies.Where(d => !loaded.Contains(d)).ToArray();
            if (unmet.Length > 0)
            {
                catalog.SetState(id, ModuleState.Failed,
                    "Dependency not loaded: " + string.Join(", ", unmet));
                results.Add(Get(id));
                continue;
            }

            try
            {
                await LoadAsync(id, cancellationToken);
                loaded.Add(id);
            }
            catch (Exception ex)
            {
                // LoadAsync 已把模块置为 Failed 并写入审计；继续加载其余模块。
                logger.LogWarning(ex, "Module '{Id}' failed during LoadAll; continuing.", id);
            }
            results.Add(Get(id));
        }
        return results;
    }

    public async Task<ModuleDescriptor> LoadAsync(string id, CancellationToken cancellationToken, string? version = null)
    {
        if (_handles.ContainsKey(id))
        {
            var loadedVersion = _handles[id].Version;
            if (string.IsNullOrWhiteSpace(version) || string.Equals(loadedVersion, version, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Module '{id}' v{loadedVersion} is already loaded.");
            // 显式指定了不同版本：自动卸载当前运行版本后切换（版本切换语义）。
            await UnloadAsync(id, cancellationToken);
        }

        // 指定版本时先校验并设为激活版本（LoadAll / 默认走激活版本）。
        if (!string.IsNullOrWhiteSpace(version))
        {
            if (!catalog.HasVersion(id, version))
                throw new KeyNotFoundException($"Module '{id}' v{version} is not installed.");
            catalog.SetActiveVersion(id, version);
        }

        var entry = catalog.TryGet(id, out var found) ? found : null;
        if (entry is null)
        {
            await RefreshCatalogAsync(cancellationToken);
            if (!catalog.TryGet(id, out found))
                throw new KeyNotFoundException($"Module '{id}' is not installed.");
            entry = found;
        }

        ModuleStateMachine.Ensure(entry.Descriptor.State, ModuleState.Loading);
        catalog.SetState(id, ModuleState.Loading);

        var package = await packages.ReadAsync(entry.Descriptor.PackagePath, cancellationToken);
        var alc = loadContextFactory.Create(id, entry.Descriptor.Version, package.EntryAssemblyPath);
        var handle = new ModuleHandle
        {
            ModuleId = id,
            Version = entry.Descriptor.Version,
            HostServices = hostServices,
            LoggerFactory = loggerFactory,
            Audit = audit,
            HostStopping = hostServices.GetService<IHostApplicationLifetime>()?.ApplicationStopping ?? CancellationToken.None,
            Package = package,
            Alc = alc,
            AlcReference = new WeakReference(alc, trackResurrection: false)
        };

        try
        {
            var assembly = alc.LoadAssemblyNoLock(package.EntryAssemblyPath);
            var entryType = assembly.GetType(package.Manifest.EntryType, throwOnError: true)!;
            if (!typeof(IModule).IsAssignableFrom(entryType))
                throw new InvalidOperationException(
                    $"Entry type '{package.Manifest.EntryType}' does not implement {nameof(IModule)}. " +
                    "Shared contracts must resolve to the Host default load context.");

            var module = (IModule)Activator.CreateInstance(entryType)!;
            handle.Module = module;

            var startups = assembly.GetTypes()
                .Where(t => t != entryType
                    && t.IsClass
                    && !t.IsAbstract
                    && typeof(IModuleStartup).IsAssignableFrom(t)
                    && t.GetConstructor(Type.EmptyTypes) is not null)
                .Select(t => (IModuleStartup)Activator.CreateInstance(t)!)
                .OrderBy(s => s.Order)
                .ThenBy(s => s.GetType().FullName, StringComparer.Ordinal)
                .ToArray();

            var services = new ServiceCollection();
            services.AddSingleton(loggerFactory);
            services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

            // 宿主事件总线注入模块 DI：DbContext 的写后发布拦截器与模块 handler 共用同一实例。
            // IEventPublisher / IChannelBus 契约在 Domain（共享程序集），类型身份一致。
            if (hostServices.GetService(typeof(IEventPublisher)) is IEventPublisher publisher)
                services.AddSingleton(publisher);
            if (hostServices.GetService(typeof(IChannelBus)) is IChannelBus channelBus)
                services.AddSingleton(channelBus);

            if (handle.HostServices.GetService(typeof(BlazorBoilerplate.Application.Abstractions.ICurrentUserContext))
                is BlazorBoilerplate.Application.Abstractions.ICurrentUserContext currentUser)
            {
                services.AddSingleton(currentUser);
            }
            if (hostServices.GetService(typeof(ISettingService)) is ISettingService settingService)
                services.AddSingleton(settingService);
            if (hostServices.GetService(typeof(IActivityLogService)) is IActivityLogService activityLog)
                services.AddSingleton(activityLog);
            if (hostServices.GetService(typeof(IMessageTemplateService)) is IMessageTemplateService messageTemplate)
                services.AddSingleton(messageTemplate);
            if (hostServices.GetService(typeof(IQueuedEmailService)) is IQueuedEmailService queuedEmail)
                services.AddSingleton(queuedEmail);
            if (hostServices.GetService(typeof(ICacheService)) is ICacheService cacheService)
                services.AddSingleton(cacheService);
            if (hostServices.GetService(typeof(IScheduleTaskManager)) is IScheduleTaskManager scheduleTasks)
                services.AddSingleton(scheduleTasks);
            if (hostServices.GetService(typeof(IProviderManager)) is IProviderManager providerManager)
                services.AddSingleton(providerManager);
            if (hostServices.GetService(typeof(IRuleEngine)) is IRuleEngine ruleEngine)
                services.AddSingleton(ruleEngine);
            if (hostServices.GetService(typeof(IDataExchangeService)) is IDataExchangeService dataExchange)
                services.AddSingleton(dataExchange);
            if (hostServices.GetService(typeof(ISearchService)) is ISearchService searchService)
                services.AddSingleton(searchService);
            if (hostServices.GetService(typeof(IDistributedLockService)) is IDistributedLockService lockService)
                services.AddSingleton(lockService);
            if (hostServices.GetService(typeof(IAppHubNotifier)) is IAppHubNotifier appHubNotifier)
                services.AddSingleton(appHubNotifier);
            module.ConfigureServices(services, handle);
            foreach (var startup in startups)
                startup.ConfigureServices(services, handle);

            var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
            handle.Services = provider;

            var collector = new EndpointCollector();
            module.MapEndpoints(collector, provider);
            foreach (var startup in startups)
                startup.MapEndpoints(collector, provider);
            handle.Definitions = collector.Definitions
                .Select(d => new ModuleEndpointDefinition
                {
                    Pattern = d.Pattern,
                    HttpMethod = d.HttpMethod,
                    Permission = d.Permission,
                    Handler = Wrap(handle, d.Handler)
                })
                .ToArray();

            await module.StartAsync(cancellationToken);

            await RegisterModuleLocalizationAsync(id, entry.Descriptor.PackagePath, provider, cancellationToken);

            // 持久化 ILocalizationStore 的桥接器挂载推迟到宿主数据钩子（ModuleDataHooks.OnLoadedAsync）：
            // LoadAllAsync 会连续 LoadAsync 全部模块，而建表迁移在全部加载完才由钩子逐模块执行；
            // 若在此处 attach，后加载的模块（如 catalog）同步词条时会命中尚未迁移的 LocResources 表
            // → SQLite Error 1: 'no such table: LocResources'。钩子里的迁移步骤会从 ILocalizationRegistry
            // 统一同步包括本模块在内的全部存量词条，因此推迟挂载不会丢词条。

            // 扫描模块程序集中标记 [Subscribe] 或实现 IConsumer<T> 的处理器，注册到宿主 EventBus 的弱引用表。
            // 强引用由 handle.EventHandlers 持有（生命周期跟随模块）；卸载时显式摘除订阅。
            var eventBus = hostServices.GetService<EventBus>();
            if (eventBus is not null)
            {
                var handlerTypes = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract
                        && (t.GetCustomAttributes(typeof(SubscribeAttribute), inherit: false).Length > 0
                            || t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>))))
                    .ToArray();
                if (handlerTypes.Length > 0)
                {
                    var handlers = new List<object>(handlerTypes.Length);
                    foreach (var handlerType in handlerTypes)
                    {
                        var handler = ActivatorUtilities.CreateInstance(provider, handlerType);
                        eventBus.SubscribeAll(handler, id);
                        handlers.Add(handler);
                    }
                    handle.EventHandlers = handlers;
                }
            }

            // 扫描并注册模块计划任务（IScheduleTask）
            if (hostServices.GetService<IScheduleTaskManager>() is { } taskManager)
            {
                var taskTypes = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && typeof(IScheduleTask).IsAssignableFrom(t))
                    .ToArray();
                foreach (var taskType in taskTypes)
                {
                    var taskInstance = (IScheduleTask)ActivatorUtilities.CreateInstance(provider, taskType);
                    taskManager.Register(id, $"{id}:{taskType.FullName}", taskInstance);
                }
            }

            // 扫描并注册模块策略提供者（IProvider）与模块包内静态资源目录
            if (hostServices.GetService<IProviderManager>() is ModuleProviderManager pm)
                pm.RegisterModuleProviders(id, assembly, provider);
            if (hostServices.GetService<IAppHubExtensionRegistry>() is { } hubRegistry)
            {
                var extensions = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && typeof(IAppHubExtension).IsAssignableFrom(t))
                    .Select(t => (IAppHubExtension)ActivatorUtilities.CreateInstance(provider, t))
                    .ToArray();
                hubRegistry.RegisterModule(id, extensions);
            }
            if (hostServices.GetService<ModularAssetManager>() is { } assetMgr)
                assetMgr.RegisterModule(id, entry.Descriptor.PackagePath);

            // 触发首次安装或跨版本升级业务钩子（IModuleLifecycleHooks）
            await RunInstallOrUpdateHooksAsync(id, entry.Descriptor.Version, module, assembly, provider, handle, cancellationToken);

            registry.Register(id, entry.Descriptor.Version, handle.Definitions);
            _handles[id] = handle;
            catalog.SetState(id, ModuleState.Running);
            stateStore?.Save(id, entry.Descriptor.Version, isRunning: true);
            audit.Record(new ModuleAuditEntry(DateTimeOffset.UtcNow, id, entry.Descriptor.Version, "Load", "OK",
                $"{handle.Definitions.Count} endpoints"));
            return Get(id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Module load failed. Id={Id}", id);
            registry.Remove(id);
            (hostServices.GetService<IProviderManager>() as ModuleProviderManager)?.UnregisterModule(id);
            hostServices.GetService<IAppHubExtensionRegistry>()?.UnregisterModule(id);
            hostServices.GetService<ModularAssetManager>()?.UnregisterModule(id);
            WidgetZoneRegistry.UnregisterModule(id);
            DomainRouteResolverRegistry.UnregisterModule(id);
            hostServices.GetService<IScheduleTaskManager>()?.UnregisterModule(id);
            hostServices.GetService<ILocalizationStoreBridge>()?.Detach(id);
            hostServices.GetService<EventBus>()?.UnsubscribeModule(id);
            handle.EventHandlers = null;
            await SafeDisposeAsync(handle, cancellationToken);
            handle.Clear();
            alc.Unload();
            _lastUnloaded[id] = handle.AlcReference!;
            catalog.SetState(id, ModuleState.Failed, ex.Message);
            throw;
        }
    }

    public async Task<WeakReference> UnloadAsync(string id, CancellationToken cancellationToken)
    {
        if (!_handles.TryGetValue(id, out var handle))
            throw new InvalidOperationException($"Module '{id}' is not loaded.");

        // 必要模块不允许卸载（平台增强于 nopCommerce）。
        if (catalog.TryGet(id, out var entry) && entry.Manifest.IsEssential)
            throw new InvalidOperationException($"Module '{id}' is essential and cannot be unloaded.");

        _handles.TryRemove(id, out _);
        catalog.SetState(id, ModuleState.Draining);
        Interlocked.Exchange(ref handle.Accepting, 0);

        try
        {
            await handle.Module!.StopAcceptingAsync(cancellationToken);
            // 先从路由快照移除 Endpoint：新请求立刻 404，旧请求继续执行到结束。
            registry.Remove(id);

            var deadline = DateTime.UtcNow.AddSeconds(Math.Max(1, options.Value.DrainTimeoutSeconds));
            while (Volatile.Read(ref handle.ActiveRequests) > 0)
            {
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException($"Drain timeout for module '{id}' ({handle.ActiveRequests} in-flight requests).");
                await Task.Delay(20, cancellationToken);
            }

            catalog.SetState(id, ModuleState.Unloading);
            await handle.Module.DrainAsync(cancellationToken);
            await handle.Module.StopAsync(cancellationToken);

            // 停止接收该模块的新事件、计划任务与 SignalR Hub 扩展，并释放 handler 强引用。
            hostServices.GetService<IAppHubExtensionRegistry>()?.UnregisterModule(id);
            hostServices.GetService<IScheduleTaskManager>()?.UnregisterModule(id);
            hostServices.GetService<EventBus>()?.UnsubscribeModule(id);
            handle.EventHandlers = null;

            // 注销该模块的多语言资源并解绑宿主 Store 桥接器、清理缓存 Tag，防止 ALC 卸载泄漏。
            hostServices.GetService<ILocalizationStoreBridge>()?.Detach(id);
            hostServices.GetService<ILocalizationRegistry>()?.UnregisterModule(id);
            if (hostServices.GetService<ICacheService>() is { } cache)
                await cache.RemoveByTagAsync($"localization:{id}", cancellationToken);

            await handle.Module.DisposeAsync();
            await handle.Services!.DisposeAsync();

            var weak = handle.AlcReference!;
            var alc = handle.Alc!;
            handle.Clear();
            alc.Unload();
            _lastUnloaded[id] = weak;
            // 卸载仅代表「停用并释放 ALC」，包仍在 modules/installed 中，回到 Installed 以便再次 Load。
            catalog.SetState(id, ModuleState.Installed);
            stateStore?.Save(id, handle.Version, isRunning: false);
            audit.Record(new ModuleAuditEntry(DateTimeOffset.UtcNow, id, handle.Version, "Unload", "OK", null));
            logger.LogInformation("Module unloaded. Id={Id}, Version={Version} (Unload() is cooperative)", id, handle.Version);
            return weak;
        }
        catch (Exception ex)
        {
            catalog.SetState(id, ModuleState.Failed, ex.Message);
            registry.Remove(id);
            (hostServices.GetService<IProviderManager>() as ModuleProviderManager)?.UnregisterModule(id);
            hostServices.GetService<IAppHubExtensionRegistry>()?.UnregisterModule(id);
            hostServices.GetService<ModularAssetManager>()?.UnregisterModule(id);
            WidgetZoneRegistry.UnregisterModule(id);
        DomainRouteResolverRegistry.UnregisterModule(id);
            hostServices.GetService<IScheduleTaskManager>()?.UnregisterModule(id);
            hostServices.GetService<ILocalizationStoreBridge>()?.Detach(id);
            hostServices.GetService<EventBus>()?.UnsubscribeModule(id);
            handle.EventHandlers = null;
            await SafeDisposeAsync(handle, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// 彻底卸载模块：触发 IModuleLifecycleHooks.UninstallAsync 业务清理钩子，
    /// 停用并释放 ALC，清除生命周期版本记录并移除磁盘包目录。
    /// </summary>
    public async Task UninstallAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!catalog.TryGet(id, out var entry))
            throw new InvalidOperationException($"Module '{id}' not found in catalog.");
        if (entry.Manifest.IsEssential)
            throw new InvalidOperationException($"Module '{id}' is essential and cannot be uninstalled.");

        if (_handles.TryGetValue(id, out var runningHandle))
        {
            await RunUninstallHooksAsync(
                runningHandle.Module!,
                runningHandle.Module!.GetType().Assembly,
                runningHandle.Services!,
                runningHandle,
                cancellationToken);
            await UnloadAsync(id, cancellationToken);
        }
        else
        {
            var entryAssemblyPath = Path.Combine(entry.Descriptor.PackagePath, entry.Manifest.EntryAssembly);
            if (File.Exists(entryAssemblyPath))
            {
                var tempAlc = loadContextFactory.Create(id, entry.Descriptor.Version, entryAssemblyPath);
                try
                {
                    var assembly = tempAlc.LoadAssemblyNoLock(entryAssemblyPath);
                    var entryType = assembly.GetType(entry.Manifest.EntryType, throwOnError: true)!;
                    var module = (IModule)Activator.CreateInstance(entryType)!;
                    await using var sp = new ServiceCollection().BuildServiceProvider();
                    var tempCtx = new UninstallModuleContext(id, entry.Descriptor.Version, hostServices, loggerFactory, audit, cancellationToken);
                    await RunUninstallHooksAsync(module, assembly, sp, tempCtx, cancellationToken);
                    await module.DisposeAsync();
                }
                finally
                {
                    tempAlc.Unload();
                }
            }
        }

        var stateFile = Path.Combine(AppContext.BaseDirectory, "data", "module-lifecycle-versions.json");
        var versions = LoadLifecycleVersions(stateFile);
        if (versions.Remove(id))
            SaveLifecycleVersions(stateFile, versions);

        foreach (var verEntry in catalog.ListVersions(id))
        {
            var pkgPath = verEntry.Descriptor.PackagePath;
            if (!string.IsNullOrWhiteSpace(pkgPath) && Directory.Exists(pkgPath))
            {
                try { Directory.Delete(pkgPath, recursive: true); }
                catch (Exception ex) { logger.LogWarning(ex, "清理已卸载模块目录失败：{Path}", pkgPath); }
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.Descriptor.PackagePath))
        {
            var moduleRootDir = Directory.GetParent(entry.Descriptor.PackagePath);
            if (moduleRootDir is not null &&
                string.Equals(moduleRootDir.Name, id, StringComparison.OrdinalIgnoreCase) &&
                moduleRootDir.Exists &&
                !moduleRootDir.EnumerateFileSystemInfos().Any())
            {
                try { moduleRootDir.Delete(recursive: false); }
                catch (Exception ex) { logger.LogWarning(ex, "清理空模块根目录失败：{Path}", moduleRootDir.FullName); }
            }
        }

        catalog.Remove(id);
        stateStore?.Save(id, entry.Descriptor.Version, isRunning: false);
        audit.Record(new ModuleAuditEntry(DateTimeOffset.UtcNow, id, entry.Descriptor.Version, "Uninstall", "OK", null));
    }

    private sealed class UninstallModuleContext(
        string moduleId,
        string version,
        IServiceProvider hostServices,
        ILoggerFactory loggerFactory,
        IModuleAudit audit,
        CancellationToken hostStopping) : IModuleContext
    {
        public string ModuleId { get; } = moduleId;
        public string Version { get; } = version;
        public IServiceProvider HostServices { get; } = hostServices;
        public ILoggerFactory LoggerFactory { get; } = loggerFactory;
        public IModuleAudit Audit { get; } = audit;
        public CancellationToken HostStopping { get; } = hostStopping;
    }

    private static async Task RunUninstallHooksAsync(
        IModule module,
        System.Reflection.Assembly assembly,
        IServiceProvider provider,
        IModuleContext context,
        CancellationToken ct)
    {
        var hookInstances = new List<IModuleLifecycleHooks>();
        if (module is IModuleLifecycleHooks entryHook)
            hookInstances.Add(entryHook);

        var extraHookTypes = assembly.GetTypes()
            .Where(t => t != module.GetType()
                && t.IsClass
                && !t.IsAbstract
                && typeof(IModuleLifecycleHooks).IsAssignableFrom(t))
            .ToArray();
        foreach (var t in extraHookTypes)
            hookInstances.Add((IModuleLifecycleHooks)ActivatorUtilities.CreateInstance(provider, t));

        foreach (var hook in hookInstances)
            await hook.UninstallAsync(context, ct);
    }

    private static async Task RunInstallOrUpdateHooksAsync(
        string id,
        string targetVersion,
        IModule module,
        System.Reflection.Assembly assembly,
        IServiceProvider provider,
        IModuleContext context,
        CancellationToken ct)
    {
        var hookInstances = new List<IModuleLifecycleHooks>();
        if (module is IModuleLifecycleHooks entryHook)
            hookInstances.Add(entryHook);

        var extraHookTypes = assembly.GetTypes()
            .Where(t => t != module.GetType()
                && t.IsClass
                && !t.IsAbstract
                && typeof(IModuleLifecycleHooks).IsAssignableFrom(t))
            .ToArray();
        foreach (var t in extraHookTypes)
            hookInstances.Add((IModuleLifecycleHooks)ActivatorUtilities.CreateInstance(provider, t));

        var stateFile = Path.Combine(AppContext.BaseDirectory, "data", "module-lifecycle-versions.json");
        var versions = LoadLifecycleVersions(stateFile);
        versions.TryGetValue(id, out var previousVersion);

        if (hookInstances.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(previousVersion))
            {
                foreach (var hook in hookInstances)
                    await hook.InstallAsync(context, ct);
            }
            else if (!string.Equals(previousVersion, targetVersion, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var hook in hookInstances)
                    await hook.UpdateAsync(context, previousVersion, targetVersion, ct);
            }
        }

        if (!string.Equals(previousVersion, targetVersion, StringComparison.OrdinalIgnoreCase))
        {
            versions[id] = targetVersion;
            SaveLifecycleVersions(stateFile, versions);
        }
    }

    private static Dictionary<string, string> LoadLifecycleVersions(string filePath)
    {
        if (!File.Exists(filePath))
            return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveLifecycleVersions(string filePath, Dictionary<string, string> versions)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var json = JsonSerializer.Serialize(versions, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }

    private static RequestDelegate Wrap(ModuleHandle handle, RequestDelegate inner) => async context =>
    {
        if (!handle.TryEnter())
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        try
        {
            await inner(context);
        }
        finally
        {
            handle.Exit();
        }
    };

    private static async Task SafeDisposeAsync(ModuleHandle handle, CancellationToken cancellationToken)
    {
        try
        {
            if (handle.Module is not null)
            {
                await handle.Module.StopAsync(cancellationToken);
                await handle.Module.DisposeAsync();
            }
            if (handle.Services is not null)
                await handle.Services.DisposeAsync();
        }
        catch (Exception ex)
        {
            // 清理路径不能覆盖主异常，也不静默失败：完整异常写入审计与日志。
            handle.Audit.Record(new ModuleAuditEntry(DateTimeOffset.UtcNow, handle.ModuleId, handle.Version,
                "Cleanup", "Failed", ex.ToString()));
            handle.LoggerFactory.CreateLogger(nameof(ModuleLifecycleManager))
                .LogError(ex, "Module cleanup failed. Id={Id}", handle.ModuleId);
        }
    }

    private async Task RegisterModuleLocalizationAsync(
        string moduleId,
        string packagePath,
        IServiceProvider moduleProvider,
        CancellationToken cancellationToken)
    {
        var registry = hostServices.GetService<ILocalizationRegistry>();
        if (registry is null)
            return;

        var collected = new List<LocaleResourceEntry>();

        // 1) 扫描模块包 localization/{culture}.json
        var locDir = Path.Combine(packagePath, ModulePackageLayout.LocalizationFolder);
        if (Directory.Exists(locDir))
        {
            foreach (var file in Directory.EnumerateFiles(locDir, "*.json"))
            {
                var culture = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(culture))
                    continue;

                try
                {
                    await using var stream = File.OpenRead(file);
                    var dict = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, cancellationToken: cancellationToken);
                    if (dict is { Count: > 0 })
                    {
                        registry.RegisterModuleResources(moduleId, culture, dict);
                        foreach (var (key, value) in dict)
                            collected.Add(new LocaleResourceEntry(moduleId, culture, key, value));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "读取模块 {ModuleId} 多语言文件失败：{File}", moduleId, file);
                }
            }
        }

        // 2) 收集模块 DI 中注册的 IModuleLocalizationProvider
        foreach (var provider in moduleProvider.GetServices<IModuleLocalizationProvider>())
        {
            var byCulture = provider.GetResources();
            registry.RegisterModuleResources(moduleId, byCulture);
            foreach (var (culture, entries) in byCulture)
            {
                foreach (var (key, value) in entries)
                    collected.Add(new LocaleResourceEntry(moduleId, culture, key, value));
            }
        }

        // 3) 若宿主已接入持久化 ILocalizationStore（Localization 管理模块），同步入库
        if (collected.Count > 0 && hostServices.GetService<ILocalizationStore>() is { } store)
            await store.SyncModuleResourcesAsync(moduleId, collected, cancellationToken);
    }
}
