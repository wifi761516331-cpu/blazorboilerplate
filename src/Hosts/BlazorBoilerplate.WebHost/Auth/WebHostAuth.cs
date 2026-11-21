using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlazorBoilerplate.WebHost.Auth;

/// <summary>多账号条目：完整 refresh_token 只存在加密的 Cookie 票据里。</summary>
public sealed record AccountEntry(
    string AccountId, string UserId, string UserName, string Email, string DisplayName, string RefreshToken);

/// <summary>ApiHost 的 TokenResponseDto / SignInResponseDto 的精简投影。</summary>
public sealed record TokenPayload(string AccessToken, string RefreshToken, int ExpiresIn);

/// <summary>
/// 浏览器 ↔ WebHost 这一段的认证常量与注册。
/// 组合认证（回应「用 Cookie 还是 JWT」）：登录接口签发 JWT（access 2 分钟 / refresh 7 天），
/// WebHost 把 JWT 放进【加密的 HttpOnly Cookie 认证票据】；浏览器只见 Cookie，
/// WebHost 调 ApiHost 时再从票据取出 JWT 附加 Bearer。这样 Blazor Interactive Server 的
/// SSR 首屏与 SignalR 线路都能拿到认证态，同时保留 Bluebird 的 access/refresh 契约。
/// </summary>
public static class WebAuth
{
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string RefreshClient = "bb-auth-refresh";
    public const string ApiClient = "bb-api-backend";

    public const string AccessClaim = "bb_access";
    public const string RefreshClaim = "bb_refresh";
    public const string ExpiryClaim = "bb_exp";
    public const string TenantClaim = "bb_tid";
    public const string DisplayNameClaim = "bb_display";
    public const string AccountsClaim = "bb_accounts";

