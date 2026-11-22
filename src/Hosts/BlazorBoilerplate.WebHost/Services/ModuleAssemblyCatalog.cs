using System.Collections.Immutable;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.WebHost.Services;

public enum ModuleRenderStrategy
{
    Auto,
    Server
}

public sealed class ServerRenderModeArbitrator(ModuleAssemblyCatalog catalog) : BlazorBoilerplate.Web.Shared.Services.IRenderModeArbitrator
{
    public bool ShouldUseWebAssembly(string? path) => catalog.ShouldUseWebAssembly(path);
}

/// <summary>
/// 动态模块前端程序集目录（运行时注入，不写死任何静态模块引用）。
/// 模块是前后端同体 RCL：页面组件与后端服务在同一个 DLL 里。
///
/// 两条渲染路径都支持运行时换版本（无需重启 WebHost）：
/// - <b>SSR 首屏</b>：ModuleHost.razor（@page "/{**path}" catch-all）调用 <see cref="ResolveComponent"/>，
///   从当前激活程序集解析组件后 DynamicComponent 渲染 —— endpoint 固定，组件动态。
/// - <b>Interactive 线路导航</b>：Router.AdditionalAssemblies 绑定 <see cref="Assemblies"/>；
///   Router 每次导航按程序集序列内容比较 RouteKey，集合替换后自动重建路由表，新导航即命中新版本。
/// </summary>
public sealed class ModuleAssemblyCatalog
{
    private readonly HttpClient _http;
    private readonly ILogger<ModuleAssemblyCatalog> _logger;
    private readonly Uri _apiBase;
    private readonly object _gate = new();

    /// <summary>当前激活条目（moduleId → 程序集 + 可卸载 ALC；包版本更新时替换并卸载旧 ALC）。</summary>
    private readonly Dictionary<string, ModuleEntry> _active = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>暴露给 Router.AdditionalAssemblies 的当前集合（整体替换引用）。</summary>
    private ImmutableArray<Assembly> _assemblies = [];
    private long _lastLoadAttemptTicks;

    /// <summary>路由解析缓存（assembly → 模板/组件映射），程序集被 GC 回收后条目随之消失。</summary>
    private readonly ConditionalWeakTable<Assembly, Dictionary<string, Type>> _routeCache = new();

    public ModuleAssemblyCatalog(HttpClient http, IConfiguration configuration,
        ILogger<ModuleAssemblyCatalog> logger)
    {
        _http = http;
        _logger = logger;
        _apiBase = new Uri(configuration["ApiHost:BaseUrl"] ?? "http://localhost:5100");
    }

    /// <summary>Router.AdditionalAssemblies 绑定此属性；Router 每次导航按内容比较并按需重建路由表。</summary>
    public IReadOnlyList<Assembly> Assemblies
    {
        get
        {
            EnsureLoadedOnDemand();
            return _assemblies;
        }
    }

    private void EnsureLoadedOnDemand()
    {
        if (!_assemblies.IsEmpty) return;
        var now = Environment.TickCount64;
        var last = Volatile.Read(ref _lastLoadAttemptTicks);
        if (last != 0 && now - last < 3000) return;
        if (Interlocked.CompareExchange(ref _lastLoadAttemptTicks, now, last) == last)
            LoadAll();
    }

    /// <summary>启动时装载：从 ApiHost `/api/modules` 动态发现所有 Running 模块并加载其前端程序集。</summary>
    public void LoadAll()
    {
        try
        {
            var modules = _http.GetFromJsonAsync<List<ModuleDto>>(new Uri(_apiBase, "/api/modules"),
                JsonOptions).GetAwaiter().GetResult();
            if (modules is null) return;
            foreach (var m in modules)
            {
                if (m.State == BlazorBoilerplate.ModuleAbstractions.ModuleState.Running)
                    LoadFromPackage(m.Id, m.PackagePath);
            }
        }
        catch (Exception ex)
        {
            // ApiHost 尚未就绪时记录告警，后续请求/导航时可按需重试加载。
            _logger.LogWarning(ex, "启动时装载模块前端程序集失败（ApiHost 不可达？）");
        }
        lock (_gate)
            _assemblies = [.. _active.Values.Select(e => e.Assembly)];
        _logger.LogInformation("模块前端程序集目录就绪：{Modules}",
            string.Join(", ", _active.Select(kv => $"{kv.Key}={kv.Value.Assembly.GetName().Name} v{kv.Value.Assembly.GetName().Version}")));
    }

