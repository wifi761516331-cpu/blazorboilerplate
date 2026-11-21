using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Shared.Features.Attachments;
using BlazorBoilerplate.Shared.Features.Identity;
using BlazorBoilerplate.Web.Shared.Services;
using BlazorBoilerplate.WebHost.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace BlazorBoilerplate.WebHost.Components.Pages;

public partial class Profile
{
    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    [Inject]
    private AccountState Account { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    private string _displayName = string.Empty;
    private string _email = string.Empty;
    private string _oldPassword = string.Empty;
    private string _newPassword = string.Empty;
    private bool _savingProfile;
    private bool _savingPassword;
    private bool _uploadingAvatar;
    private string? _avatarUrl;
    private List<UserSessionDto> _sessions = [];
    private readonly HashSet<Guid> _revoking = [];
    private string? _loadError;

    protected override async Task OnInitializedAsync()
    {
        await Account.EnsureLoadedAsync();
        if (Account.CurrentUser is { } user)
        {
            _displayName = user.DisplayName;
            _email = user.Email;
            await LoadAvatarAsync(user.Id);
        }
        else
        {
            _loadError = L["platform.profile.error.noUser"];
        }
        await LoadSessionsAsync();
    }

    private async Task LoadAvatarAsync(Guid userId)
    {
        try
        {
            var list = await Api.GetAttachmentsAsync(AttachmentKind.UserProfileImage, userId);
            var avatar = list.FirstOrDefault();
            _avatarUrl = avatar is null
                ? null
                : Api.GetAttachmentUrl(avatar.Id, avatar.Kind, cacheBust: avatar.CreatedAtUtc.ToUnixTimeMilliseconds().ToString());
        }
        catch
        {
            _avatarUrl = null;
        }
    }

    private async Task UploadAvatarAsync(InputFileChangeEventArgs e)
    {
        var file = e.File;
        if (file is null) return;
        const long maxBytes = 11 * 1024 * 1024;
        if (file.Size > maxBytes)
        {
            Toast.Warning(L.GetString("platform.profile.toast.avatarTooLarge", 11));
            return;
        }

        _uploadingAvatar = true;
        try
        {
            await using var stream = file.OpenReadStream(maxBytes);
            var uploaded = await Api.UploadUserProfilePictureAsync(stream, file.Name, file.ContentType);
            _avatarUrl = Api.GetAttachmentUrl(uploaded.Id, uploaded.Kind, cacheBust: uploaded.CreatedAtUtc.ToUnixTimeMilliseconds().ToString());
            Toast.Success(L["platform.profile.toast.avatarUpdated"]);
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.profile.toast.avatarUploadFailed", ex.Message));
        }
        finally
        {
            _uploadingAvatar = false;
        }
    }

    private async Task DeleteAvatarAsync()
    {
        _uploadingAvatar = true;
        try
        {
            await Api.DeleteUserProfilePictureAsync();
            _avatarUrl = null;
            Toast.Success(L["platform.profile.toast.avatarRemoved"]);
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.profile.toast.avatarRemoveFailed", ex.Message));
        }
        finally
        {
            _uploadingAvatar = false;
        }
    }

    private async Task LoadSessionsAsync()
    {
        try
        {
            _sessions = (await Api.GetUserSessionsAsync()).ToList();
        }
        catch (Exception ex)
        {
            _loadError = L.GetString("platform.profile.toast.loadSessionsFailed", ex.Message);
            Toast.Error(_loadError);
        }
    }

    private async Task SaveProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(_displayName) || string.IsNullOrWhiteSpace(_email))
        {
            Toast.Warning(L["platform.profile.toast.nameEmailRequired"]);
            return;
        }
        _savingProfile = true;
        try
        {
            var updated = await Api.UpdateProfileAsync(new EditUserRequestDto(_email.Trim(), _displayName.Trim()));
            _displayName = updated.DisplayName;
            _email = updated.Email;
            Toast.Success(L["platform.profile.toast.profileSaved"]);
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.profile.toast.profileSaveFailed", ex.Message));
        }
        finally
        {
            _savingProfile = false;
        }
    }

    private async Task ChangePasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(_oldPassword) || string.IsNullOrWhiteSpace(_newPassword))
        {
            Toast.Warning(L["platform.profile.toast.passwordsRequired"]);
            return;
        }
        _savingPassword = true;
        try
        {
            await Api.ChangePasswordAsync(new ChangePasswordRequestDto(_oldPassword, _newPassword));
            _oldPassword = string.Empty;
            _newPassword = string.Empty;
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

    private async Task RevokeAsync(UserSessionDto session)
    {
        _revoking.Add(session.Id);
        try
        {
            await Api.RevokeSessionAsync(session.Id);
            _sessions.RemoveAll(s => s.Id == session.Id);
            Toast.Success(L["platform.profile.toast.sessionRevoked"]);
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.profile.toast.sessionRevokeFailed", ex.Message));
        }
        finally
        {
            _revoking.Remove(session.Id);
        }
    }
}
