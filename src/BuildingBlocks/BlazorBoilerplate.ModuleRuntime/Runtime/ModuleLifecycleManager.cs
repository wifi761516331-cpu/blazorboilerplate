using System.Collections.Concurrent;
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
            var assembly = alc.LoadFromAssemblyPath(package.EntryAssemblyPath);
            var entryType = assembly.GetType(package.Manifest.EntryType, throwOnError: true)!;
            if (!typeof(IModule).IsAssignableFrom(entryType))
                throw new InvalidOperationException(
                    $"Entry type '{package.Manifest.EntryType}' does not implement {nameof(IModule)}. " +
                    "Shared contracts must resolve to the Host default load context.");

            var module = (IModule)Activator.CreateInstance(entryType)!;
            handle.Module = module;

            var services = new ServiceCollection();
            services.AddSingleton(loggerFactory);
            if (handle.HostServices.GetService(typeof(BlazorBoilerplate.Application.Abstractions.ICurrentUserContext))
                is BlazorBoilerplate.Application.Abstractions.ICurrentUserContext currentUser)
            {
                services.AddSingleton(currentUser);
            }
            module.ConfigureServices(services, handle);
            var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
            handle.Services = provider;

            var collector = new EndpointCollector();
            module.MapEndpoints(collector, provider);
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
            await SafeDisposeAsync(handle, cancellationToken);
            throw;
        }
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
}
