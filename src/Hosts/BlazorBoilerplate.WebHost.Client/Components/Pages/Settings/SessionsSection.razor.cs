using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Shared.Features.Identity;
using BlazorBoilerplate.Web.Shared.Services;
using BlazorBoilerplate.WebHost.Services;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.WebHost.Components.Pages.Settings;

public partial class SessionsSection
{
    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    private bool _loading = true;
    private UserSessionDto? _currentSession;
    private List<UserSessionDto> _otherSessions = [];
    private readonly HashSet<Guid> _revoking = [];

    protected override async Task OnInitializedAsync() => await LoadSessionsAsync();

    private async Task LoadSessionsAsync()
    {
        _loading = true;
        try
        {
            var all = await Api.GetUserSessionsAsync();
            _currentSession = all.FirstOrDefault(s => s.Current);
            _otherSessions = all.Where(s => !s.Current).OrderByDescending(s => s.StartedOnUtc).ToList();
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.profile.toast.loadSessionsFailed", ex.Message));
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task RevokeAsync(UserSessionDto session)
    {
        _revoking.Add(session.Id);
        try
        {
            await Api.RevokeSessionAsync(session.Id);
            _otherSessions.RemoveAll(s => s.Id == session.Id);
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

    private string FormatTime(UserSessionDto s) =>
        L.GetString("platform.settings.sessions.signedInAt", s.StartedOnUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
}
