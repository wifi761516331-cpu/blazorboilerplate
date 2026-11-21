using Bit.BlazorUI;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.Modules.Identity.Frontend.Pages;

public partial class Users
{
    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private ILogger<Users> Logger { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private FrontendLocalizationService FrontendLoc { get; set; } = default!;

    private List<IdentityUserDto> _users = [];
    private List<IdentityRoleDto> _roles = [];
    private List<BitNavItem> _userNavItems = [];
    private IdentityUserDto? _selected;
    private HashSet<Guid> _selectedRoleIds = [];
    private Dictionary<Guid, bool> _roleSelection = [];
    private bool _roleDirty;
    private bool _loading = true;
    private string? _error;
    private string _search = string.Empty;
    private readonly HashSet<string> _busy = [];

    private bool _createDialogOpen;
    private bool _editDialogOpen;
    private bool _resetPwDialogOpen;
    private bool _deleteDialogOpen;

    private string NewUserName = string.Empty;
    private string NewEmail = string.Empty;
    private string? NewDisplayName;
    private string NewPassword = string.Empty;
    private string EditEmail = string.Empty;
    private string? EditDisplayName;
    private string ResetPw = string.Empty;
    private int _passwordMinLength = 6;

    protected override async Task OnInitializedAsync()
    {
        await FrontendLoc.EnsureLoadedAsync();
        try
        {
            var pub = await Api.GetPublicSystemSettingsAsync();
            if (pub.PasswordMinLength > 0)
                _passwordMinLength = pub.PasswordMinLength;
        }
        catch { /* 失败时保留默认值 */ }
        await RefreshAsync();
    }

    private void ResetCreateForm()
    {
        NewUserName = NewEmail = NewPassword = string.Empty;
        NewDisplayName = null;
        _error = null;
        _createDialogOpen = true;
    }

    private async Task RefreshAsync()
    {
        _error = null;
        _loading = true;
        try
        {
            _users = [.. await Api.GetListAsync<IdentityUserDto>("api/identity/users")];
            _roles = [.. await Api.GetListAsync<IdentityRoleDto>("api/identity/roles")];
            if (_selected is not null)
                _selected = _users.FirstOrDefault(u => u.Id == _selected.Id);
            RebuildNav();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            Logger.LogWarning(ex, "加载用户列表失败");
        }
        finally
        {
            _loading = false;
        }
    }

    private void RebuildNav()
    {
        var query = _search.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _users
            : [.. _users.Where(u => $"{u.UserName}|{u.Email}|{u.DisplayName}".Contains(query, StringComparison.OrdinalIgnoreCase))];
        _userNavItems = [.. filtered.Select(u => new BitNavItem
        {
            Key = u.Id.ToString(),
            Text = u.DisplayName,
            Description = $"{u.UserName} · {u.Email}" + (u.IsActive ? "" : $" · {L["identity.users.status.disabled"]}"),
            Data = u
        })];
    }

    private async Task SelectUserAsync(BitNavItem item)
    {
        if (item?.Data is not IdentityUserDto user)
            return;
        _selected = user;
        _roleDirty = false;
        _error = null;
        try
        {
            _selectedRoleIds = [.. await Api.GetListAsync<Guid>($"api/identity/users/{user.Id}/roles")];
            _roleSelection = _roles.ToDictionary(r => r.Id, r => _selectedRoleIds.Contains(r.Id));
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private string GetRoleLabel(IdentityRoleDto role) => role.IsSystem ? L.GetString("identity.roles.builtInSuffix", role.Name) : role.Name;

    private void OnRoleSelectionChanged(Guid roleId, bool selected)
    {
        _roleSelection[roleId] = selected;
        _roleDirty = true;
    }

    private async Task CreateUserAsync()
    {
        _error = null;
        var userName = (NewUserName ?? string.Empty).Trim();
        var email = (NewEmail ?? string.Empty).Trim();
        if (userName.Length < 2)
        {
            Toast.Warning(L["identity.error.userNameInvalid"]);
            return;
        }
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            Toast.Warning(L["identity.error.emailInvalid"]);
            return;
        }
        if (string.IsNullOrEmpty(NewPassword) || NewPassword.Length < 6)
        {
            Toast.Warning(L.GetString("identity.error.passwordMinLength", 6));
            return;
        }

        try
        {
            var body = new { UserName = userName, Email = email, DisplayName = NewDisplayName, Password = NewPassword };
            var response = await Api.PostAsync("api/identity/users", body);
            await BlazorBoilerplateApiClient.EnsureSuccessAsync(response);
            _createDialogOpen = false;
            Toast.Success(L.GetString("identity.users.toast.created", userName));
            NewUserName = NewEmail = NewPassword = string.Empty;
            NewDisplayName = null;
            await RefreshAsync();
        }
        catch (Exception ex) { var msg = ex.GetUserMessage(L); _error = msg; Toast.Error(L.GetString("identity.users.toast.createFailed", msg)); }
    }

    private Task OpenEditDialogAsync()
    {
        if (_selected is null) return Task.CompletedTask;
        EditEmail = _selected.Email;
        EditDisplayName = _selected.DisplayName;
        _editDialogOpen = true;
        return Task.CompletedTask;
    }

    private async Task SaveProfileAsync()
    {
        if (_selected is null) return;
        _error = null;
        var email = (EditEmail ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            Toast.Warning(L["identity.error.emailInvalid"]);
            return;
        }

        try
        {
            var body = new { UserName = _selected.UserName, Email = email, DisplayName = EditDisplayName, Password = string.Empty };
            var response = await Api.PutAsync($"api/identity/users/{_selected.Id}", body);
            await BlazorBoilerplateApiClient.EnsureSuccessAsync(response);
            _editDialogOpen = false;
            Toast.Success(L.GetString("identity.users.toast.profileSaved", _selected.DisplayName));
            await RefreshAsync();
        }
        catch (Exception ex) { var msg = ex.GetUserMessage(L); _error = msg; Toast.Error(L.GetString("identity.users.toast.saveProfileFailed", msg)); }
    }

    private async Task ResetPasswordAsync()
    {
        if (_selected is null) return;
        _error = null;
        if (string.IsNullOrEmpty(ResetPw) || ResetPw.Length < 6)
        {
            Toast.Warning(L.GetString("identity.error.passwordMinLength", 6));
            return;
        }

        try
        {
            var response = await Api.PutAsync($"api/identity/users/{_selected.Id}/password", new { Password = ResetPw });
            await BlazorBoilerplateApiClient.EnsureSuccessAsync(response);
            _resetPwDialogOpen = false;
            Toast.Success(L.GetString("identity.users.toast.passwordReset", _selected.DisplayName));
        }
        catch (Exception ex) { var msg = ex.GetUserMessage(L); _error = msg; Toast.Error(L.GetString("identity.users.toast.resetPasswordFailed", msg)); }
    }

    private async Task ToggleActiveAsync()
    {
        if (_selected is null) return;
        _error = null;
        var key = _selected.Id.ToString();
        _busy.Add(key);
        try
        {
            var enabling = !_selected.IsActive;
            var name = _selected.DisplayName;
            var response = await Api.PutAsync($"api/identity/users/{_selected.Id}/active", new { IsActive = !_selected.IsActive });
            await BlazorBoilerplateApiClient.EnsureSuccessAsync(response);
            Toast.Success(L.GetString(enabling ? "identity.users.toast.enabled" : "identity.users.toast.disabled", name));
            await RefreshAsync();
        }
        catch (Exception ex) { var msg = ex.GetUserMessage(L); _error = msg; Toast.Error(L.GetString("identity.users.toast.toggleActiveFailed", msg)); }
        finally { _busy.Remove(key); }
    }

    private async Task DeleteUserAsync()
    {
        if (_selected is null) return;
        _error = null;
        var name = _selected.DisplayName;
        try
        {
            var response = await Api.DeleteAsync($"api/identity/users/{_selected.Id}");
            await BlazorBoilerplateApiClient.EnsureSuccessAsync(response);
            _deleteDialogOpen = false;
            Toast.Success(L.GetString("identity.users.toast.deleted", name));
            _selected = null;
            await RefreshAsync();
        }
        catch (Exception ex) { var msg = ex.GetUserMessage(L); _error = msg; Toast.Error(L.GetString("identity.users.toast.deleteFailed", msg)); }
    }

    private async Task SaveRolesAsync()
    {
        if (_selected is null) return;
        _error = null;
        var key = _selected.Id.ToString();
        _busy.Add(key);
        try
        {
            var ids = _roleSelection.Where(kv => kv.Value).Select(kv => kv.Key).ToArray();
            var response = await Api.PutAsync($"api/identity/users/{_selected.Id}/roles", new { Ids = ids });
            await BlazorBoilerplateApiClient.EnsureSuccessAsync(response);
            _roleDirty = false;
            Toast.Success(L.GetString("identity.users.toast.rolesSaved", _selected.DisplayName));
        }
        catch (Exception ex) { var msg = ex.GetUserMessage(L); _error = msg; Toast.Error(L.GetString("identity.users.toast.saveRolesFailed", msg)); }
        finally { _busy.Remove(key); }
    }
}