    internal const string IdentityBase = "api/v1/Identity/";
    internal const string UserBase = "api/v1/User/";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static IServiceCollection AddWebHostAuth(this IServiceCollection services, string apiBaseUrl)
    {
        services.AddAuthentication(CookieScheme)
            .AddCookie(CookieScheme, options =>
            {
                options.Cookie.Name = "bb_auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.Path = "/";
                options.LoginPath = "/sign-in";
                options.AccessDeniedPath = "/sign-in";
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(7); // 与 refresh_token 有效期一致
                // fetch / Blazor 请求不要 302 到登录页 HTML，直接给 401。
                options.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization();
        services.AddCascadingAuthenticationState();
        services.AddHttpContextAccessor();
        services.AddScoped<IAuthSession, CookieAuthSession>();
        services.AddScoped<ElevatedAccessContext>();
        services.AddScoped(sp =>
        {
            var host = sp.GetService<IHttpContextAccessor>()?.HttpContext?.Request.Host.Host;
            return new TenantHostContext { Host = host };
        });
        services.AddTransient<AuthHeaderHandler>();

        // 独立的 HttpClient：不带 AuthHeaderHandler，避免 IAuthSession → ApiClient → Handler → IAuthSession 构造循环。
        services.AddHttpClient(RefreshClient, client =>
        {
            client.BaseAddress = new Uri(apiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // 底层连接池由 IHttpClientFactory 管理；BlazorBoilerplateApiClient 与 AuthHeaderHandler 必须绑定到
        // 当前 Blazor Circuit / HTTP 请求的 Scoped 容器中，避免 AddHttpMessageHandler 将 IAuthSession 困在
        // IHttpClientFactory 2 分钟全局复用的 Handler 作用域内导致跨账号/跨请求串号。
        services.AddHttpClient(ApiClient, client =>
        {
            client.BaseAddress = new Uri(apiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped(sp =>
        {
            var handlerFactory = sp.GetRequiredService<IHttpMessageHandlerFactory>();
            var session = sp.GetRequiredService<IAuthSession>();
            var tenantHost = sp.GetService<TenantHostContext>();
            var elevated = sp.GetService<ElevatedAccessContext>();
            var handler = new AuthHeaderHandler(session, tenantHost, elevated)
            {
                InnerHandler = handlerFactory.CreateHandler(ApiClient)
            };
            return new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = new Uri(apiBaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
        });

        services.AddScoped(sp => new BlazorBoilerplateApiClient(sp.GetRequiredService<HttpClient>()));

        return services;
    }

    internal static string? Find(ClaimsPrincipal? principal, string claim) =>
        principal?.FindFirst(claim)?.Value;

    /// <summary>多账号列表存在【加密】票据里，浏览器 JS 只拿 accountId，绝不接触 refresh_token。</summary>
    internal static List<AccountEntry> ReadAccounts(ClaimsPrincipal? principal)
    {
        var raw = Find(principal, AccountsClaim);
        if (string.IsNullOrWhiteSpace(raw)) return [];
        try { return JsonSerializer.Deserialize<List<AccountEntry>>(raw, Json) ?? []; }
        catch { return []; }
    }

    internal static List<AccountEntry> UpsertAccount(ClaimsPrincipal? principal, AccountEntry entry)
    {
        var list = ReadAccounts(principal)
            .Where(a =>
                !string.Equals(a.UserId, entry.UserId, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(entry.Email) || !string.Equals(a.Email, entry.Email, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(entry.UserName) || !string.Equals(a.UserName, entry.UserName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        list.Insert(0, entry);
        return list.Take(8).ToList();
    }

    /// <summary>JWT payload 的 exp（Unix 秒）；票据 claim 作为兜底。</summary>
    internal static DateTimeOffset ParseExpiry(string? accessToken, string? rawExpClaim)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                var parts = accessToken.Split('.');
                if (parts.Length == 3)
                {
                    var padded = parts[1].Replace('-', '+').Replace('_', '/');
                    padded += new string('=', (4 - padded.Length % 4) % 4);
                    using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(padded)));
                    if (doc.RootElement.TryGetProperty("exp", out var exp))
                        return DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64());
                }
            }
        }
        catch { /* 落到 claim 兜底 */ }

        return long.TryParse(rawExpClaim, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.MinValue;
    }
}

/// <summary>
/// 线路级认证会话：从 Cookie 票据 claims 读 JWT，发请求前若临期就用 refresh_token 静默换新。
/// 刷新在服务端完成，浏览器无感；refresh_token 不轮换（Cookie 票据不会失效）。
/// </summary>
public sealed class CookieAuthSession(
    AuthenticationStateProvider authState,
    IHttpContextAccessor httpContext,
    IHttpClientFactory httpFactory,
    ILogger<CookieAuthSession> logger) : IAuthSession
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _access;
    private string? _refresh;
    private string? _cookieAccess;
    private DateTimeOffset _expiresAtUtc = DateTimeOffset.MinValue;
    private bool _loaded;

    public string? AccessToken => _access;
    public string? RefreshToken => _refresh;

    public void Set(string? accessToken, string? refreshToken)
    {
        _cookieAccess = accessToken;
        _access = accessToken;
        _refresh = refreshToken;
        _expiresAtUtc = WebAuth.ParseExpiry(accessToken, null);
        _loaded = true;
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        if (IsUsable) return _access;
        if (string.IsNullOrWhiteSpace(_refresh)) return null;

        // 并发请求只刷新一次；拿到锁后二次检查，避免重复打 ApiHost。
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsUsable) return _access;

            var client = httpFactory.CreateClient(WebAuth.RefreshClient);
            using var response = await client.PostAsJsonAsync(
                WebAuth.IdentityBase + "Refresh",
                new { refreshToken = _refresh }, WebAuth.Json, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("access_token 无感刷新失败：{Status} {Body}",
                    (int)response.StatusCode, (await response.Content.ReadAsStringAsync(cancellationToken)).Truncate(200));
                return null;
            }

            var token = await response.Content.ReadFromJsonAsync<TokenPayload>(WebAuth.Json, cancellationToken);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken)) return null;

            _access = token.AccessToken;
            // 提前 15 秒视为临期；refresh_token 保持票据里那一个，不轮换。
            _expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn, 30) - 15);
            logger.LogDebug("access_token 已无感刷新，下次刷新窗口 {Expiry:HH:mm:ss}", _expiresAtUtc);
            return _access;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "access_token 无感刷新异常");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsUsable =>
        !string.IsNullOrWhiteSpace(_access) && _expiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(20);

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        var principal = await ResolvePrincipalAsync();
        var claimAccess = WebAuth.Find(principal, WebAuth.AccessClaim);
        var claimRefresh = WebAuth.Find(principal, WebAuth.RefreshClaim);

        // 若尚未加载，或当前认证票据中的令牌已发生变化（如切换账号、重新登录、切换租户），
        // 则以最新认证票据为准更新会话中的令牌。
        if (!string.IsNullOrWhiteSpace(claimAccess) || !string.IsNullOrWhiteSpace(claimRefresh))
        {
            if (!_loaded
                || !string.Equals(_cookieAccess, claimAccess, StringComparison.Ordinal)
                || !string.Equals(_refresh, claimRefresh, StringComparison.Ordinal))
            {
                _cookieAccess = claimAccess;
                _access = claimAccess;
                _refresh = claimRefresh;
                _expiresAtUtc = WebAuth.ParseExpiry(_access, WebAuth.Find(principal, WebAuth.ExpiryClaim));
                _loaded = true;
            }
            return;
        }

        if (!_loaded)
        {
            _loaded = true;
        }
    }

    private async Task<ClaimsPrincipal> ResolvePrincipalAsync()
    {
        if (httpContext.HttpContext?.User is { Identity.IsAuthenticated: true } httpUser)
            return httpUser;

        try
        {
            var state = await authState.GetAuthenticationStateAsync();
            if (state.User.Identity?.IsAuthenticated == true)
                return state.User;
        }
        catch
        {
            // 非 Blazor 组件作用域时回落 HttpContextAccessor。
        }

        return httpContext.HttpContext?.User ?? new ClaimsPrincipal();
    }
}

/// <summary>同源 /auth/* 端点：JS（app.js 的 blazorAuth）只负责调用，令牌始终留在服务端加密票据里。</summary>
public static class WebAuthEndpoints
{
    public static IEndpointRouteBuilder MapWebAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", LoginAsync).DisableAntiforgery();
        app.MapPost("/auth/register", RegisterAsync).DisableAntiforgery();
        app.MapPost("/auth/refresh", RefreshAsync).DisableAntiforgery();
        app.MapPost("/auth/switch-account", SwitchAccountAsync).DisableAntiforgery();
        app.MapPost("/auth/switch-tenant", SwitchTenantAsync).DisableAntiforgery();
        app.MapPost("/auth/logout", LogoutAsync).DisableAntiforgery();
        app.MapGet("/auth/accounts", AccountsAsync);
        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, IHttpClientFactory factory, HttpContext context, CancellationToken ct)
    {
        var client = factory.CreateClient(WebAuth.RefreshClient);
        using var message = new HttpRequestMessage(HttpMethod.Post, WebAuth.IdentityBase + "SignIn")
        {
            Content = JsonContent.Create(new
            {
                userName = request.UserName?.Trim() ?? string.Empty,
                password = request.Password ?? string.Empty,
                rememberMe = request.RememberMe,
                tenantId = request.TenantId
            }, options: WebAuth.Json)
        };
        if (!string.IsNullOrWhiteSpace(context.Request.Host.Host))
            message.Headers.TryAddWithoutValidation(AuthHeaderHandler.TenantHostHeader, context.Request.Host.Host);
        using var response = await client.SendAsync(message, ct);

        if (!response.IsSuccessStatusCode)
            return Fail(await ReadErrorAsync(response, ct));

        return await SignInFromAuthResponseAsync(response, request.RememberMe, context, ct);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request, IHttpClientFactory factory, HttpContext context, CancellationToken ct)
    {
        var client = factory.CreateClient(WebAuth.RefreshClient);
        using var response = await client.PostAsJsonAsync(WebAuth.IdentityBase + "Register", new
        {
            userName = request.UserName?.Trim() ?? string.Empty,
            email = request.Email?.Trim() ?? string.Empty,
            password = request.Password ?? string.Empty,
            displayName = request.DisplayName?.Trim(),
            verificationCode = request.VerificationCode?.Trim()
        }, WebAuth.Json, ct);

        if (!response.IsSuccessStatusCode)
            return Fail(await ReadErrorAsync(response, ct));

        return await SignInFromAuthResponseAsync(response, rememberMe: true, context, ct);
    }

