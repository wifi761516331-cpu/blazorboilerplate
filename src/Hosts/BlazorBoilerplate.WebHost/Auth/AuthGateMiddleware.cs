using BlazorBoilerplate.ModuleAbstractions;

namespace BlazorBoilerplate.WebHost.Auth;

/// <summary>
/// 未登录首屏 HTTP 守卫：在进入 Blazor SSR 渲染前直接以 302 跳转至 /sign-in，
/// 消除打开受保护页面（如 /）时先渲染 Home 仪表盘再跳登录页的闪烁。
/// C 端公开域名（AllowAdminRoutes == false）与公共/静态路径自动放行。
/// </summary>
public sealed class AuthGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsGet(context.Request.Method) &&
            context.User.Identity?.IsAuthenticated != true)
        {
            var path = context.Request.Path.Value ?? "/";
            if (!IsPublicOrAssetPath(path) && !IsPublicPortalRequest(context))
            {
                var currentUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
                context.Response.Redirect($"/sign-in?return-url={Uri.EscapeDataString(currentUrl)}");
                return;
            }
        }

        await next(context);
    }

    private static bool IsPublicPortalRequest(HttpContext context) =>
        context.Items.TryGetValue(DomainRouteResolverRegistry.HttpItemKey, out var raw) &&
        raw is DomainRouteMatch { AllowAdminRoutes: false };

    private static bool IsPublicOrAssetPath(string path)
    {
        if (path.StartsWith("/_", StringComparison.Ordinal)
            || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase)
            || path is "/sign-in" or "/sign-up" or "/forgot-password" or "/setup" or "/Error")
        {
            return true;
        }

        return Path.HasExtension(path);
    }
}

public static class AuthGateExtensions
{
    public static IApplicationBuilder UseAuthGate(this IApplicationBuilder app) =>
        app.UseMiddleware<AuthGateMiddleware>();
}
