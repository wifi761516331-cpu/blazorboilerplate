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

/// <summary>
/// 对应 Bluebird 的 AuthDelegatingHandler：对内部 API 请求附加 Bearer access_token，
/// 并在令牌临期时先做无感刷新。匿名端点（setup / Identity / PushNotification）不附加，
/// 避免登录前拿过期令牌去撞 /Identity/SignIn。
/// </summary>
public sealed class AuthHeaderHandler(IAuthSession session) : DelegatingHandler
{
    private static readonly string[] AnonymousPrefixes =
        ["api/setup", "api/v1/identity", "api/v1/pushnotification"];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
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
