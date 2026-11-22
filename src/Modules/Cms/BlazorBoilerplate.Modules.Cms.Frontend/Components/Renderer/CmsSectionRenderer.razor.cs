using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Renderer;

public partial class CmsSectionRenderer
{
    [Parameter, EditorRequired]
    public CmsPublishedSection Section { get; set; } = default!;

    [Parameter]
    public string SiteKey { get; set; } = "default";

    [Parameter]
    public bool IsDesignMode { get; set; }

    [Parameter]
    public Guid? SelectedWidgetId { get; set; }

    [Parameter]
    public EventCallback<string> OnNavigateAnchor { get; set; }

    [Parameter]
    public EventCallback<Guid> OnSelectWidget { get; set; }

    [Parameter]
    public EventCallback<(Guid WidgetId, int Delta)> OnMoveWidgetOrder { get; set; }

    [Parameter]
    public EventCallback<Guid> OnDeleteWidget { get; set; }

    [Parameter]
    public EventCallback<Guid> OnDuplicateWidget { get; set; }

    [Parameter]
    public EventCallback<Guid> OnDragStartWidget { get; set; }

    [Parameter]
    public EventCallback<(Guid SectionId, string ZoneId)> OnDropToZone { get; set; }

    [Parameter]
    public EventCallback<(Guid SectionId, string ZoneId, Guid TargetWidgetId)> OnDropOnWidget { get; set; }

    [Parameter]
    public EventCallback<(Guid WidgetId, string? Width, string? Height)> OnResizeWidget { get; set; }

    [Parameter]
    public EventCallback<(Guid WidgetId, string OldText, string NewText)> OnInlineEditText { get; set; }

    [Parameter]
    public EventCallback<(Guid WidgetId, string OldImageUrl)> OnInlineEditImage { get; set; }

    private Task HandleDropAsync(string zoneId) =>
        IsDesignMode ? OnDropToZone.InvokeAsync((Section.Id, zoneId)) : Task.CompletedTask;
}
