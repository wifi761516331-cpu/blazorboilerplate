using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

public partial class CmsButtonWidget
{
    public sealed record ButtonProps(
        string Text = "立即咨询",
        string Href = "#contact",
        string Variant = "fill",
        string Align = "left",
        bool OpenNewTab = false);

    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Parameter] public string PropsJson { get; set; } = "{}";
    [Parameter] public EventCallback<string> OnNavigateAnchor { get; set; }

    private ButtonProps _props = new();

    private string JustifyContent => (_props.Align ?? "left").ToLowerInvariant() switch
    {
        "center" => "center",
        "right" => "flex-end",
        _ => "flex-start"
    };

    protected override void OnParametersSet() =>
        _props = CmsJson.DeserializeOrDefault(PropsJson, new ButtonProps());

    private async Task HandleClickAsync()
    {
        var href = _props.Href?.Trim();
        if (string.IsNullOrWhiteSpace(href)) return;
        if (href.StartsWith('#'))
        {
            await OnNavigateAnchor.InvokeAsync(href[1..]);
            return;
        }
        if (_props.OpenNewTab)
        {
            await JS.InvokeVoidAsync("open", href, "_blank", "noopener");
            return;
        }
        Navigation.NavigateTo(href);
    }
}
