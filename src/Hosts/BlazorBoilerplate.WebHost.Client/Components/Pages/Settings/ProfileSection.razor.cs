using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Shared.Features.Identity;
using BlazorBoilerplate.Web.Shared.Services;
using BlazorBoilerplate.WebHost.Services;
using Microsoft.AspNetCore.Components;
using BlazorBoilerplate.Shared.Features.Attachments;
using Microsoft.AspNetCore.Components.Forms;

namespace BlazorBoilerplate.WebHost.Components.Pages.Settings;

public partial class ProfileSection
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

    private bool _loading = true;
    private bool _saving;
    private bool _uploadingAvatar;
    private int _avatarInputKey;
    private string _displayName = string.Empty;
    private string _email = string.Empty;
    private string? _avatarUrl;

    protected override async Task OnInitializedAsync()
    {
        await Account.EnsureLoadedAsync();
        if (Account.CurrentUser is { } user)
        {
            _displayName = user.DisplayName;
            _email = user.Email;
            await LoadAvatarAsync(user.Id);
        }
        _loading = false;
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

        await SysState.RefreshAsync();
        var maxMb = SysState.Public.MaxUploadSizeMb > 0 ? SysState.Public.MaxUploadSizeMb : 11;
        var maxBytes = (long)maxMb * 1024 * 1024;
        if (file.Size > maxBytes)
        {
            Toast.Warning(L.GetString("platform.profile.toast.avatarTooLarge", maxMb));
            _avatarInputKey++;
            return;
        }

        _uploadingAvatar = true;
        try
        {
            await using var stream = file.OpenReadStream(maxBytes);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            var uploaded = await Api.UploadUserProfilePictureAsync(ms, file.Name, file.ContentType);
            _avatarUrl = Api.GetAttachmentUrl(uploaded.Id, uploaded.Kind, cacheBust: uploaded.CreatedAtUtc.ToUnixTimeMilliseconds().ToString());
            Account.SetAvatarUrl(_avatarUrl);
            Toast.Success(L["platform.profile.toast.avatarUpdated"]);
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.profile.toast.avatarUploadFailed", ex.Message));
        }
        finally
        {
            _avatarInputKey++;
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
            Account.SetAvatarUrl(null);
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

    private async Task SaveProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(_displayName) || string.IsNullOrWhiteSpace(_email))
        {
            Toast.Warning(L["platform.profile.toast.nameEmailRequired"]);
            return;
        }

        _saving = true;
        try
        {
            var updated = await Api.UpdateProfileAsync(new EditUserRequestDto(_email.Trim(), _displayName.Trim()));
            _displayName = updated.DisplayName;
            _email = updated.Email;
            Account.UpdateCurrentUser(updated);
            Toast.Success(L["platform.profile.toast.profileSaved"]);
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.profile.toast.profileSaveFailed", ex.Message));
        }
        finally
        {
            _saving = false;
        }
    }
}
