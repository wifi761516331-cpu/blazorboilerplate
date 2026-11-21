using System.Net.Http.Headers;

namespace BlazorBoilerplate.Web.Shared.Services;

/// <summary>
/// 当前 Blazor 线路（circuit）的认证会话。WebHost 在登录时把 JWT 写进加密的 Cookie 认证票据，
/// 线路启动时从 ClaimsPrincipal 取出令牌存于此 scoped 服务，供整个线路复用。
/// </summary>
public interface IAuthSession
{
    string? AccessToken { get; }
    string? RefreshToken { get; }
    void Set(string? accessToken, string? refreshToken);

    /// <summary>
    /// 返回「当前可用」的 access token。临期或过期时用 refresh token 静默换新（服务端完成，
    /// 浏览器全程无感），多个并发请求共享同一次刷新；刷新失败返回 null（调用方按未登录处理）。
    /// </summary>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken);
}

/// <summary>当前 Blazor 线路或 HTTP 请求捕获到的浏览器访问域名（用于转发 X-Tenant-Host 给 ApiHost）。</summary>
public sealed class TenantHostContext
{
    public string? Host { get; set; }
}

/// <summary>当前 Blazor 线路的短期高权限二次认证令牌（ELEVATED_ACCESS）。</summary>
public sealed class ElevatedAccessContext
{
    public string? Token { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public bool IsValid => !string.IsNullOrWhiteSpace(Token) && ExpiresAtUtc > DateTimeOffset.UtcNow;

    public void Set(string token, DateTimeOffset expiresAtUtc)
    {
        Token = token;
        ExpiresAtUtc = expiresAtUtc;
    }

    public void Clear()
    {
        Token = null;
        ExpiresAtUtc = null;
    }
}

/// <summary>
/// 对应 Bluebird 的 AuthDelegatingHandler：对内部 API 请求附加 Bearer access_token（以及 X-Tenant-Host / X-Elevated-Token），
/// 并在令牌临期时先做无感刷新。匿名端点（setup / Identity / PushNotification）不附加，
/// 避免登录前拿过期令牌去撞 /Identity/SignIn。
/// </summary>
public sealed class AuthHeaderHandler(
    IAuthSession session,
    TenantHostContext? tenantHost = null,
    ElevatedAccessContext? elevated = null) : DelegatingHandler
{
    public const string TenantHostHeader = "X-Tenant-Host";
    public const string ElevatedAccessHeader = "X-Elevated-Token";

    private static readonly string[] AnonymousPrefixes =
        ["api/setup", "api/v1/identity", "api/v1/pushnotification", "api/v1/tenant/resolvebydomain"];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(tenantHost?.Host) && !request.Headers.Contains(TenantHostHeader))
            request.Headers.TryAddWithoutValidation(TenantHostHeader, tenantHost.Host);

        if (elevated?.IsValid == true && !request.Headers.Contains(ElevatedAccessHeader))
            request.Headers.TryAddWithoutValidation(ElevatedAccessHeader, elevated.Token);

        var path = request.RequestUri?.AbsolutePath.TrimStart('/') ?? string.Empty;
        var isAnonymous = AnonymousPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        if (!isAnonymous && request.Headers.Authorization is null)
        {
            var token = await session.GetAccessTokenAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
