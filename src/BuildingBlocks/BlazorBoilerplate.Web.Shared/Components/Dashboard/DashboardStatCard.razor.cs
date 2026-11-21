using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Web.Shared.Components;

public partial class DashboardStatCard
{
    [Parameter, EditorRequired] public string Icon { get; set; } = "Info";
    [Parameter, EditorRequired] public string Value { get; set; } = "—";
    [Parameter, EditorRequired] public string Label { get; set; } = string.Empty;
}
