using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.WebHost.Services;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.WebHost.Components.Pages;

public partial class ModuleHost
{
    [Inject]
    private ModuleAssemblyCatalog ModuleCatalog { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    /// <summary>catch-all 捕获的路径（/catalog → "catalog"；/ → ""）。</summary>
    [Parameter]
    public string? Path { get; set; }

    private Type? _componentType;

    protected override void OnParametersSet()
    {
        _componentType = ModuleCatalog.ResolveComponent("/" + (Path ?? string.Empty));
    }
}
