using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
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
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var payload = tokens.Validate(header[7..].Trim(), JwtTokenService.AccessType);
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

    public static bool HasPermission(this ClaimsPrincipal user, string code) =>
        user.FindAll("perm").Any(c => c.Value == "*" || string.Equals(c.Value, code, StringComparison.OrdinalIgnoreCase));
}
