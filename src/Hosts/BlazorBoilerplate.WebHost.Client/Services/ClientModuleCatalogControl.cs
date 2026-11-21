using System.Net.Http.Json;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Web.Shared.Localization;

namespace BlazorBoilerplate.WebHost.Client.Services;

/// <summary>WASM 端模块控制实现：通过 /_modules/client/* 端点与服务端目录同步。</summary>
public sealed class ClientModuleCatalogControl(
    ClientModuleCatalog catalog,
    HttpClient http,
    FrontendLocalizationService frontendLoc) : IModuleCatalogControl
{
    public bool IsFrontendOnlyModule(string moduleId) =>
        catalog.Manifest?.Modules.Any(m => string.Equals(m.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase)) ?? false;

    public bool IsAutoRenderMode(string moduleId) =>
        catalog.Manifest?.Modules.FirstOrDefault(m => string.Equals(m.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase))
            ?.RenderMode.Equals("Auto", StringComparison.OrdinalIgnoreCase) ?? false;

    public async Task<bool> SetRenderModeAsync(string moduleId, bool useAuto, CancellationToken ct = default)
    {
        var mode = useAuto ? "Auto" : "Server";
        var resp = await http.PostAsync($"/_modules/client/{Uri.EscapeDataString(moduleId)}/render-mode?mode={mode}", null, ct);
        if (!resp.IsSuccessStatusCode)
            return false;

        await catalog.RefreshManifestAsync(ct);
        return true;
    }

    public async Task RefreshModuleAsync(string moduleId, CancellationToken ct = default)
    {
        await http.PostAsync($"/_modules/client/{Uri.EscapeDataString(moduleId)}/refresh", null, ct);
        await catalog.RefreshManifestAsync(ct);
        if (catalog.Manifest?.Modules.Any(m => string.Equals(m.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase)) == true)
        {
            await catalog.ActivateModuleWidgetsAsync(moduleId, ct);
            await frontendLoc.InvalidateAsync(cancellationToken: ct);
        }
        else
        {
            WidgetZoneRegistry.UnregisterModule(moduleId);
        }
    }
}