    private static async Task<IResult> SignInFromAuthResponseAsync(
        HttpResponseMessage response, bool rememberMe, HttpContext context, CancellationToken ct)
    {
        var payload = await response.Content.ReadFromJsonAsync<SignInPayload>(WebAuth.Json, ct);
        if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
            return Fail("登录响应无效。");

        var user = payload.User ?? UserPayload.Empty;
        var accounts = WebAuth.UpsertAccount(context.User, new AccountEntry(
            Guid.NewGuid().ToString("N"), user.Id.ToString(), user.UserName, user.Email,
            user.DisplayName, payload.RefreshToken));

        await SignInCookieAsync(context, payload.AccessToken, payload.RefreshToken, payload.ExpiresIn,
            user, rememberMe, accounts, null);
        return Ok();
    }

    /// <summary>手动刷新票据里的 access_token（无感刷新主要由 circuit 内的 CookieAuthSession 完成）。</summary>
    private static async Task<IResult> RefreshAsync(
        IHttpClientFactory factory, HttpContext context, CancellationToken ct)
    {
        var refresh = WebAuth.Find(context.User, WebAuth.RefreshClaim);
        if (string.IsNullOrWhiteSpace(refresh)) return Fail("尚未登录。");

        var (token, error) = await PostRefreshAsync(factory, new { refreshToken = refresh }, ct);
        if (token is null) return Fail(error);

        await SignInCookieAsync(context, token.AccessToken, refresh, token.ExpiresIn,
            UserFromPrincipal(context.User), true, WebAuth.ReadAccounts(context.User), null);
        return Ok();
    }

