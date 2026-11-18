using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Shared.Features.Account;
using BlazorBoilerplate.Shared.Features.Identity;
using BlazorBoilerplate.Web.Shared.Services;
using BlazorBoilerplate.WebHost.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;

namespace BlazorBoilerplate.WebHost.Components.Pages;

public partial class Tenants
{
    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private AccountState Account { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    private List<TenantDto> _tenants = [];
    private List<TenantDto> _allTenants = [];
    private TenantDto? _currentTenant;
    private string? _loadError;
    private bool _loading;
    private bool _busy;
    private bool _isAdmin;

    private string? _expanded;

    // 新建
    private string _createName = string.Empty;
    private string _createTitle = string.Empty;
    private string _createDomain = string.Empty;

    // 邀请
    private string _inviteEmail = string.Empty;

    // 编辑当前租户
    private string _editName = string.Empty;
    private string _editTitle = string.Empty;
    private string _editDomain = string.Empty;

    // 离开 / 拒绝
    private bool _confirmLeaveOpen;
    private TenantDto? _leavingTenant;

    // 管理员编辑
    private bool _adminEditOpen;
    private TenantDto? _adminEditing;
    private string _adminName = string.Empty;
    private string _adminTitle = string.Empty;
    private string _adminDomain = string.Empty;
    private bool _adminActive;

    protected override async Task OnInitializedAsync()
    {
        await Account.EnsureLoadedAsync();
        _isAdmin = Account.HasPermission("tenant.create");
        await LoadAsync();
        if (_isAdmin)
            await LoadAllAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _loadError = null;
        try
        {
            _tenants = (await Api.GetTenantsAsync()).ToList();
            _currentTenant = _tenants.FirstOrDefault(IsCurrent);
            if (_currentTenant is not null)
            {
                _editName = _currentTenant.Name;
                _editTitle = _currentTenant.Title ?? string.Empty;
                _editDomain = _currentTenant.Domain ?? string.Empty;
            }
            // 无租户时默认展开「新建租户」；否则默认收起。
            _expanded = _tenants.Count == 0 ? "create" : null;
        }
        catch (Exception ex)
        {
            _loadError = L.GetString("platform.tenants.error.loadFailed", ex.Message);
            Toast.Error(_loadError);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task LoadAllAsync()
    {
        try { _allTenants = (await Api.GetAllTenantsAsync()).ToList(); }
        catch (Exception ex) { Toast.Error(L.GetString("platform.tenants.error.loadAllFailed", ex.Message)); }
    }

    private bool IsCurrent(TenantDto tenant) =>
        tenant.Current || (Account.CurrentUser?.TenantId is { } tid && tid == tenant.Id);

    // 手风琴分区标题用属性计算，事件处理器用方法组（规避 Razor 属性内字符串字面量 lambda 的引号解析问题）。
    private string InviteSectionTitle => _currentTenant is null
        ? L["platform.tenants.inviteMembers"]
        : L.GetString("platform.tenants.inviteMembersTo", string.IsNullOrWhiteSpace(_currentTenant.Title) ? _currentTenant.Name : _currentTenant.Title);

    private void ToggleCreate() => ToggleSection("create");
    private void ToggleInvite() => ToggleSection("invite");
    private void ToggleUpdate() => ToggleSection("update");

    private void ToggleSection(string key)
    {
        if (_currentTenant is null && (key == "invite" || key == "update")) return;
        _expanded = _expanded == key ? null : key;
    }

    private async Task Redirect(string url)
    {
        try { await JS.InvokeVoidAsync("blazorAuth.redirect", url); }
        catch { /* 整页跳转会销毁 JS 上下文，忽略跳转引发的 interop 异常 */ }
    }

    /// <summary>切换/接受邀请入口（带 busy 守卫，供卡片按钮直接触发）。</summary>
    private async Task SwitchOrAcceptAsync(TenantDto tenant)
    {
        if (IsCurrent(tenant) || _busy) return;
        _busy = true;
        try
        {
            await SwitchTenantCoreAsync(tenant.Id, tenant.Name);
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.tenants.toast.switchFailed", ex.Message));
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>切换核心逻辑（不含 busy 守卫）：CreateAsync / ConfirmLeaveAsync 已持有 _busy，
    /// 若经 SwitchOrAcceptAsync 进入会被开头的 _busy 检查静默吞掉（切换与跳转都不执行）。</summary>
    private async Task SwitchTenantCoreAsync(Guid tenantId, string tenantName)
    {
        var result = await JS.InvokeAsync<JsonElement>("blazorAuth.switchTenant", tenantId.ToString());
        if (!Ok(result))
        {
            Toast.Error(Err(result) ?? L["platform.tenants.toast.switchFailedSimple"]);
            return;
        }
        Toast.Success(L.GetString("platform.tenants.toast.switching", tenantName));
        await Redirect("/");
    }

    private async Task CreateAsync()
    {
        if (_busy) return;
        if (string.IsNullOrWhiteSpace(_createName))
        {
            Toast.Warning(L["platform.tenants.toast.nameRequired"]);
            return;
        }
        _busy = true;
        try
        {
            var name = _createName.Trim();
            var title = string.IsNullOrWhiteSpace(_createTitle) ? null : _createTitle.Trim();
            var domain = string.IsNullOrWhiteSpace(_createDomain) ? null : _createDomain.Trim();
            var created = await Api.CreateTenantAsync(
                new TenantDto(Guid.Empty, name, title, domain, true, true, false));
            Toast.Success(L.GetString("platform.tenants.toast.createdEntering", created.Name));
            // 创建者即成为该租户管理员，直接切入（与 Bluebird 一致）。
            await SwitchTenantCoreAsync(created.Id, created.Name);
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.tenants.toast.createFailed", ex.Message));
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task InviteAsync()
    {
        if (_busy || _currentTenant is null) return;
        if (string.IsNullOrWhiteSpace(_inviteEmail))
        {
            Toast.Warning(L["platform.tenants.toast.inviteEmailRequired"]);
            return;
        }
        _busy = true;
        try
        {
            await Api.InviteTenantUserAsync(new InviteUserToTenantRequestDto(_currentTenant.Id, _inviteEmail.Trim()));
            Toast.Success(L.GetString("platform.tenants.toast.invited", _inviteEmail.Trim()));
            _inviteEmail = string.Empty;
            _expanded = null;
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.tenants.toast.inviteFailed", ex.Message));
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task UpdateAsync()
    {
        if (_busy || _currentTenant is null) return;
        if (string.IsNullOrWhiteSpace(_editName))
        {
            Toast.Warning(L["platform.tenants.toast.nameRequired"]);
            return;
        }
        _busy = true;
        try
        {
            var name = _editName.Trim();
            var title = string.IsNullOrWhiteSpace(_editTitle) ? null : _editTitle.Trim();
            var domain = string.IsNullOrWhiteSpace(_editDomain) ? null : _editDomain.Trim();
            await Api.UpdateTenantAsync(_currentTenant with { Name = name, Title = title, Domain = domain });
            Toast.Success(L.GetString("platform.tenants.toast.updated", name));
            _expanded = null;
            await Redirect(Navigation.Uri); // 整页刷新以反映新名称/标题
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.tenants.toast.updateFailed", ex.Message));
        }
        finally
        {
            _busy = false;
        }
    }

    private void AskLeave(TenantDto tenant)
    {
        _leavingTenant = tenant;
        _confirmLeaveOpen = true;
    }

    private async Task ConfirmLeaveAsync()
    {
        if (_leavingTenant is null) return;
        var tenant = _leavingTenant;
        var wasCurrent = IsCurrent(tenant);
        _busy = true;
        try
        {
            await Api.LeaveTenantAsync(tenant.Id);
            Toast.Success(tenant.Accepted
                ? L.GetString("platform.tenants.toast.left", tenant.Name)
                : L.GetString("platform.tenants.toast.declined", tenant.Name));
            _confirmLeaveOpen = false;
            _leavingTenant = null;

            await LoadAsync();
            // 离开的是当前租户：若还有其他已接受租户则切入，否则整页刷新（cookie 的 tid 可能短暂滞后）。
            if (wasCurrent)
            {
                var fallback = _tenants.FirstOrDefault(t => t.Accepted);
                if (fallback is not null)
                {
                    await SwitchTenantCoreAsync(fallback.Id, fallback.Name);
                    return;
                }
            }
            await Redirect(Navigation.Uri);
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.tenants.toast.leaveFailed", ex.Message));
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ToggleActiveAsync(TenantDto tenant)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var updated = await Api.UpdateTenantAsync(tenant with { IsActive = !tenant.IsActive });
            var idx = _allTenants.FindIndex(t => t.Id == updated.Id);
            if (idx >= 0) _allTenants[idx] = updated;
            Toast.Success(updated.IsActive
                ? L.GetString("platform.tenants.toast.activated", updated.Name)
                : L.GetString("platform.tenants.toast.deactivated", updated.Name));
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.tenants.toast.toggleFailed", ex.Message));
        }
        finally
        {
            _busy = false;
        }
    }

    private void OpenAdminCreate()
    {
        _adminEditing = null;
        _adminName = string.Empty;
        _adminTitle = string.Empty;
        _adminDomain = string.Empty;
        _adminActive = true;
        _adminEditOpen = true;
    }

    private void OpenAdminEdit(TenantDto tenant)
    {
        _adminEditing = tenant;
        _adminName = tenant.Name;
        _adminTitle = tenant.Title ?? string.Empty;
        _adminDomain = tenant.Domain ?? string.Empty;
        _adminActive = tenant.IsActive;
        _adminEditOpen = true;
    }

    private async Task SaveAdminEditAsync()
    {
        if (string.IsNullOrWhiteSpace(_adminName))
        {
            Toast.Warning(L["platform.tenants.toast.nameRequired"]);
            return;
        }
        try
        {
            var name = _adminName.Trim();
            var title = string.IsNullOrWhiteSpace(_adminTitle) ? null : _adminTitle.Trim();
            var domain = string.IsNullOrWhiteSpace(_adminDomain) ? null : _adminDomain.Trim();
            if (_adminEditing is null)
            {
                var created = await Api.CreateTenantAsync(new TenantDto(Guid.Empty, name, title, domain, _adminActive, true, false));
                Toast.Success(L.GetString("platform.tenants.toast.adminCreated", created.Name));
            }
            else
            {
                var updated = await Api.UpdateTenantAsync(_adminEditing with
                {
                    Name = name,
                    Title = title,
                    Domain = domain,
                    IsActive = _adminActive
                });
                Toast.Success(L.GetString("platform.tenants.toast.updated", updated.Name));
            }
            _adminEditOpen = false;
            await LoadAsync();
            await LoadAllAsync();
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.tenants.toast.saveFailed", ex.Message));
        }
    }

    private static bool Ok(JsonElement el) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty("ok", out var v) && v.ValueKind == JsonValueKind.True;

    private static string? Err(JsonElement el) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty("error", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
