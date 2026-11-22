using System.Net.Http.Json;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Web.Shared.Services;

namespace BlazorBoilerplate.WebHost.Auth;

/// <summary>
/// 未登录首屏 HTTP 守卫：
/// 1) 首次运行未安装时（!Installed && !DevBypass）优先 302 跳转至 /setup 初始化向导；
/// 2) 已安装且未登录时直接以 302 跳转至 /sign-in，消除先渲染 Home 再跳登录页的闪烁；
/// 3) C 端公开域名（AllowAdminRoutes == false）与公共/静态路径自动放行。
/// </summary>
public sealed class AuthGateMiddleware(RequestDelegate next)
{
    private volatile bool _installedConfirmed;

    public async Task InvokeAsync(HttpContext context, IHttpClientFactory httpClientFactory)
    {
        if (HttpMethods.IsGet(context.Request.Method) &&
            context.User.Identity?.IsAuthenticated != true)
        {
            var path = context.Request.Path.Value ?? "/";

            if (!_installedConfirmed && !IsSystemOrAssetPath(path) && !string.Equals(path, "/setup", StringComparison.OrdinalIgnoreCase))
            {
                var needsSetup = await CheckNeedsSetupAsync(httpClientFactory, context.RequestAborted);
                if (needsSetup)
                {
                    context.Response.Redirect("/setup");
                    return;
                }
            }

            if (!IsPublicOrAssetPath(path) && !IsPublicPortalRequest(context))
            {
                var currentUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
                context.Response.Redirect($"/sign-in?return-url={Uri.EscapeDataString(currentUrl)}");
                return;
            }
        }

        await next(context);
    }

    private async Task<bool> CheckNeedsSetupAsync(IHttpClientFactory httpClientFactory, CancellationToken ct)
    {
        try
        {
            var http = httpClientFactory.CreateClient(WebAuth.RefreshClient);
            var status = await http.GetFromJsonAsync<SetupStatus>("api/setup/status", ct);
            if (status is null)
                return false;

            if (status.Installed || status.DevBypass)
            {
                _installedConfirmed = true;
                return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPublicPortalRequest(HttpContext context) =>
        context.Items.TryGetValue(DomainRouteResolverRegistry.HttpItemKey, out var raw) &&
        raw is DomainRouteMatch { AllowAdminRoutes: false };

    private static bool IsSystemOrAssetPath(string path) =>
        path.StartsWith("/_", StringComparison.Ordinal)
        || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase)
        || path is "/Error"
        || Path.HasExtension(path);

    private static bool IsPublicOrAssetPath(string path) =>
        IsSystemOrAssetPath(path)
        || path is "/sign-in" or "/sign-up" or "/forgot-password" or "/setup"
        || path.Equals("/site", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/site/", StringComparison.OrdinalIgnoreCase);
}

public static class AuthGateExtensions
{
    public static IApplicationBuilder UseAuthGate(this IApplicationBuilder app) =>
        app.UseMiddleware<AuthGateMiddleware>();
}