    private static IResult AccountsAsync(HttpContext context)
    {
        var currentId = WebAuth.Find(context.User, ClaimTypes.NameIdentifier);
        var accounts = WebAuth.ReadAccounts(context.User)
            .Where(a => !string.Equals(a.UserId, currentId, StringComparison.OrdinalIgnoreCase))
            .Select(a => new { a.AccountId, a.UserId, a.UserName, a.Email, a.DisplayName })
            .ToArray();
        return Results.Json(accounts, WebAuth.Json);
    }

    private static async Task<IResult> SwitchAccountAsync(
        SwitchAccountRequest request, IHttpClientFactory factory, HttpContext context, CancellationToken ct)
    {
        var accounts = WebAuth.ReadAccounts(context.User);
        var entry = accounts.FirstOrDefault(a =>
            string.Equals(a.AccountId, request.AccountId, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return Fail("该账号已退出，请重新登录。");

        var (token, error) = await PostRefreshAsync(factory, new { refreshToken = entry.RefreshToken }, ct);
        if (token is null) return Fail(error);

        var user = await FetchUserAsync(factory, token.AccessToken, ct) ?? UserPayload.From(entry);
        await SignInCookieAsync(context, token.AccessToken, entry.RefreshToken, token.ExpiresIn,
            user, true, accounts, null);
        return Ok();
    }

    private static async Task<IResult> SwitchTenantAsync(
        SwitchTenantRequest request, IHttpClientFactory factory, HttpContext context, CancellationToken ct)
    {
        var refresh = WebAuth.Find(context.User, WebAuth.RefreshClaim);
        if (string.IsNullOrWhiteSpace(refresh)) return Fail("尚未登录。");

        // ApiHost 的 Refresh 会校验租户成员资格，并把新 tid 写进新签发的 access_token。
        var (token, error) = await PostRefreshAsync(factory,
            new { refreshToken = refresh, requestedTenantId = request.TenantId }, ct);
        if (token is null) return Fail(error);

        await SignInCookieAsync(context, token.AccessToken, refresh, token.ExpiresIn,
            UserFromPrincipal(context.User), true, WebAuth.ReadAccounts(context.User), request.TenantId);
        return Ok();
    }

    private static async Task<IResult> LogoutAsync(
        LogoutRequest request, IHttpClientFactory factory, HttpContext context, CancellationToken ct)
    {
        var client = factory.CreateClient(WebAuth.RefreshClient);
        var access = WebAuth.Find(context.User, WebAuth.AccessClaim);

        // 吊销当前会话（幂等，失败不阻断退出）。
        if (!string.IsNullOrWhiteSpace(access))
        {
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, WebAuth.UserBase + "SignOut");
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
                using var response = await client.SendAsync(message, ct);
            }
            catch { /* ApiHost 不可达时仍允许本地登出 */ }
        }

        // 退出所有账号：逐个吊销 refresh_token（ApiHost 侧会话随即失效）。
        if (request.All)
        {
            foreach (var account in WebAuth.ReadAccounts(context.User))
            {
                try
                {
                    using var response = await client.PostAsJsonAsync(
                        WebAuth.IdentityBase + "RevokeRefreshToken",
                        new { refreshToken = account.RefreshToken }, WebAuth.Json, ct);
                }
                catch { /* 忽略单个账号的吊销失败 */ }
            }
        }

        await context.SignOutAsync(WebAuth.CookieScheme);
        return Ok();
    }

    // ---- helpers ----

