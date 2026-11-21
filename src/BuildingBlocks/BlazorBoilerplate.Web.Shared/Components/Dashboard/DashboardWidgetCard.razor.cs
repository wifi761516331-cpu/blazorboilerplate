using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Web.Shared.Components;

public partial class DashboardWidgetCard
{
    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;
    [Parameter] public string? Subtitle { get; set; }
    [Parameter] public int Span { get; set; } = 3;
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
