using Bit.BlazorUI;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Shared.Features.SystemSettings;
using BlazorBoilerplate.Web.Shared.Services;
using BlazorBoilerplate.WebHost.Services;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.WebHost.Components.Pages;

public partial class SystemSettings
{
    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private SystemSettingsState SysState { get; set; } = default!;

    [Inject]
    private UserPreferences Preferences { get; set; } = default!;

    [Inject]
    private AccountState Account { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Parameter] public string? Section { get; set; }

    private string? _expandedSection = "general";
    private bool _loading = true;
    private bool _saving;
    private bool _sendingTest;
    private string? _loadError;
    private string _testEmailTo = string.Empty;
    private SystemSettingsDto _model = new();

    private readonly List<BitDropdownItem<string>> _cultureItems =
    [
        new() { Text = "简体中文 (zh-CN)", Value = "zh-CN" },
        new() { Text = "English (en-US)", Value = "en-US" },
        new() { Text = "日本語 (ja-JP)", Value = "ja-JP" },
        new() { Text = "Deutsch (de-DE)", Value = "de-DE" }
    ];

    private List<BitDropdownItem<string>> TimeZoneItems =>
    [
        new() { Text = L["platform.sysSettings.tz.shanghai"], Value = "Asia/Shanghai" },
        new() { Text = "(UTC+00:00) UTC", Value = "UTC" },
        new() { Text = L["platform.sysSettings.tz.tokyo"], Value = "Asia/Tokyo" },
        new() { Text = L["platform.sysSettings.tz.london"], Value = "Europe/London" },
        new() { Text = L["platform.sysSettings.tz.berlin"], Value = "Europe/Berlin" },
        new() { Text = L["platform.sysSettings.tz.newYork"], Value = "America/New_York" },
        new() { Text = L["platform.sysSettings.tz.sydney"], Value = "Australia/Sydney" }
    ];

    private List<BitDropdownItem<string>> StorageItems =>
    [
        new() { Text = L["platform.sysSettings.storage.local"], Value = "Local" },
        new() { Text = L["platform.sysSettings.storage.s3"], Value = "S3" }
    ];

    protected override void OnParametersSet()
    {
        if (!string.IsNullOrWhiteSpace(Section))
            _expandedSection = Section.Trim().ToLowerInvariant();
    }

    protected override async Task OnInitializedAsync()
    {
        await Account.EnsureLoadedAsync();
        if (string.IsNullOrWhiteSpace(_testEmailTo) && Account.CurrentUser is { } user)
            _testEmailTo = user.Email;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _loadError = null;
        try
        {
            _model = await Api.GetSystemSettingsAsync();
        }
        catch (Exception ex)
        {
            _loadError = L.GetString("platform.sysSettings.error.loadFailed", ex.Message);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task SaveAsync()
    {
        _saving = true;
        var prevCulture = SysState.Public.DefaultCulture;
        var prevTimeZone = SysState.Public.DefaultTimeZone;
        try
        {
            _model = await Api.UpdateSystemSettingsAsync(_model);
            SysState.ApplyUpdated(_model);

            // 若当前用户仍处于旧的系统默认语言/时区，同步跟随新的系统默认值
            var prefs = await Preferences.GetAsync();
            var nextCulture = string.Equals(prefs.Culture, prevCulture, StringComparison.OrdinalIgnoreCase)
                ? _model.DefaultCulture
                : prefs.Culture;
            var nextTimeZone = string.Equals(prefs.TimeZone, prevTimeZone, StringComparison.OrdinalIgnoreCase)
                ? _model.DefaultTimeZone
                : prefs.TimeZone;
            if (nextCulture != prefs.Culture || nextTimeZone != prefs.TimeZone)
                await Preferences.SetAsync(prefs with { Culture = nextCulture, TimeZone = nextTimeZone });

            Toast.Success(L["platform.sysSettings.toast.saved"]);
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.sysSettings.toast.saveFailed", ex.Message));
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task SendTestEmailAsync()
    {
        if (string.IsNullOrWhiteSpace(_testEmailTo) || !_testEmailTo.Contains('@'))
        {
            Toast.Warning(L["platform.sysSettings.toast.invalidTestEmail"]);
            return;
        }

        _sendingTest = true;
        try
        {
            await Api.SendTestSmtpEmailAsync(_testEmailTo.Trim());
            Toast.Success(L.GetString("platform.notifications.toast.testEmailSent", _testEmailTo.Trim()));
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.notifications.toast.sendTestEmailFailed", ex.Message));
        }
        finally
        {
            _sendingTest = false;
        }
    }
}
