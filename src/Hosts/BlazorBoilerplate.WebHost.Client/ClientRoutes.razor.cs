using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Web.Shared.Components.Layout;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.WebHost.Client.Services;
using BlazorBoilerplate.WebHost.Components.Layout;
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

    [Inject]
    private HttpClient Http { get; set; } = default!;

    private DomainRouteMatch? _domainMatch;
    private Type? _notFoundDomainComponent;

    protected override async Task OnInitializedAsync()
    {
        if (OperatingSystem.IsBrowser())
        {
            await FrontendLoc.EnsureLoadedAsync();
            await Account.EnsureLoadedAsync();
        }

        if (DomainRouteResolverRegistry.Current is { } resolver)
            _domainMatch = await resolver.ResolveAsync(Http, new Uri(Navigation.BaseUri).Host);
    }

    private Type PickLayout()
    {
        var path = new Uri(Navigation.Uri).AbsolutePath.TrimEnd('/');
        if (path is "/sign-in" or "/sign-up" or "/forgot-password" or "/setup")
            return typeof(IdentityLayout);

        return _domainMatch is { AllowAdminRoutes: false }
            ? typeof(PortalLayout)
            : typeof(MainLayout);
    }

    private async Task OnNavigateAsync(NavigationContext args)
    {
        var path = "/" + (args.Path ?? string.Empty).TrimStart('/');
        var clean = path.Split('?', '#')[0].TrimEnd('/');
        if (clean.Length == 0) clean = "/";

        if (clean.Equals("/sign-in", StringComparison.OrdinalIgnoreCase)
            || clean.Equals("/sign-up", StringComparison.OrdinalIgnoreCase)
            || clean.Equals("/forgot-password", StringComparison.OrdinalIgnoreCase)
            || clean.Equals("/setup", StringComparison.OrdinalIgnoreCase)
            || clean.Equals("/auth", StringComparison.OrdinalIgnoreCase)
            || clean.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase))
        {
            Navigation.NavigateTo(args.Path ?? "/", forceLoad: true);
            return;
        }

        _notFoundDomainComponent = null;
        var ensurePath = path;

        if (DomainRouteResolverRegistry.Current is { } resolver)
        {
            _domainMatch ??= await resolver.ResolveAsync(Http, new Uri(Navigation.BaseUri).Host, args.CancellationToken);
            if (_domainMatch is not null)
            {
                if (DomainRouteResolverRegistry.TryStripEntryPrefix(_domainMatch, path, out var stripped))
                {
                    Navigation.NavigateTo(stripped, replace: true);
                    return;
                }
                if (DomainRouteResolverRegistry.ShouldRedirectAdminRoute(_domainMatch, clean))
                {
                    Navigation.NavigateTo("/", replace: true);
                    return;
                }
                ensurePath = DomainRouteResolverRegistry.ResolveTargetPath(_domainMatch, clean);
            }
        }

        var requiresForceLoad = await ModuleCatalog.EnsureRouteReadyAsync(ensurePath, args.CancellationToken);
        if (requiresForceLoad)
        {
            Navigation.NavigateTo(args.Path ?? "/", forceLoad: true);
            return;
        }

        if (_domainMatch is not null && clean != "/")
            _notFoundDomainComponent = ModuleCatalog.ResolveComponent(ensurePath);
    }
}
