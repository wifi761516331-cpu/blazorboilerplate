using BlazorBoilerplate.WebHost.Client.Services;

namespace BlazorBoilerplate.WebHost.Services;

/// <summary>Server 端模块控制实现：直接委托给内存中的 ModuleAssemblyCatalog。</summary>
public sealed class ServerModuleCatalogControl(ModuleAssemblyCatalog catalog) : IModuleCatalogControl
{
    public bool IsFrontendOnlyModule(string moduleId) => catalog.IsFrontendOnlyModule(moduleId);

    public bool IsAutoRenderMode(string moduleId) => catalog.GetRenderStrategy(moduleId) == ModuleRenderStrategy.Auto;

    public Task<bool> SetRenderModeAsync(string moduleId, bool useAuto, CancellationToken ct = default)
    {
        var strategy = useAuto ? ModuleRenderStrategy.Auto : ModuleRenderStrategy.Server;
        return Task.FromResult(catalog.SetRenderStrategy(moduleId, strategy));
    }

    public Task RefreshModuleAsync(string moduleId, CancellationToken ct = default) =>
        catalog.RefreshModuleAsync(moduleId, ct);

    public Type? ResolveComponent(string path) => catalog.ResolveComponent(path);
}

