using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Shared.Features.Account;
using BlazorBoilerplate.Web.Shared.Services;
using BlazorBoilerplate.WebHost.Services;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.WebHost.Components.Pages;

public partial class Notifications
{
    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    [Inject]
    private UserPreferences Preferences { get; set; } = default!;

    [Inject]
    private AccountState Account { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    private List<NotificationDto> _notifications = [];
    private bool _notificationsEnabled;
    private bool _sending;
    private bool _sendingEmail;
    private bool _emailModalOpen;
    private string _emailTo = string.Empty;
    private string _emailSubject = string.Empty;
    private string _emailBody = string.Empty;
    private string? _loadError;
    private readonly HashSet<Guid> _marking = [];

    private int UnreadCount => _notifications.Count(n => !n.IsRead);

    protected override async Task OnInitializedAsync()
    {
        _emailSubject = L["platform.notifications.defaultEmailSubject"];
        _emailBody = L["platform.notifications.defaultEmailBody"];
        await Account.EnsureLoadedAsync();
        _emailTo = Account.CurrentUser?.Email ?? string.Empty;
        var prefs = await Preferences.GetAsync();
        _notificationsEnabled = prefs.NotificationsEnabled;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loadError = null;
        try
        {
            _notifications = (await Api.GetNotificationsAsync())
                .OrderByDescending(n => n.CreatedAtUtc).ToList();
        }
        catch (Exception ex)
        {
            _loadError = L.GetString("platform.notifications.toast.loadFailed", ex.Message);
            Toast.Error(_loadError);
        }
    }

    private async Task MarkReadAsync(NotificationDto item)
    {
        _marking.Add(item.Id);
        try
        {
            await Api.MarkNotificationReadAsync(item.Id);
            var read = item with { IsRead = true };
            _notifications = _notifications.Select(n => n.Id == read.Id ? read : n).ToList();
            Toast.Success(L["platform.notifications.toast.markedRead"]);
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.notifications.toast.markReadFailed", ex.Message));
        }
        finally
        {
            _marking.Remove(item.Id);
        }
    }

    private async Task MarkAllReadAsync()
    {
        try
        {
            await Api.MarkAllNotificationsReadAsync();
            _notifications = _notifications.Select(n => n with { IsRead = true }).ToList();
            Toast.Success(L["platform.notifications.toast.markedAllRead"]);
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.notifications.toast.markAllReadFailed", ex.Message));
        }
    }

    private async Task SendTestAsync()
    {
        _sending = true;
        try
        {
            await Api.SendTestNotificationAsync();
            Toast.Success(L["platform.notifications.toast.testSent"]);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.notifications.toast.sendTestFailed", ex.Message));
        }
        finally
        {
            _sending = false;
        }
    }

    private void OpenEmailModal()
    {
        if (string.IsNullOrWhiteSpace(_emailTo))
            _emailTo = Account.CurrentUser?.Email ?? string.Empty;
        _emailModalOpen = true;
    }

    private async Task SendTestEmailAsync()
    {
        _emailModalOpen = false;
        _sendingEmail = true;
        try
        {
            var result = await Api.SendTestEmailAsync(new SendEmailNotificationRequestDto(
                _emailTo, _emailSubject, _emailBody));
            Toast.Success(L.GetString("platform.notifications.toast.testEmailSent", result.ToEmail));
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.notifications.toast.sendTestEmailFailed", ex.Message));
        }
        finally
        {
            _sendingEmail = false;
        }
    }
}
