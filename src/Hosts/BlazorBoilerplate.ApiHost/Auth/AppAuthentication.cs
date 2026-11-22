using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BlazorBoilerplate.ApiHost.Auth;

public static class AppAuthSchemes
{
    public const string Bearer = "AppBearer";
}

/// <summary>
/// 校验自签 JWT 的 Bearer 认证处理器（对应 Bluebird 的 AuthDelegatingHandler 服务端侧）。
/// 无 Authorization 头时按匿名放行：平台自身端点/模块端点的权限由 PermissionGuard 在已认证时校验。
/// </summary>
public sealed class AppBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    JwtTokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        string? rawToken = null;
        if (!string.IsNullOrWhiteSpace(header) && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            rawToken = header[7..].Trim();
        else if (Request.Path.StartsWithSegments("/hubs") &&
                 Request.Query.TryGetValue("access_token", out var qsToken) &&
                 !string.IsNullOrWhiteSpace(qsToken))
            rawToken = qsToken.ToString().Trim();

        if (string.IsNullOrWhiteSpace(rawToken))
            return Task.FromResult(AuthenticateResult.NoResult());

        var payload = tokens.Validate(rawToken, JwtTokenService.AccessType);
        if (payload is null)
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired access token."));

        var root = payload.RootElement;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, JwtTokenService.ReadString(root, "sub") ?? string.Empty),
            new(ClaimTypes.Name, JwtTokenService.ReadString(root, "name") ?? string.Empty),
            new(ClaimTypes.Email, JwtTokenService.ReadString(root, "email") ?? string.Empty),
            new("sid", JwtTokenService.ReadString(root, "sid") ?? string.Empty),
            new("tid", JwtTokenService.ReadString(root, "tid") ?? string.Empty)
        };
        claims.AddRange(JwtTokenService.ReadArray(root, "roles").Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(JwtTokenService.ReadArray(root, "perms").Select(p => new Claim("perm", p)));

        var identity = new ClaimsIdentity(claims, AppAuthSchemes.Bearer);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), AppAuthSchemes.Bearer);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>当前用户 claims 的便捷读取。</summary>
public static class AppUserExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    public static Guid GetSessionId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sid"), out var id) ? id : Guid.Empty;

    public static Guid? GetTenantId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("tid"), out var id) ? id : null;

    public static bool HasPermission(this ClaimsPrincipal user, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return true;

        var perms = user.FindAll("perm").Select(c => c.Value).ToList();
        if (perms.Any(p => p == "*" || string.Equals(p, code, StringComparison.OrdinalIgnoreCase)))
            return true;

        // 兼容历史 .write 权限码与细粒度 .create/.update/.delete 权限码
        if (code.EndsWith(".create", StringComparison.OrdinalIgnoreCase) ||
            code.EndsWith(".update", StringComparison.OrdinalIgnoreCase) ||
            code.EndsWith(".delete", StringComparison.OrdinalIgnoreCase))
        {
            var lastDot = code.LastIndexOf('.');
            var writeCode = string.Concat(code.AsSpan(0, lastDot), ".write");
            return perms.Any(p => string.Equals(p, writeCode, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }
}

/// <summary>Minimal API 端点所需的权限码元数据（由 Program.cs 权限中间件统一校验）。</summary>
public sealed record PermissionMetadata(string Permission);

public static class EndpointPermissionExtensions
{
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permission) =>
        builder.RequireAuthorization().WithMetadata(new PermissionMetadata(permission));
}

/// <summary>MVC Controller / Action 权限码校验特性（未登录返回 401，缺权限返回 403）。</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(string permission) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (!string.IsNullOrWhiteSpace(permission) && !user.HasPermission(permission))
        {
            context.Result = new ObjectResult(new { error = $"无权执行该操作（缺少权限：{permission}）。" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}

/// <summary>高权限二次认证令牌服务（15 分钟有效，绑定当前 SessionId）。</summary>
public sealed class ElevatedAccessService(IMemoryCache cache)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private static string Key(Guid sessionId) => $"elevated:{sessionId:N}";

    public (string Token, DateTimeOffset ExpiresAtUtc) Issue(Guid sessionId)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var expires = DateTimeOffset.UtcNow.Add(Lifetime);
        cache.Set(Key(sessionId), token, expires);
        return (token, expires);
    }

    public bool Validate(Guid sessionId, string? token)
    {
        if (sessionId == Guid.Empty || string.IsNullOrWhiteSpace(token))
            return false;
        return cache.TryGetValue(Key(sessionId), out string? expected) &&
               !string.IsNullOrWhiteSpace(expected) &&
               CryptographicOperations.FixedTimeEquals(
                   System.Text.Encoding.UTF8.GetBytes(expected),
                   System.Text.Encoding.UTF8.GetBytes(token.Trim()));
    }
}

/// <summary>高权限二次认证拦截器（对应 Bluebird AuthPolicies.ELEVATED_ACCESS）。</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireElevatedAccessAttribute : Attribute, IAsyncActionFilter
{
    public const string HeaderName = "X-Elevated-Token";
    public const string ErrorCode = "ELEVATED_ACCESS_REQUIRED";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var sessionId = user.GetSessionId();
        var token = context.HttpContext.Request.Headers[HeaderName].FirstOrDefault();
        var service = context.HttpContext.RequestServices.GetRequiredService<ElevatedAccessService>();
        if (!service.Validate(sessionId, token))
        {
            context.Result = new ObjectResult(new
            {
                error = "需要高权限二次验证，请先输入当前登录密码。",
                errorCode = ErrorCode
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}

/// <summary>ApiHost 认证与端点权限中间件扩展。</summary>
public static class AppAuthPipelineExtensions
{
    /// <summary>
    /// 注册自签 JWT Bearer 认证与授权服务：
    /// 不用 FallbackPolicy，平台自身的 setup / health / openapi / modules 端点保持原有访问规则，
    /// 需要登录的 MVC Controller 各自标 [Authorize]。
    /// </summary>
    public static IServiceCollection AddAppAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AppAuthSchemes.Bearer;
                options.DefaultChallengeScheme = AppAuthSchemes.Bearer;
            })
            .AddScheme<AuthenticationSchemeOptions, AppBearerHandler>(AppAuthSchemes.Bearer, null);
        services.AddAuthorization();
        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<ElevatedAccessService>();
        services.AddScoped<CurrentUserFactory>();
        return services;
    }

    /// <summary>统一校验动态模块端点（ModuleEndpointMetadata）与平台 Minimal API（PermissionMetadata）的登录态与权限码。</summary>
    public static IApplicationBuilder UseEndpointPermissionGuard(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var endpoint = context.GetEndpoint();
            if (endpoint is not null)
            {
                var moduleMeta = endpoint.Metadata.GetMetadata<BlazorBoilerplate.ModuleRuntime.Endpoints.ModuleEndpointMetadata>();
                var permMeta = endpoint.Metadata.GetMetadata<PermissionMetadata>();

                if (moduleMeta is not null || permMeta is not null)
                {
                    var requiredPermission = !string.IsNullOrWhiteSpace(permMeta?.Permission)
                        ? permMeta.Permission
                        : moduleMeta?.Permission;

                    if (string.Equals(requiredPermission, "anonymous", StringComparison.OrdinalIgnoreCase))
                    {
                        await next();
                        return;
                    }

                    if (context.User.Identity?.IsAuthenticated != true)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(requiredPermission) &&
                        !context.User.HasPermission(requiredPermission))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(
                            new { error = $"无权执行该操作（缺少权限：{requiredPermission}）。" },
                            context.RequestAborted);
                        return;
                    }
                }
            }

            await next();
        });
}
