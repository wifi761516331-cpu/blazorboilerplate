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

    /// <summary>
    /// 按路由集中选择布局：登录页 /sign-in 与安装向导 /setup 使用独立的 IdentityLayout（无侧边栏）；
    /// 其余路由使用主布局 MainLayout。
    /// 原因：模块页面（如 SignIn.razor）的 @layout 指令在 SSR 预渲染阶段不可靠（实测会回退到默认 MainLayout），
    /// 故不依赖页面级 @layout，而在路由层统一裁决，确保登录/安装页始终以 IdentityLayout 渲染。
    /// </summary>
    private Type PickLayout()
    {
        var path = new Uri(Navigation.Uri).AbsolutePath.TrimEnd('/');
        return path is "/sign-in" or "/sign-up" or "/forgot-password" or "/setup"
            ? typeof(IdentityLayout)
            : typeof(MainLayout);
    }
}
