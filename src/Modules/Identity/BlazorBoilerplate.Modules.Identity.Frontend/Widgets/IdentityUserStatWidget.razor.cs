using System.Net.Http.Json;
using BlazorBoilerplate.Application.Localization;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Identity.Widgets;

public partial class IdentityUserStatWidget
{
    private sealed record UserStatsWire(int TotalUsers, int ActiveUsers);

    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] private ILocalizationService L { get; set; } = default!;

    private int? _activeUsers;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var stats = await Http.GetFromJsonAsync<UserStatsWire>("api/identity/stats");
            _activeUsers = stats?.ActiveUsers;
        }
        catch
        {
            _activeUsers = null;
        }
    }
}
