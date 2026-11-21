using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Web.Shared.Components.Layout;
using BlazorBoilerplate.WebHost.Components.Layout;
using BlazorBoilerplate.WebHost.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace BlazorBoilerplate.WebHost.Components;

// 布局裁决逻辑在 .razor.cs；Router/NotFound 标记留在 .razor。
public partial class Routes
{
    [Inject]
    private ModuleAssemblyCatalog ModuleCatalog { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private IHttpContextAccessor HttpContextAccessor { get; set; } = default!;

    /// <summary>
    /// 按路由与域名契约集中选择布局：
    /// 1) 登录页 /sign-in 与安装向导 /setup 使用独立的 IdentityLayout；
    /// 2) 命中域名路由且 AllowAdminRoutes=false 时使用无后台侧边栏的 C 端门户布局 PortalLayout；
    /// 3) 其余路由使用主布局 MainLayout。
    /// </summary>
    private Type PickLayout()
    {
        var path = new Uri(Navigation.Uri).AbsolutePath.TrimEnd('/');
        if (path is "/sign-in" or "/sign-up" or "/forgot-password" or "/setup")
            return typeof(IdentityLayout);

        if (HttpContextAccessor.HttpContext?.Items.TryGetValue(
                BlazorBoilerplate.ModuleAbstractions.DomainRouteResolverRegistry.HttpItemKey, out var raw) == true &&
            raw is BlazorBoilerplate.ModuleAbstractions.DomainRouteMatch { AllowAdminRoutes: false })
        {
            return typeof(PortalLayout);
        }

        return typeof(MainLayout);
    }
}
