using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.WebHost.Services;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.WebHost.Components.Pages;

public partial class ModuleHost
{
    [Inject]
    private ModuleAssemblyCatalog ModuleCatalog { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private HttpClient Http { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    /// <summary>catch-all 捕获的路径（/catalog → "catalog"；/ → ""）。</summary>
    [Parameter]
    public string? Path { get; set; }

    private Type? _componentType;

    protected override async Task OnParametersSetAsync()
    {
        var rawPath = "/" + (Path ?? string.Empty).TrimStart('/');
        _componentType = ModuleCatalog.ResolveComponent(rawPath);

        if (_componentType is null && DomainRouteResolverRegistry.Current is { } resolver)
        {
            var host = new Uri(Navigation.BaseUri).Host;
            var match = await resolver.ResolveAsync(Http, host);
            if (match is not null)
            {
                var mappedPath = DomainRouteResolverRegistry.ResolveTargetPath(match, rawPath);
                _componentType = ModuleCatalog.ResolveComponent(mappedPath);
            }
        }
    }
}
