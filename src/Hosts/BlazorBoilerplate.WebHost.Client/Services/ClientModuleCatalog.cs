using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Loader;
using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.WebHost.Client.Services;

public sealed record ClientModuleDescriptor(
    string ModuleId,
    string Version,
    string RenderMode,
    string AssemblyFileName,
    bool HasPdb,
    string CssUrl,
    string[] RoutePrefixes);

public sealed record ClientModuleManifest(
    string ManifestHash,
    IReadOnlyList<ClientModuleDescriptor> Modules);

/// <summary>
/// WASM 端模块目录：拉取 /_modules/client/manifest.json，按版本号加载纯前端 DLL；
/// 若模块被服务端降级为 Server 或发布了新版本，返回 RequiresForceReload=true 触发整页刷新重建 WASM 运行时。
/// </summary>
public sealed class ClientModuleCatalog(HttpClient http)
{
    private readonly Dictionary<string, (string Version, Assembly Assembly)> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Assembly> _assemblies = [];
    private ClientModuleManifest? _manifest;

    public IReadOnlyList<Assembly> Assemblies => _assemblies;
    public ClientModuleManifest? Manifest => _manifest;

    public async Task RefreshManifestAsync(CancellationToken ct = default)
    {
        try
        {
            _manifest = await http.GetFromJsonAsync<ClientModuleManifest>("/_modules/client/manifest.json", ct);
        }
        catch
        {
            // 离线或服务端重启瞬间保留旧清单
        }
    }

    public async Task PreloadAutoModulesAsync(IServiceCollection services, CancellationToken ct = default)
    {
        await RefreshManifestAsync(ct);
        if (_manifest is null) return;

        foreach (var mod in _manifest.Modules.Where(m => string.Equals(m.RenderMode, "Auto", StringComparison.OrdinalIgnoreCase)))
        {
            var asm = await LoadModuleAssemblyAsync(mod, ct);
            if (asm is not null)
                RegisterModuleServices(asm, services);
        }
    }

    public ClientModuleDescriptor? FindByPath(string path)
    {
        if (_manifest is null) return null;
        var clean = (path ?? string.Empty).Split('?', '#')[0];
        var normalized = "/" + clean.TrimStart('/');
        return _manifest.Modules.FirstOrDefault(m =>
            m.RoutePrefixes.Any(prefix =>
                normalized.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(prefix.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// 导航前检查：
    /// 1) 目标模块已切为 Server 兜底 → 返回 true（需 forceLoad 切回 InteractiveServer）；
    /// 2) 目标模块在 WASM 中已加载过旧版本且服务端已升级 → 返回 true（需 forceLoad 重建 WASM 运行时）；
    /// 3) 未加载过且为 Auto → 拉取带版本号 DLL 并挂载到 AdditionalAssemblies。
    /// </summary>
    public async Task<bool> EnsureRouteReadyAsync(string path, CancellationToken ct = default)
    {
        await RefreshManifestAsync(ct);
        var target = FindByPath(path);
        if (target is null)
            return false;

        if (string.Equals(target.RenderMode, "Server", StringComparison.OrdinalIgnoreCase))
            return true;

        if (_loaded.TryGetValue(target.ModuleId, out var existing))
        {
            if (!string.Equals(existing.Version, target.Version, StringComparison.OrdinalIgnoreCase))
                return true; // 已加载旧版 DLL：必须 forceLoad 重建 WASM 运行时
            return false;
        }

        await LoadModuleAssemblyAsync(target, ct);
        return false;
    }

    private async Task<Assembly?> LoadModuleAssemblyAsync(ClientModuleDescriptor mod, CancellationToken ct)
    {
        if (_loaded.TryGetValue(mod.ModuleId, out var existing))
            return existing.Assembly;

        var dllUrl = $"/_modules/client/{Uri.EscapeDataString(mod.ModuleId)}/{Uri.EscapeDataString(mod.Version)}/{Uri.EscapeDataString(mod.AssemblyFileName)}";
        try
        {
            await using var dllStream = await http.GetStreamAsync(dllUrl, ct);
            using var ms = new MemoryStream();
            await dllStream.CopyToAsync(ms, ct);
            ms.Position = 0;

            Assembly asm;
            if (mod.HasPdb)
            {
                var pdbName = Path.ChangeExtension(mod.AssemblyFileName, ".pdb");
                var pdbUrl = $"/_modules/client/{Uri.EscapeDataString(mod.ModuleId)}/{Uri.EscapeDataString(mod.Version)}/{Uri.EscapeDataString(pdbName)}";
                try
                {
                    await using var pdbStream = await http.GetStreamAsync(pdbUrl, ct);
                    using var pdbMs = new MemoryStream();
                    await pdbStream.CopyToAsync(pdbMs, ct);
                    pdbMs.Position = 0;
                    asm = AssemblyLoadContext.Default.LoadFromStream(ms, pdbMs);
                }
                catch
                {
                    ms.Position = 0;
                    asm = AssemblyLoadContext.Default.LoadFromStream(ms);
                }
            }
            else
            {
                asm = AssemblyLoadContext.Default.LoadFromStream(ms);
            }

            _loaded[mod.ModuleId] = (mod.Version, asm);
            if (!_assemblies.Contains(asm))
                _assemblies.Add(asm);
            return asm;
        }
        catch
        {
            return null;
        }
    }

    private static void RegisterModuleServices(Assembly assembly, IServiceCollection services)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

        foreach (var startup in types
            .Where(t => t.IsClass && !t.IsAbstract
                && typeof(IModuleClientStartup).IsAssignableFrom(t)
                && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (IModuleClientStartup)Activator.CreateInstance(t)!)
            .OrderBy(s => s.Order)
            .ThenBy(s => s.GetType().FullName, StringComparer.Ordinal))
        {
            startup.ConfigureServices(services);
        }

        foreach (var impl in types.Where(t => t.IsClass && !t.IsAbstract))
        {
            if (typeof(ITransientClientService).IsAssignableFrom(impl))
                RegisterByInterfaces(services, impl, ServiceLifetime.Transient);
            else if (typeof(IClientService).IsAssignableFrom(impl))
                RegisterByInterfaces(services, impl, ServiceLifetime.Scoped);
        }
    }

    private static void RegisterByInterfaces(IServiceCollection services, Type impl, ServiceLifetime lifetime)
    {
        services.Add(new ServiceDescriptor(impl, impl, lifetime));
        foreach (var iface in impl.GetInterfaces().Where(i =>
            i != typeof(IClientService) && i != typeof(ITransientClientService) &&
            i != typeof(IDisposable) && i != typeof(IAsyncDisposable)))
        {
            services.Add(new ServiceDescriptor(iface, impl, lifetime));
        }
    }
}

