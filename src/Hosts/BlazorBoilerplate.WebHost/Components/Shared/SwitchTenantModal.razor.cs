using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Shared.Features.Identity;
using BlazorBoilerplate.Web.Shared.Services;
using BlazorBoilerplate.WebHost.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;

namespace BlazorBoilerplate.WebHost.Components.Shared;

// @inject 已由 [Inject] 取代；租户加载/切换逻辑在 .razor.cs，标记留在 .razor。
public partial class SwitchTenantModal
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private AccountState Account { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    /// <summary>显隐由父组件（AccountMenu）双向绑定：true 时加载数据并显示。</summary>
    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback<bool> IsOpenChanged { get; set; }

    private bool _lastOpen;
    private bool _loading;
    private bool _busy;
    private List<TenantDto> _tenants = [];

    /// <summary>IsOpen 由 false 变 true 时加载当前用户的租户列表（含受邀未接受的）。</summary>
    protected override async Task OnParametersSetAsync()
    {
        if (IsOpen && !_lastOpen)
        {
            _loading = true;
            try
            {
                await Account.EnsureLoadedAsync();
                _tenants = (await Api.GetTenantsAsync()).ToList();
            }
            catch (Exception ex)
            {
                _tenants = [];
                Toast.Error($"读取租户列表失败：{ex.Message}");
            }
            finally
            {
                _loading = false;
            }
        }
        _lastOpen = IsOpen;
    }

    private Task CloseAsync() => IsOpenChanged.InvokeAsync(false);

    private bool IsCurrent(TenantDto tenant) =>
        tenant.Accepted && (tenant.Current || Account.CurrentUser?.TenantId == tenant.Id);

    private async Task SelectAsync(TenantDto tenant)
    {
        if (IsCurrent(tenant) || _busy) return;
        _busy = true;
        try
        {
            var result = await JS.InvokeAsync<JsonElement>("blazorAuth.switchTenant", tenant.Id.ToString());
            if (!Ok(result))
            {
                Toast.Error(Err(result) ?? "切换租户失败。");
                return;
            }
            Toast.Success($"正在切换到租户「{tenant.Name}」…");
            await CloseAsync();
            await Redirect("/");
        }
        catch (Exception ex)
        {
            Toast.Error($"切换租户失败：{ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ManageTenants()
    {
        await CloseAsync();
        Navigation.NavigateTo("/tenants");
    }

    private async Task Redirect(string url)
    {
        try { await JS.InvokeVoidAsync("blazorAuth.redirect", url); }
        catch { /* 整页跳转会销毁 JS 上下文，忽略跳转引发的 interop 异常 */ }
    }

    private static bool Ok(JsonElement el) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty("ok", out var v) && v.ValueKind == JsonValueKind.True;

    private static string? Err(JsonElement el) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty("error", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
