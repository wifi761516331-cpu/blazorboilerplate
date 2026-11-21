using BlazorBoilerplate.WebHost.Services;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.WebHost.Components;

public partial class App
{
    [Inject]
    private ModuleAssemblyCatalog ModuleCatalog { get; set; } = default!;

    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    private bool UseWebAssembly =>
        ModuleCatalog.ShouldUseWebAssembly(HttpContext?.Request.Path.Value);
}