    /// <summary>
    /// 扫描已加载模块前端程序集中的 IModuleClientStartup（按 Order 升序）以及
    /// IClientService / ITransientClientService 标记服务，注册到 WebHost Blazor DI 容器。
    /// </summary>
    public void RegisterClientServices(IServiceCollection services)
    {
        var assemblies = _assemblies.Length > 0
            ? _assemblies.ToArray()
            : AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && (a.GetName().Name?.Contains(".Modules.", StringComparison.OrdinalIgnoreCase) == true))
                .ToArray();

        var startups = new List<BlazorBoilerplate.ModuleAbstractions.IModuleClientStartup>();
        foreach (var asm in assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

            foreach (var t in types.Where(t => t.IsClass && !t.IsAbstract))
            {
                if (typeof(BlazorBoilerplate.ModuleAbstractions.IModuleClientStartup).IsAssignableFrom(t) &&
                    t.GetConstructor(Type.EmptyTypes) is not null)
                {
                    startups.Add((BlazorBoilerplate.ModuleAbstractions.IModuleClientStartup)Activator.CreateInstance(t)!);
                }

                if (typeof(BlazorBoilerplate.ModuleAbstractions.IClientService).IsAssignableFrom(t))
                    RegisterMarkedService(services, t, ServiceLifetime.Scoped);
                else if (typeof(BlazorBoilerplate.ModuleAbstractions.ITransientClientService).IsAssignableFrom(t))
                    RegisterMarkedService(services, t, ServiceLifetime.Transient);
            }
        }