    private static async Task SignInCookieAsync(
        HttpContext context, string access, string refresh, int expiresIn, UserPayload user,
        bool rememberMe, List<AccountEntry> accounts, string? tenantId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(WebAuth.DisplayNameClaim, user.DisplayName ?? string.Empty),
            new(WebAuth.AccessClaim, access),
            new(WebAuth.RefreshClaim, refresh),
            new(WebAuth.ExpiryClaim, DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresIn, 30))
                .ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            new(WebAuth.TenantClaim, tenantId ?? user.TenantId?.ToString() ?? string.Empty),
            new(WebAuth.AccountsClaim, JsonSerializer.Serialize(accounts, WebAuth.Json))
        };

        var identity = new ClaimsIdentity(claims, WebAuth.CookieScheme);
        var properties = new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            AllowRefresh = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(rememberMe ? 7 : 1)
        };
        await context.SignInAsync(WebAuth.CookieScheme, new ClaimsPrincipal(identity), properties);
    }

    private static async Task<(TokenPayload? Token, string? Error)> PostRefreshAsync(
        IHttpClientFactory factory, object body, CancellationToken ct)
    {
        try
        {
            var client = factory.CreateClient(WebAuth.RefreshClient);
            using var response = await client.PostAsJsonAsync(
                WebAuth.IdentityBase + "Refresh", body, WebAuth.Json, ct);
            if (!response.IsSuccessStatusCode)
                return (null, await ReadErrorAsync(response, ct));

            var token = await response.Content.ReadFromJsonAsync<TokenPayload>(WebAuth.Json, ct);
            return token is null || string.IsNullOrWhiteSpace(token.AccessToken)
                ? (null, "刷新令牌响应无效。")
                : (token, null);
        }
        catch (Exception ex)
        {
            return (null, $"无法连接 ApiHost：{ex.Message}");
        }
    }

    private static async Task<UserPayload?> FetchUserAsync(
        IHttpClientFactory factory, string access, CancellationToken ct)
    {
        try
        {
            var client = factory.CreateClient(WebAuth.RefreshClient);
            using var message = new HttpRequestMessage(HttpMethod.Get, WebAuth.UserBase + "GetCurrentUser");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            using var response = await client.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<UserPayload>(WebAuth.Json, ct);
        }
        catch { return null; }
    }

    private static UserPayload UserFromPrincipal(ClaimsPrincipal principal) => new(
        Guid.TryParse(WebAuth.Find(principal, ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty,
        WebAuth.Find(principal, ClaimTypes.Name) ?? string.Empty,
        WebAuth.Find(principal, ClaimTypes.Email) ?? string.Empty,
        WebAuth.Find(principal, WebAuth.DisplayNameClaim) ?? string.Empty,
        Guid.TryParse(WebAuth.Find(principal, WebAuth.TenantClaim), out var tid) ? tid : null);

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                    return error.GetString()!;
                if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                    return detail.GetString()!;
                if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                    return title.GetString()!;
            }
        }
        catch { /* 非 JSON：直接截断返回 */ }
        return string.IsNullOrWhiteSpace(body)
            ? $"ApiHost 返回 {(int)response.StatusCode}"
            : body.Truncate(200);
    }

    private static IResult Ok() => Results.Json(new { ok = true });
    private static IResult Fail(string? error) => Results.Json(new { ok = false, error = error ?? "操作失败。" });

    // ---- DTO（与 ApiHost 的 camelCase JSON 对齐）----

    public sealed record LoginRequest(string? UserName, string? Password, bool RememberMe = true, string? TenantId = null);
    public sealed record RegisterRequest(string? UserName, string? Email, string? Password, string? DisplayName = null, string? VerificationCode = null);
    public sealed record SwitchAccountRequest(string? AccountId);
    public sealed record SwitchTenantRequest(string? TenantId);
    public sealed record LogoutRequest(bool All = false);
    public sealed record SignInPayload(string AccessToken, string RefreshToken, int ExpiresIn, UserPayload? User);

    public sealed record UserPayload(Guid Id, string UserName, string Email, string DisplayName, Guid? TenantId)
    {
        public static UserPayload Empty { get; } = new(Guid.Empty, string.Empty, string.Empty, string.Empty, null);
        public static UserPayload From(AccountEntry a) =>
            new(Guid.TryParse(a.UserId, out var id) ? id : Guid.Empty, a.UserName, a.Email, a.DisplayName, null);
    }

}

internal static class StringExtensions
{
    internal static string Truncate(this string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
