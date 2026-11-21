using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Shared.Features.Identity;
using BlazorBoilerplate.Web.Shared.Services;
using BlazorBoilerplate.WebHost.Services;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.WebHost.Components.Pages.Settings;

public partial class AccountSection
{
    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private AccountState Account { get; set; } = default!;

    [Inject]
    private SystemSettingsState SysState { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    private string _currentEmail = string.Empty;
    private string _displayName = string.Empty;
    private string _newEmail = string.Empty;
    private string _oldPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private bool _savingEmail;
    private bool _savingPassword;
    private int _passwordMinLength = 6;

    protected override async Task OnInitializedAsync()
    {
        await Account.EnsureLoadedAsync();
        await SysState.EnsureLoadedAsync();
        if (SysState.Public.PasswordMinLength > 0)
            _passwordMinLength = SysState.Public.PasswordMinLength;
        if (Account.CurrentUser is { } user)
        {
            _currentEmail = user.Email;
            _displayName = user.DisplayName;
        }
    }

    private async Task SaveEmailAsync()
    {
        var email = _newEmail.Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            Toast.Warning(L["platform.settings.account.toast.invalidEmail"]);
            return;
        }
        if (string.Equals(email, _currentEmail, StringComparison.OrdinalIgnoreCase))
        {
            Toast.Info(L["platform.settings.account.toast.sameEmail"]);
            return;
        }

        _savingEmail = true;
        try
        {
            var updated = await Api.UpdateProfileAsync(new EditUserRequestDto(email, _displayName));
            _currentEmail = updated.Email;
            _newEmail = string.Empty;
            Account.UpdateCurrentUser(updated);
            Toast.Success(L["platform.settings.account.toast.emailUpdated"]);
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.settings.account.toast.emailUpdateFailed", ex.Message));
        }
        finally
        {
            _savingEmail = false;
        }
    }

    private async Task ChangePasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(_oldPassword) || string.IsNullOrWhiteSpace(_newPassword))
        {
            Toast.Warning(L["platform.profile.toast.passwordsRequired"]);
            return;
        }
        await SysState.RefreshAsync();
        if (SysState.Public.PasswordMinLength > 0)
            _passwordMinLength = SysState.Public.PasswordMinLength;
        if (_newPassword.Length < _passwordMinLength)
        {
            Toast.Warning(L.GetString("platform.settings.account.toast.passwordTooShort", _passwordMinLength));
            return;
        }
        if (!string.Equals(_newPassword, _confirmPassword, StringComparison.Ordinal))
        {
            Toast.Warning(L["platform.settings.account.toast.passwordMismatch"]);
            return;
        }

        _savingPassword = true;
        try
        {
            await Api.ChangePasswordAsync(new ChangePasswordRequestDto(_oldPassword, _newPassword));
            _oldPassword = string.Empty;
            _newPassword = string.Empty;
            _confirmPassword = string.Empty;
            Toast.Success(L["platform.profile.toast.passwordChanged"]);
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.profile.toast.passwordChangeFailed", ex.Message));
        }
        finally
        {
            _savingPassword = false;
        }
    }
}
