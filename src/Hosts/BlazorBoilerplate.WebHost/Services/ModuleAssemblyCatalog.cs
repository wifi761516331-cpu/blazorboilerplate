using System.Collections.Immutable;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.WebHost.Services;

/// <summary>
/// 动态模块前端程序集目录（运行时注入）。
/// 模块是前后端同体 RCL：页面组件与后端服务在同一个 DLL 里。
///
/// 两条渲染路径都支持运行时换版本（无需重启 WebHost）：
/// - <b>SSR 首屏</b>：ModuleHost.razor（@page "/{**path}" catch-all）调用 <see cref="ResolveComponent"/>，
///   从当前激活程序集解析组件后 DynamicComponent 渲染 —— endpoint 固定，组件动态。
/// - <b>Interactive 线路导航</b>：Router.AdditionalAssemblies 绑定 <see cref="Assemblies"/>；
///   Router 每次导航按程序集序列内容比较 RouteKey，集合替换后自动重建路由表，新导航即命中新版本。
///
/// 程序集加载走默认 ALC：打包注入 -p:Version 使程序集身份随包版本变化，多版本可共存；
/// 切换后旧版本程序集暂留内存（默认 ALC 不可卸载，collectible ALC 是后续 POC）。
/// </summary>
public sealed class ModuleAssemblyCatalog
{
    private readonly HttpClient _http;
    private readonly ILogger<ModuleAssemblyCatalog> _logger;
    private readonly Uri _apiBase;

    /// <summary>编译时静态引用的模块程序集（开发回退：改源码即生效；键 = 模块 id）。</summary>
    private static readonly ImmutableDictionary<string, Assembly> StaticByModule =
        new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase)
        {
            ["identity"] = typeof(BlazorBoilerplate.Modules.Identity.IdentityModule).Assembly,
            ["catalog"] = typeof(BlazorBoilerplate.Modules.Catalog.CatalogModule).Assembly,
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前激活条目（moduleId → 程序集 + 可卸载 ALC；初始为静态引用，包版本更新时替换并卸载旧 ALC）。</summary>
    private readonly Dictionary<string, ModuleEntry> _active = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>暴露给 Router.AdditionalAssemblies 的当前集合（整体替换引用）。</summary>
    private ImmutableArray<Assembly> _assemblies = [.. StaticByModule.Values];

    /// <summary>路由解析缓存（assembly → 模板/组件映射），程序集被 GC 回收后条目随之消失。</summary>
    private readonly ConditionalWeakTable<Assembly, Dictionary<string, Type>> _routeCache = new();

    public ModuleAssemblyCatalog(HttpClient http, IConfiguration configuration,
        ILogger<ModuleAssemblyCatalog> logger)
    {
        _http = http;
        _logger = logger;
        _apiBase = new Uri(configuration["ApiHost:BaseUrl"] ?? "http://localhost:5100");
        foreach (var (id, asm) in StaticByModule) _active[id] = new ModuleEntry(asm);
    }

    /// <summary>Router.AdditionalAssemblies 绑定此属性；Router 每次导航按内容比较并按需重建路由表。</summary>
    public IReadOnlyList<Assembly> Assemblies => _assemblies;

    /// <summary>启动时装载：优先 ApiHost 激活版本的包 DLL（版本比编译时新才替换），否则保持静态引用。</summary>
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
            // ApiHost 未启动等：保持静态引用（开发模式），SSR/导航由 ModuleHost 兜底提示。
            _logger.LogWarning(ex, "启动时装载模块前端程序集失败（ApiHost 不可达？），使用编译时静态引用");
        }
        _assemblies = [.. _active.Values.Select(e => e.Assembly)];
        _logger.LogInformation("模块前端程序集目录就绪：{Modules}",
            string.Join(", ", _active.Select(kv => $"{kv.Key}={kv.Value.Assembly.GetName().Name} v{kv.Value.Assembly.GetName().Version}")));
    }

    /// <summary>
    /// 运行时刷新单个模块（上传新版本 / 切换激活版本 / 加载成功后由页面调用，进程内即时生效）。
    /// 返回 true 表示程序集发生了替换。
    /// </summary>
    public async Task<bool> RefreshModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        try
        {
            var descriptor = await _http.GetFromJsonAsync<ModuleDto>(new Uri(_apiBase, $"/api/modules/{moduleId}"),
                JsonOptions, cancellationToken);
            // UI 显式切版本/加载后的刷新：允许回退降级（以 ApiHost 激活版本为准）。
            return LoadFromPackage(moduleId, descriptor?.PackagePath, allowDowngrade: true);
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
        var normalized = path.TrimEnd('/');
        if (normalized.Length == 0) normalized = "/";

        foreach (var asm in _assemblies)
        {
            var routes = _routeCache.GetValue(asm, BuildRouteMap);
            if (routes.TryGetValue(normalized, out var type)) return type;
        }
        return null;
    }

    /// <param name="allowDowngrade">
    /// 启动装载（false）：只进不退 —— 防止磁盘上的旧包覆盖开发编译的静态引用。
    /// 显式刷新（true，UI 切版本后调用）：以 ApiHost 激活版本为准，允许回退降级。
    /// </param>
    private bool LoadFromPackage(string moduleId, string? packagePath, bool allowDowngrade = false)
    {
        if (string.IsNullOrWhiteSpace(packagePath)) return false;
        var current = _active.GetValueOrDefault(moduleId) ?? (StaticByModule.TryGetValue(moduleId, out var s) ? new ModuleEntry(s) : null);

        // manifest.entryAssembly 与包内 backend/ 目录约定：BlazorBoilerplate.Modules.<Id>.dll
        var id = char.ToUpperInvariant(moduleId[0]) + moduleId[1..];
        var dll = Path.Combine(packagePath, "backend", $"BlazorBoilerplate.Modules.{id}.dll");
        if (!File.Exists(dll)) return false;

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
            loaded = context.LoadFromAssemblyPath(dll);
        }
        catch
        {
            context.Unload(); // 加载失败立即释放，避免留下孤儿 ALC
            throw;
        }

        _active[moduleId] = new ModuleEntry(loaded, context, packageVersion);

        // 卸载旧的可回收 ALC（静态引用无 ALC、不卸载）。旧组件实例仍被活跃线路引用时卸载请求挂起，
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

        _assemblies = [.. _active.Values.Select(e => e.Assembly)]; // 整体替换引用：Router 下次导航重建路由表
        _logger.LogInformation("模块 {ModuleId} 前端程序集已切换：v{PackageVersion}（{Path}）", moduleId, packageVersion, dll);
        return true;
    }

    /// <summary>激活条目：程序集 + 承载它的可回收 ALC（静态引用为 null）+ 已加载版本。</summary>
    private sealed record ModuleEntry(Assembly Assembly, ModuleLoadContext? Context = null, Version? Version = null);

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