        foreach (var s in startups.OrderBy(x => x.Order).ThenBy(x => x.GetType().FullName, StringComparer.Ordinal))
            s.ConfigureServices(services);
    }

    private static void RegisterMarkedService(IServiceCollection services, Type implType, ServiceLifetime lifetime)
    {
        var interfaces = implType.GetInterfaces()
            .Where(i => i != typeof(BlazorBoilerplate.ModuleAbstractions.IClientService)
                     && i != typeof(BlazorBoilerplate.ModuleAbstractions.ITransientClientService))
            .ToArray();

        if (interfaces.Length > 0)
        {
            foreach (var iface in interfaces)
                services.Add(new ServiceDescriptor(iface, implType, lifetime));
        }
        else
        {
            services.Add(new ServiceDescriptor(implType, implType, lifetime));
        }
    }

    /// <summary>
    /// 运行时刷新单个模块（上传新版本 / 切换激活版本 / 加载或卸载成功后由页面调用，进程内即时生效）。
    /// 返回 true 表示程序集发生了替换或移除。
    /// </summary>
    public async Task<bool> RefreshModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(new Uri(_apiBase, $"/api/modules/{moduleId}"), cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return UnloadModule(moduleId);

            response.EnsureSuccessStatusCode();
            var descriptor = await response.Content.ReadFromJsonAsync<ModuleDto>(JsonOptions, cancellationToken);
            if (descriptor is null || descriptor.State != BlazorBoilerplate.ModuleAbstractions.ModuleState.Running)
                return UnloadModule(moduleId);

            // UI 显式切版本/加载后的刷新：允许回退降级（以 ApiHost 激活版本为准）。
            return LoadFromPackage(moduleId, descriptor.PackagePath, allowDowngrade: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "刷新模块 {ModuleId} 前端程序集失败", moduleId);
            return false;
        }
    }

    /// <summary>按路由模板解析组件类型（ModuleHost catch-all 使用）。未匹配返回 null。</summary>
    public Type? ResolveComponent(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/") return null;
        if (_assemblies.IsEmpty) LoadAll();
        var normalized = path.TrimEnd('/');
        if (normalized.Length == 0) normalized = "/";

        foreach (var asm in _assemblies)
        {
            var routes = _routeCache.GetValue(asm, BuildRouteMap);
            if (routes.TryGetValue(normalized, out var type)) return type;
        }
        return null;
    }

    private bool UnloadModule(string moduleId)
    {
        BlazorBoilerplate.ModuleAbstractions.WidgetZoneRegistry.UnregisterModule(moduleId);
        BlazorBoilerplate.ModuleAbstractions.DomainRouteResolverRegistry.UnregisterModule(moduleId);

        ModuleEntry? current;
        lock (_gate)
        {
            if (!_active.Remove(moduleId, out current))
                return false;
            _assemblies = [.. _active.Values.Select(e => e.Assembly)];
        }

        if (current.Context is not null)
        {
            try
            {
                current.Context.Unload();
                _logger.LogInformation("模块 {ModuleId} v{OldVersion} 的前端 ALC 已请求卸载", moduleId, current.Version);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "模块 {ModuleId} 前端 ALC 卸载请求失败", moduleId);
            }
        }
        return true;
    }

    /// <param name="allowDowngrade">
    /// 启动装载（false）：只进不退，避免重复加载同版本。
    /// 显式刷新（true，UI 切版本后调用）：以 ApiHost 激活版本为准，允许回退降级。
    /// </param>
    private bool LoadFromPackage(string moduleId, string? packagePath, bool allowDowngrade = false)
    {
        if (string.IsNullOrWhiteSpace(packagePath)) return false;
        ModuleEntry? current;
        lock (_gate)
            current = _active.GetValueOrDefault(moduleId);

        var dll = ResolveEntryDllPath(moduleId, packagePath);
        if (dll is null || !File.Exists(dll)) return false;

        var packageVersion = AssemblyName.GetAssemblyName(dll).Version;
        if (!allowDowngrade && current is not null && packageVersion <= current.Assembly.GetName().Version)
        {
            _logger.LogInformation("模块 {ModuleId} 包版本 v{PackageVersion} 不高于当前 v{CurrentVersion}，保持现状",
                moduleId, packageVersion, current.Assembly.GetName().Version);
            return false;
        }

        // 参照官方插件模式：collectible ALC + AssemblyDependencyResolver（按包内 deps.json 解析私有依赖）。
        // resolver 解析不到的共享依赖（ModuleAbstractions / Bit.BlazorUI 等）回退默认 ALC —— 保证类型同一实例。
        var context = new ModuleLoadContext(dll);
        Assembly loaded;
        try
        {
            using var dllStream = new MemoryStream(File.ReadAllBytes(dll));
            var pdb = Path.ChangeExtension(dll, ".pdb");
            if (File.Exists(pdb))
            {
                using var pdbStream = new MemoryStream(File.ReadAllBytes(pdb));
                loaded = context.LoadFromStream(dllStream, pdbStream);
            }
            else
            {
                loaded = context.LoadFromStream(dllStream);
            }
        }
        catch
        {
            context.Unload(); // 加载失败立即释放，避免留下孤儿 ALC
            throw;
        }

        var cssPath = ResolveScopedCssPath(dll);
        var isFrontendOnly = dll.Contains($"{Path.DirectorySeparatorChar}frontend{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileNameWithoutExtension(dll).EndsWith(".Frontend", StringComparison.OrdinalIgnoreCase);
        // 纯前端模块统一走 Auto（WASM）；identity 的 /sign-in 等公共认证壳由 IsServerOnlyRoute 固定走 Server 兜底。
        var strategy = !isFrontendOnly ? ModuleRenderStrategy.Server : ModuleRenderStrategy.Auto;
        var versionStr = packageVersion?.ToString() ?? "0.0.0.0";
        BlazorBoilerplate.ModuleAbstractions.WidgetZoneRegistry.UnregisterModule(moduleId);
        BlazorBoilerplate.ModuleAbstractions.DomainRouteResolverRegistry.UnregisterModule(moduleId);
        lock (_gate)
        {
            _active[moduleId] = new ModuleEntry(loaded, context, packageVersion, cssPath, dll, strategy, isFrontendOnly);
            _assemblies = [.. _active.Values.Select(e => e.Assembly)]; // 整体替换引用：Router 下次导航重建路由表
        }

        // 每次装载（启动装载 / 按需装载 / 显式刷新切版本）都必须执行 IModuleClientStartup：
        // WidgetZoneRegistry、DomainRouteResolverRegistry 等是进程内静态注册表（不进 DI 容器），
        // 程序集一旦装载就必须同步刷新，否则当 WebHost 先于 ApiHost 启动时，启动装载失败、
        // 后续按需装载又因 allowDowngrade=false 跳过 startup，静态注册表会一直为空，
        // 症状是域名路由完全失效（DomainRouteResolverRegistry.Current == null）。
        // 上方 281-282 行已先 UnregisterModule，故重复执行是幂等的。
        Type[] loadedTypes;
        try { loadedTypes = loaded.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { loadedTypes = ex.Types.Where(t => t is not null).ToArray()!; }

        var dummyServices = new ServiceCollection();
        foreach (var startup in loadedTypes
            .Where(t => t.IsClass && !t.IsAbstract
                && typeof(BlazorBoilerplate.ModuleAbstractions.IModuleClientStartup).IsAssignableFrom(t)
                && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (BlazorBoilerplate.ModuleAbstractions.IModuleClientStartup)Activator.CreateInstance(t)!)
            .OrderBy(s => s.Order)
            .ThenBy(s => s.GetType().FullName, StringComparer.Ordinal))
        {
            startup.ConfigureServices(dummyServices);
        }

        // 卸载旧的可回收 ALC。旧组件实例仍被活跃线路引用时卸载请求挂起，
        // 等引用释放（线路关闭 + GC）后自动完成 —— 官方 unloadability 模式的标准行为。
        if (current?.Context is not null)
        {
            try
            {
                current.Context.Unload();
                _logger.LogInformation("模块 {ModuleId} 旧版本 v{OldVersion} 的 ALC 已请求卸载（等待活跃线路释放引用）",
                    moduleId, current.Version);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "模块 {ModuleId} 旧 ALC 卸载请求失败（忽略，不影响新版本生效）", moduleId);
            }
        }

        _logger.LogInformation("模块 {ModuleId} 前端程序集已切换：v{PackageVersion}（{Path}）", moduleId, packageVersion, dll);
        return true;
    }

    private static string? ResolveEntryDllPath(string moduleId, string packagePath)
    {
        var manifestPath = Path.Combine(packagePath, "manifest.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (doc.RootElement.TryGetProperty("frontend", out var frontend) &&
                    frontend.TryGetProperty("assembly", out var frontendAsm))
                {
                    var asmName = frontendAsm.GetString();
                    if (!string.IsNullOrWhiteSpace(asmName))
                    {
                        var frontendDll = Path.Combine(packagePath, "frontend", Path.GetFileName(asmName));
                        if (File.Exists(frontendDll))
                            return frontendDll;
                    }
                }

                if (doc.RootElement.TryGetProperty("entryAssembly", out var prop))
                {
                    var entry = prop.GetString();
                    if (!string.IsNullOrWhiteSpace(entry))
                        return Path.Combine(packagePath, "backend", Path.GetFileName(entry));
                }
            }
            catch
            {
                // manifest 解析失败时回退命名约定
            }
        }

        var id = char.ToUpperInvariant(moduleId[0]) + moduleId[1..];
        return Path.Combine(packagePath, "backend", $"BlazorBoilerplate.Modules.{id}.dll");
    }

    /// <summary>聚合全部已加载动态模块前端程序集的隔离 CSS，供 /_modules/styles.css 输出。</summary>
    public string GetCombinedCss()
    {
        EnsureLoadedOnDemand();
        List<string> paths;
        lock (_gate)
        {
            paths = _active.Values
                .Select(e => e.CssPath)
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                .Cast<string>()
                .ToList();
        }

        if (paths.Count == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var path in paths)
        {
            sb.AppendLine($"/* {Path.GetFileName(path)} */");
            sb.AppendLine(File.ReadAllText(path));
        }
        return sb.ToString();
    }

    private static string? ResolveScopedCssPath(string dllPath)
    {
        var dir = Path.GetDirectoryName(dllPath);
        if (string.IsNullOrEmpty(dir)) return null;
        var baseName = Path.GetFileNameWithoutExtension(dllPath);

        var packaged = Path.Combine(dir, baseName + ".styles.css");
        if (File.Exists(packaged)) return packaged;

        // 开发期：从 modules/installed/{id}/{ver}/frontend 向上找到仓库根，匹配 src/Modules/*/{baseName}/obj/*/net10.0/scopedcss
        var cursor = dir;
        for (var i = 0; i < 6 && !string.IsNullOrEmpty(cursor); i++)
        {
            var modulesSrc = Path.Combine(cursor, "src", "Modules");
            if (Directory.Exists(modulesSrc))
            {
                foreach (var modDir in Directory.EnumerateDirectories(modulesSrc))
                {
                    foreach (var config in new[] { "Debug", "Release" })
                    {
                        var projectBundle = Path.Combine(modDir, baseName, "obj", config, "net10.0", "scopedcss", "projectbundle", baseName + ".bundle.scp.css");
                        if (File.Exists(projectBundle)) return projectBundle;
                    }
                }
                break;
            }
            cursor = Path.GetDirectoryName(cursor);
        }
        return null;
    }

    /// <summary>激活条目：程序集 + 可回收 ALC + 版本 + CSS + DLL 路径 + 渲染策略。</summary>
    private sealed record ModuleEntry(
        Assembly Assembly,
        ModuleLoadContext? Context = null,
        Version? Version = null,
        string? CssPath = null,
        string? DllPath = null,
        ModuleRenderStrategy RenderStrategy = ModuleRenderStrategy.Auto,
        bool IsFrontendOnly = false);

    public string ManifestHash
    {
        get
        {
            EnsureLoadedOnDemand();
            lock (_gate)
            {
                var raw = string.Join("|", _active.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => $"{kv.Key}:{kv.Value.Version}:{kv.Value.RenderStrategy}"));
                return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)))[..12];
            }
        }
    }

    public bool ShouldUseWebAssembly(string? path)
    {
        EnsureLoadedOnDemand();
        var clean = (path ?? "/").Split('?', '#')[0];
        var normalized = clean.TrimEnd('/');
        if (normalized.Length == 0) normalized = "/";

        if (IsServerOnlyRoute(normalized))
            return false;

        lock (_gate)
        {
            foreach (var kv in _active)
            {
                var entry = kv.Value;
                if (entry.IsFrontendOnly && entry.RenderStrategy == ModuleRenderStrategy.Auto)
                    continue;
                var routes = _routeCache.GetValue(entry.Assembly, BuildRouteMap);
                if (routes.ContainsKey(normalized))
                    return false;
            }
        }
        return true;
    }

    private static bool IsServerOnlyRoute(string normalized) =>
        normalized.Equals("/sign-in", StringComparison.OrdinalIgnoreCase)
        || normalized.Equals("/sign-up", StringComparison.OrdinalIgnoreCase)
        || normalized.Equals("/forgot-password", StringComparison.OrdinalIgnoreCase)
        || normalized.Equals("/setup", StringComparison.OrdinalIgnoreCase)
        || normalized.Equals("/auth", StringComparison.OrdinalIgnoreCase)
        || normalized.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase);

    public bool IsFrontendOnlyModule(string moduleId)
    {
        lock (_gate)
            return _active.TryGetValue(moduleId, out var entry) && entry.IsFrontendOnly;
    }

    public ModuleRenderStrategy GetRenderStrategy(string moduleId)
    {
        lock (_gate)
            return _active.TryGetValue(moduleId, out var entry) ? entry.RenderStrategy : ModuleRenderStrategy.Server;
    }

    public bool SetRenderStrategy(string moduleId, ModuleRenderStrategy strategy)
    {
        lock (_gate)
        {
            if (!_active.TryGetValue(moduleId, out var entry))
                return false;
            if (strategy == ModuleRenderStrategy.Auto && !entry.IsFrontendOnly)
                return false;
            _active[moduleId] = entry with { RenderStrategy = strategy };
            return true;
        }
    }

    public object GetClientManifest()
    {
        EnsureLoadedOnDemand();
        var hash = ManifestHash;
        List<object> modules = [];
        lock (_gate)
        {
            foreach (var kv in _active.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var entry = kv.Value;
                if (string.IsNullOrWhiteSpace(entry.DllPath)) continue;
                var version = entry.Version?.ToString() ?? "0.0.0.0";
                var routes = _routeCache.GetValue(entry.Assembly, BuildRouteMap).Keys.ToArray();
                var pdbPath = Path.ChangeExtension(entry.DllPath, ".pdb");
                modules.Add(new
                {
                    moduleId = kv.Key,
                    version,
                    renderMode = entry.RenderStrategy == ModuleRenderStrategy.Auto && entry.IsFrontendOnly ? "Auto" : "Server",
                    assemblyFileName = Path.GetFileName(entry.DllPath),
                    hasPdb = File.Exists(pdbPath),
                    cssUrl = $"/_modules/styles.css?v={hash}",
                    routePrefixes = routes
                });
            }
        }
        return new { manifestHash = hash, modules };
    }

    public bool TryGetClientAssemblyFile(string moduleId, string version, string fileName, out string? filePath)
    {
        filePath = null;
        if (string.IsNullOrWhiteSpace(moduleId) || string.IsNullOrWhiteSpace(fileName))
            return false;

        lock (_gate)
        {
            if (!_active.TryGetValue(moduleId, out var entry) || string.IsNullOrWhiteSpace(entry.DllPath))
                return false;
            var currentVersion = entry.Version?.ToString() ?? "0.0.0.0";
            if (!string.Equals(currentVersion, version, StringComparison.OrdinalIgnoreCase))
                return false;

            var safeName = Path.GetFileName(fileName);
            var dllName = Path.GetFileName(entry.DllPath);
            var pdbName = Path.GetFileName(Path.ChangeExtension(entry.DllPath, ".pdb"));
            if (string.Equals(safeName, dllName, StringComparison.OrdinalIgnoreCase) && File.Exists(entry.DllPath))
            {
                filePath = entry.DllPath;
                return true;
            }
            var pdbPath = Path.ChangeExtension(entry.DllPath, ".pdb");
            if (string.Equals(safeName, pdbName, StringComparison.OrdinalIgnoreCase) && File.Exists(pdbPath))
            {
                filePath = pdbPath;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 模块插件 ALC（官方 creating-app-with-plugin-support 模式）：
    /// isCollectible 支持卸载；AssemblyDependencyResolver 按包内 deps.json 把模块<b>私有</b>依赖
    /// 解析到包目录，共享依赖返回 null 落回默认 ALC（与宿主共用同一类型实例，避免类型身份分裂）。
    /// </summary>
    private sealed class ModuleLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public ModuleLoadContext(string dllPath)
            : base($"Module:{Path.GetFileNameWithoutExtension(dllPath)}-{Guid.NewGuid():N}", isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(dllPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path is not null) return LoadFromAssemblyPath(path);

            // 回退默认上下文：按简单名绑定而非 return null（完整身份绑定）。
            // 打包注入 -p:Version 会给共享依赖盖章，版本不匹配会导致 FileNotFound。
            return string.IsNullOrEmpty(assemblyName.Name)
                ? null
                : Assembly.Load(new AssemblyName(assemblyName.Name));
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is not null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
        }
    }

    private static Dictionary<string, Type> BuildRouteMap(Assembly asm)
    {
        var map = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        Type[] types;
        try
        {
            types = asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).Select(t => t!).ToArray();
        }
        foreach (var type in types)
        {
            if (!typeof(IComponent).IsAssignableFrom(type)) continue;
            var route = type.GetCustomAttribute<RouteAttribute>()?.Template;
            if (string.IsNullOrEmpty(route)) continue;
            var normalized = route.TrimEnd('/');
            if (normalized.Length == 0) normalized = "/";
            map[normalized] = type;
        }
        return map;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record ModuleDto(string Id, string? PackagePath, BlazorBoilerplate.ModuleAbstractions.ModuleState State);
}
