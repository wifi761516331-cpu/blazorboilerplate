using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.WebHost.Client.Services;
using BlazorBoilerplate.WebHost.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace BlazorBoilerplate.WebHost.Client;

public partial class ClientRoutes
{
    [Inject]
    private ClientModuleCatalog ModuleCatalog { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private AccountState Account { get; set; } = default!;

    [Inject]
    private FrontendLocalizationService FrontendLoc { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        if (OperatingSystem.IsBrowser())
        {
            await FrontendLoc.EnsureLoadedAsync();
            await Account.EnsureLoadedAsync();
        }
    }

    private async Task OnNavigateAsync(NavigationContext args)
    {
        var path = "/" + (args.Path ?? string.Empty).TrimStart('/');
        var target = ModuleCatalog.FindByPath(path);
        if (target is null)
        {
            // 非动态模块路由（如平台 /、/tenants、/modules 等），交回服务端渲染
            Navigation.NavigateTo(args.Path ?? "/", forceLoad: true);
            return;
        }

        var requiresForceLoad = await ModuleCatalog.EnsureRouteReadyAsync(path, args.CancellationToken);
        if (requiresForceLoad)
            Navigation.NavigateTo(args.Path ?? "/", forceLoad: true);
    }
}
