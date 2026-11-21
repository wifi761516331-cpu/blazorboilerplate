using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.WebHost.Auth;

namespace BlazorBoilerplate.WebHost.Services;

/// <summary>
/// 可选域名路由契约分发中间件：
/// 仅当已安装模块向 <see cref="DomainRouteResolverRegistry"/> 注册了解析器时生效（零模块硬编码）。
/// 负责写入命中上下文、剥离显式模块前缀以及拦截未授权的后台路由。
/// </summary>
public sealed class DomainRoutingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IHttpClientFactory httpClientFactory)
    {
        var resolver = DomainRouteResolverRegistry.Current;
        if (resolver is not null && HttpMethods.IsGet(context.Request.Method))
        {
            var path = context.Request.Path.Value ?? "/";
            if (!IsSystemPath(path))
            {
                var http = httpClientFactory.CreateClient(WebAuth.RefreshClient);
                var match = await resolver.ResolveAsync(http, context.Request.Host.Host, context.RequestAborted);
                if (match is not null)
                {
                    context.Items[DomainRouteResolverRegistry.HttpItemKey] = match;

                    if (DomainRouteResolverRegistry.TryStripEntryPrefix(match, path, out var stripped))
                    {
                        context.Response.Redirect(stripped);
                        return;
                    }

                    if (DomainRouteResolverRegistry.ShouldRedirectAdminRoute(match, path))
                    {
                        context.Response.Redirect("/");
                        return;
                    }
                }
            }
        }

        await next(context);
    }

    private static bool IsSystemPath(string path) =>
        path.StartsWith("/_", StringComparison.Ordinal)
        || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase)
        || path is "/sign-in" or "/sign-up" or "/forgot-password" or "/setup";
}

public static class DomainRoutingExtensions
{
    public static IApplicationBuilder UseDomainRouting(this IApplicationBuilder app) =>
        app.UseMiddleware<DomainRoutingMiddleware>();
}
