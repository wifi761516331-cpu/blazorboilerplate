namespace BlazorBoilerplate.WebHost.Client.Services;

/// <summary>模块管理页控制前端模块目录与渲染策略的抽象（Server 直调 / WASM 走端点）。</summary>
public interface IModuleCatalogControl
{
    bool IsFrontendOnlyModule(string moduleId);
    bool IsAutoRenderMode(string moduleId);
    Task<bool> SetRenderModeAsync(string moduleId, bool useAuto, CancellationToken ct = default);
    Task RefreshModuleAsync(string moduleId, CancellationToken ct = default);
}

