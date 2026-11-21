using BlazorBoilerplate.Application.Localization;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.WebHost.Components.Pages.Settings;

public partial class Settings
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Parameter] public string? Section { get; set; }

    private string? _expandedSection;

    protected override void OnParametersSet()
    {
        if (!string.IsNullOrWhiteSpace(Section))
            _expandedSection = Section.Trim().ToLowerInvariant();
    }
}
