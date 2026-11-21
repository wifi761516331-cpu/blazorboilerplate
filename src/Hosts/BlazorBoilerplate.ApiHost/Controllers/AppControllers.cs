using System.Security.Claims;
using BlazorBoilerplate.ApiHost.Auth;
using BlazorBoilerplate.Application.OData;
using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.AspNetCore.SignalR;
using BlazorBoilerplate.ApiHost.Services;
using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Infrastructure.Email;
using BlazorBoilerplate.Modules.Identity.Domain;
using BlazorBoilerplate.Modules.Identity.Persistence;
using BlazorBoilerplate.Modules.Identity.Services;
using BlazorBoilerplate.Shared.Features.Account;
using BlazorBoilerplate.Shared.Features.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.ApiHost.Controllers;

/// <summary>
/// Bluebird 风格服务端实现：共享契约接口（Web.Shared/Features）+ MVC Controller 同名路由。
/// 路由统一 api/v1/[controller]/[action]，默认全部 RequireAuthorization（在 Program.cs 配置），
/// 仅 IdentityController 与 PushNotificationController 允许匿名。
/// </summary>
public abstract class AppControllerBase(
    IdentityDbContext db,
    JwtTokenService tokens,
    CurrentUserFactory userFactory) : ControllerBase
{
    protected IdentityDbContext Db => db;
    protected JwtTokenService Tokens => tokens;
    protected CurrentUserFactory UserFactory => userFactory;

    protected Guid UserId => User.GetUserId();
    protected Guid SessionId => User.GetSessionId();
    protected Guid? TenantId => User.GetTenantId();
    protected string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    protected async Task<BlazorBoilerplate.Modules.Identity.Domain.User> RequireUserAsync(CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == UserId, ct)
        ?? throw new KeyNotFoundException("用户不存在或登录已失效。");

    protected IActionResult Fail(Exception ex) => ex switch
    {
        KeyNotFoundException => NotFound(new { error = ex.Message }),
        InvalidOperationException => BadRequest(new { error = ex.Message }),
        UnauthorizedAccessException => Unauthorized(new { error = ex.Message }),
        _ => Problem(ex.Message)
    };
}

/// <summary>登录 / 无感刷新 / 吊销 refresh token / 支持语言。</summary>
[ApiController, AllowAnonymous]
[Route("api/v1/[controller]/[action]")]
public sealed class IdentityController(
    IdentityDbContext db,
    JwtTokenService tokens,
    CurrentUserFactory factory,
    SystemSettingsStore settings,
    AccountRegistrationService registrationService)
    : AppControllerBase(db, tokens, factory), IIdentityController
{
    private static readonly string[] Cultures = ["zh-CN", "en-US", "ja-JP", "de-DE", "fr-FR", "ko-KR"];

    [HttpPost]
    public async Task<SignInResponseDto> SignIn(SignInRequestDto request, CancellationToken cancellationToken)
    {
        var account = (request.UserName ?? string.Empty).Trim();
        var user = await Db.Users.AsNoTracking().FirstOrDefaultAsync(
            u => u.UserName.ToLower() == account.ToLower() || u.Email.ToLower() == account.ToLower() || (TenantValidator.NormalizePhone(account) != null && u.PhoneNumber == TenantValidator.NormalizePhone(account)), cancellationToken);
        if (user is null || !user.IsActive || !PasswordHasher.Verify(request.Password ?? string.Empty, user.PasswordHash))
            throw new UnauthorizedAccessException("账号或密码不正确，或账号已停用。");

        var sys = settings.GetRaw();
        if (sys.MaxSessionsPerUser > 0)
        {
            // SQLite 不支持在 SQL ORDER BY 中对 DateTimeOffset 排序，先查出再在内存排序（保留 EF 跟踪以支持 Revoke）
            var activeSessions = (await Db.UserSessions
                .Where(s => s.UserId == user.Id && s.RevokedAtUtc == null)
                .ToListAsync(cancellationToken))
                .OrderBy(s => s.StartedOnUtc)
                .ToList();
            var excess = activeSessions.Count - (sys.MaxSessionsPerUser - 1);
            for (var i = 0; i < excess; i++)
                activeSessions[i].Revoke();
        }

        Guid? tenantId = Guid.TryParse(request.TenantId, out var tid)
            ? tid
            : HttpContext.GetResolvedTenant()?.Id;
        if (tenantId is { } targetTid)
        {
            var tenant = await Db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == targetTid, cancellationToken);
            if (tenant is null || !tenant.IsActive)
                throw new UnauthorizedAccessException("目标租户不存在或已停用。");

            var isMember = await Db.TenantUsers.AsNoTracking()
                .AnyAsync(t => t.TenantId == targetTid && t.UserId == user.Id && t.Accepted, cancellationToken);
            if (!isMember)
                throw new UnauthorizedAccessException($"当前账号未加入租户「{tenant.Title ?? tenant.Name}」，无权登录。");
        }
        var defaultCulture = string.IsNullOrWhiteSpace(sys.DefaultCulture) ? "zh-CN" : sys.DefaultCulture;
        var defaultTimeZone = string.IsNullOrWhiteSpace(sys.DefaultTimeZone) ? "Asia/Shanghai" : sys.DefaultTimeZone;
        var session = UserSession.Create(user.Id, tenantId, ClientIp, "Blazor Server", defaultCulture, defaultTimeZone, false, null);
        Db.UserSessions.Add(session);

        var refresh = Tokens.IssueRefreshToken(user.Id, session.Id, tenantId);
        session.Renew(refresh);
        await Db.SaveChangesAsync(cancellationToken);

        var current = await factory.BuildAsync(user, tenantId, cancellationToken);
        var access = Tokens.IssueAccessToken(user.Id, user.UserName, user.Email, session.Id, tenantId,
            current.Roles, current.Permissions);
        return new SignInResponseDto(access, refresh, Tokens.AccessTokenLifetimeSeconds, current);
    }

    [HttpPost]
    [EnableRateLimiting("auth-sensitive")]
    public Task<SignInResponseDto> Register(RegisterRequestDto request, CancellationToken cancellationToken) =>
        registrationService.RegisterAsync(request, ClientIp, cancellationToken);

    [HttpPost]
    [EnableRateLimiting("auth-sensitive")]
    public Task SendEmailCode(SendEmailCodeRequestDto request, CancellationToken cancellationToken) =>
        registrationService.SendEmailCodeAsync(request, cancellationToken);

    [HttpPost]
    [EnableRateLimiting("auth-sensitive")]
    public Task ResetPasswordByEmail(ResetPasswordByEmailRequestDto request, CancellationToken cancellationToken) =>
        registrationService.ResetPasswordByEmailAsync(request, cancellationToken);

    [HttpPost]
    public async Task<TokenResponseDto> Refresh(RefreshTokenRequestDto request, CancellationToken cancellationToken)
    {
        var payload = Tokens.Validate(request.RefreshToken, JwtTokenService.RefreshType)
            ?? throw new UnauthorizedAccessException("refresh_token 无效或已过期，请重新登录。");
        var sessionId = Guid.Parse(JwtTokenService.ReadString(payload.RootElement, "sid")!);
        var userId = Guid.Parse(JwtTokenService.ReadString(payload.RootElement, "sub")!);

        var session = await Db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, cancellationToken)
            ?? throw new UnauthorizedAccessException("会话不存在。");
        // 数据库里的 token 必须匹配，且会话未被吊销。
        // 注意：refresh_token 不轮换（见下方说明），所以这里校验的是「登录时签发的那一个」。
        if (session.RevokedAtUtc is not null || !string.Equals(session.RefreshToken, request.RefreshToken, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("refresh_token 已失效或被吊销，请重新登录。");

        var user = await Db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new UnauthorizedAccessException("用户不存在。");
        if (!user.IsActive) throw new UnauthorizedAccessException("账号已停用。");

        Guid? tenantId = session.TenantId;
        if (Guid.TryParse(request.RequestedTenantId, out var requested))
        {
            var tenant = await Db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == requested, cancellationToken);
            if (tenant is null || !tenant.IsActive)
                throw new UnauthorizedAccessException("目标租户不存在或已停用。");

            var link = await Db.TenantUsers.FirstOrDefaultAsync(
                t => t.TenantId == requested && t.UserId == userId, cancellationToken)
                ?? throw new UnauthorizedAccessException("无权访问该租户。");
            if (!link.Accepted)
            {
                link.Accept();
                await TenantValidator.EnsureTenantMemberRoleAsync(Db, requested, userId, cancellationToken);
            }

            tenantId = requested;
            session.SwitchTenant(requested);
        }
        else if (tenantId is { } currentTid)
        {
            var tenant = await Db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == currentTid, cancellationToken);
            if (tenant is null || !tenant.IsActive)
                throw new UnauthorizedAccessException("当前租户不存在或已停用。");

            var isMember = await Db.TenantUsers.AsNoTracking()
                .AnyAsync(t => t.TenantId == currentTid && t.UserId == userId && t.Accepted, cancellationToken);
            if (!isMember)
                throw new UnauthorizedAccessException("您已不是该租户成员，请重新登录。");
        }

        // 不轮换 refresh_token：WebHost 把它放在 HttpOnly Cookie 认证票据里，
        // 而 Blazor Server 的 circuit 运行在服务端、无法回写浏览器 Cookie；
        // 一旦轮换，票据里的旧 token 立刻失效，用户下次开页面就会被登出。
        // 因此 refresh_token 在 7 天有效期内保持稳定，仅由 SignOut / RevokeRefreshToken 吊销。
        session.Renew();
        await Db.SaveChangesAsync(cancellationToken);

        var current = await factory.BuildAsync(user, tenantId, cancellationToken);
        var access = Tokens.IssueAccessToken(user.Id, user.UserName, user.Email, session.Id, tenantId,
            current.Roles, current.Permissions);
        return new TokenResponseDto(access, request.RefreshToken, Tokens.AccessTokenLifetimeSeconds);
    }

    [HttpPost]
    public async Task RevokeRefreshToken(RefreshTokenRequestDto request, CancellationToken cancellationToken)
    {
        var session = await Db.UserSessions.FirstOrDefaultAsync(
            s => s.RefreshToken == request.RefreshToken, cancellationToken);
        if (session is null) return;
        session.Revoke();
        await Db.SaveChangesAsync(cancellationToken);
    }

    [HttpGet]
    public async Task<IReadOnlyList<string>> GetSupportedCultures(
        [FromServices] BlazorBoilerplate.Application.Localization.ILocalizationService localization,
        CancellationToken cancellationToken)
    {
        var languages = await localization.GetSupportedLanguagesAsync(cancellationToken);
        return languages.Where(l => l.IsEnabled).Select(l => l.Culture).ToArray();
    }
}

/// <summary>当前用户 / 资料 / 会话 / 通知开关 / 租户成员 / 按钮级权限码。</summary>
[ApiController, Authorize]
[Route("api/v1/[controller]/[action]")]
public sealed class UserController(IdentityDbContext db, JwtTokenService tokens, CurrentUserFactory factory)
    : AppControllerBase(db, tokens, factory), IUserController
{
    [HttpGet]
    public async Task<CurrentUserDto> GetCurrentUser(CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(cancellationToken);
        return await factory.BuildAsync(user, TenantId, cancellationToken);
    }

    [HttpPost]
    public async Task SignOut(CancellationToken cancellationToken)
    {
        var session = await Db.UserSessions.FirstOrDefaultAsync(s => s.Id == SessionId, cancellationToken);
        if (session is null) return;
        session.Revoke();
        await Db.SaveChangesAsync(cancellationToken);
    }

    [HttpPut]
    public async Task<CurrentUserDto> Update(EditUserRequestDto request, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(cancellationToken);
        var email = (request.Email ?? string.Empty).Trim();
        if (await Db.Users.AnyAsync(u => u.Email == email && u.Id != user.Id, cancellationToken))
            throw new InvalidOperationException("邮箱已被占用。");
        user.Update(email, request.DisplayName);
        await Db.SaveChangesAsync(cancellationToken);
        return await factory.BuildAsync(user, TenantId, cancellationToken);
    }

    [HttpPost]
    public async Task ChangePassword(ChangePasswordRequestDto request, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(cancellationToken);
        if (!PasswordHasher.Verify(request.OldPassword ?? string.Empty, user.PasswordHash))
            throw new InvalidOperationException("原密码不正确。");
        var minLen = HttpContext.RequestServices.GetService<BlazorBoilerplate.ApiHost.Services.SystemSettingsStore>()
            ?.GetRaw().PasswordMinLength ?? 6;
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < minLen)
            throw new InvalidOperationException($"新密码至少 {minLen} 位。");
        user.SetPassword(PasswordHasher.Hash(request.NewPassword));
        await Db.SaveChangesAsync(cancellationToken);
    }

    [HttpGet]
    public async Task<IReadOnlyList<UserSessionDto>> GetUserSessions(CancellationToken cancellationToken)
    {
        var sessions = await Db.UserSessions.AsNoTracking()
            .Where(s => s.UserId == UserId && s.RevokedAtUtc == null)
            .OrderBy(s => s.Id).ToListAsync(cancellationToken);
        return sessions.Select(s => new UserSessionDto(s.Id, s.IP, s.DeviceInfo, s.CultureName, s.TimeZone,
            s.NotificationsEnabled, s.StartedOnUtc, s.Id == SessionId)).ToArray();
    }

    [HttpPost("{id}")]
    public async Task RevokeSession(Guid id, CancellationToken cancellationToken)
    {
        var session = await Db.UserSessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("会话不存在。");
        session.Revoke();
        await Db.SaveChangesAsync(cancellationToken);
    }

    [HttpPost]
    public async Task UpdateSession(UpdateUserSessionRequestDto request, CancellationToken cancellationToken)
    {
        var session = await Db.UserSessions.FirstOrDefaultAsync(s => s.Id == SessionId, cancellationToken);
        if (session is null) return;
        session.Update(request.CultureName, request.TimeZone, request.NotificationsEnabled, request.DeviceInfo);
        await Db.SaveChangesAsync(cancellationToken);
    }

    [HttpPost("{enabled}")]
    public async Task SetNotificationEnabled(bool enabled, CancellationToken cancellationToken)
    {
        var sessions = await Db.UserSessions.Where(s => s.UserId == UserId && s.RevokedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var session in sessions) session.SetNotificationsEnabled(enabled);
        var preference = await Db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == UserId, cancellationToken);
        if (preference is not null)
            preference.Update(preference.Culture, preference.TimeZone, preference.Theme, preference.AccentColor, enabled);
        await Db.SaveChangesAsync(cancellationToken);
    }

    [HttpGet]
    public async Task<IReadOnlyList<TenantDto>> GetTenants(CancellationToken cancellationToken)
    {
        var rows = await (from tu in Db.TenantUsers.AsNoTracking()
                          join t in Db.Tenants.AsNoTracking() on tu.TenantId equals t.Id
                          where tu.UserId == UserId
                          select new { t, tu.Accepted }).ToListAsync(cancellationToken);
        return rows.Select(x => new TenantDto(x.t.Id, x.t.Name, x.t.Title, x.t.Domain,
            x.t.IsActive, x.Accepted, x.Accepted && x.t.Id == TenantId)).ToArray();
    }

    [HttpPost("{tenantId}")]
    public async Task LeaveTenant(Guid tenantId, CancellationToken cancellationToken)
    {
        var link = await Db.TenantUsers.FirstOrDefaultAsync(
            t => t.TenantId == tenantId && t.UserId == UserId, cancellationToken)
            ?? throw new KeyNotFoundException("未加入该租户。");
        if (link.IsOwner)
        {
            var hasOtherOwner = await Db.TenantUsers.AsNoTracking().AnyAsync(
                t => t.TenantId == tenantId && t.UserId != UserId && t.IsOwner, cancellationToken);
            if (!hasOtherOwner)
                throw new InvalidOperationException("您是该租户唯一的所有者，无法直接退出。");
        }

        Db.TenantUsers.Remove(link);
        var tenantRoles = await Db.UserRoles
            .Where(ur => ur.UserId == UserId && ur.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        if (tenantRoles.Count > 0)
            Db.UserRoles.RemoveRange(tenantRoles);

        var revokedIds = await TenantValidator.RevokeTenantUserSessionsAsync(
            Db, tenantId, UserId, TenantId == tenantId ? SessionId : null, cancellationToken);
        await Db.SaveChangesAsync(cancellationToken);
        if (revokedIds.Count > 0)
        {
            var hub = HttpContext.RequestServices.GetRequiredService<IAppHubNotifier>();
            await TenantValidator.NotifySessionsRevokedAsync(hub, revokedIds, tenantId, "left_tenant", cancellationToken);
        }
    }

    [HttpGet]
    [RequirePermission("identity.permission.read")]
    public async Task<IReadOnlyList<PermissionDefinitionDto>> GetPermissionCatalog(CancellationToken cancellationToken)
    {
        var rows = await Db.PermissionDefinitions.AsNoTracking().OrderBy(p => p.Code).ToListAsync(cancellationToken);
        return rows.Select(p => new PermissionDefinitionDto(p.Code, p.Name, p.ModuleId, p.MenuPath, p.Kind)).ToArray();
    }

    [HttpGet("{roleId}")]
    [RequirePermission("identity.permission.read")]
    public async Task<IReadOnlyList<string>> GetRolePermissions(Guid roleId, CancellationToken cancellationToken) =>
        await Db.RolePermissions.AsNoTracking().Where(p => p.RoleId == roleId)
            .Select(p => p.Code).Distinct().ToArrayAsync(cancellationToken);

    [HttpPost("{roleId}")]
    [RequirePermission("identity.permission.write")]
    public async Task SetRolePermissions(Guid roleId, string[] codes, CancellationToken cancellationToken)
    {
        _ = await Db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken)
            ?? throw new KeyNotFoundException("角色不存在。");
        var valid = await Db.PermissionDefinitions.AsNoTracking().Select(p => p.Code).ToArrayAsync(cancellationToken);
        var wanted = (codes ?? []).Where(c => valid.Contains(c)).Distinct().ToHashSet(StringComparer.Ordinal);
        var existing = await Db.RolePermissions.Where(p => p.RoleId == roleId).ToListAsync(cancellationToken);
        foreach (var row in existing.Where(x => !wanted.Contains(x.Code))) Db.RolePermissions.Remove(row);
        foreach (var code in wanted.Where(c => existing.All(x => x.Code != c)))
            Db.RolePermissions.Add(new RolePermission(roleId, code));
        await Db.SaveChangesAsync(cancellationToken);
    }

    [HttpPost]
    public async Task<ElevatedAccessResponseDto> VerifyElevatedAccess(ElevatedAccessRequestDto request, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(cancellationToken);
        if (!user.IsActive || !PasswordHasher.Verify(request.Password ?? string.Empty, user.PasswordHash))
            throw new UnauthorizedAccessException("密码不正确，无法完成高权限二次验证。");
        var (token, expiresAtUtc) = HttpContext.RequestServices.GetRequiredService<ElevatedAccessService>().Issue(SessionId);
        return new ElevatedAccessResponseDto(token, expiresAtUtc);
    }
}

/// <summary>租户管理 / 切换（仅操作当前用户所属或当前已切入的租户）。</summary>
[ApiController, Authorize]
[Route("api/v1/[controller]/[action]")]
public sealed class TenantController(
    IdentityDbContext db,
    JwtTokenService tokens,
    CurrentUserFactory factory,
    IEmailSender emailSender,
    ISmsSender smsSender,
    ISmtpSettingsProvider smtpSettingsProvider,
    Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
    ILogger<TenantController> logger)
    : AppControllerBase(db, tokens, factory), ITenantController
{
    [HttpGet, AllowAnonymous]
    public TenantDto? ResolveByDomain()
    {
        var resolved = HttpContext.GetResolvedTenant();
        return resolved is null
            ? null
            : new TenantDto(resolved.Id, resolved.Name, resolved.Title, resolved.Domain, resolved.IsActive, true, true);
    }

    [HttpGet]
    public async Task<TenantDto?> GetCurrentTenant(CancellationToken cancellationToken)
    {
        if (TenantId is not { } tid) return null;
        var tenant = await Db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tid, cancellationToken);
        return tenant is null ? null : new TenantDto(tenant.Id, tenant.Name, tenant.Title, tenant.Domain,
            tenant.IsActive, true, true);
    }

    /// <summary>仅返回当前用户所属的租户（含待接受邀请），防止枚举全平台租户。</summary>
    [HttpGet]
    public async Task<IReadOnlyList<TenantDto>> GetTenants(CancellationToken cancellationToken)
    {
        var rows = await (from tu in Db.TenantUsers.AsNoTracking()
                          join t in Db.Tenants.AsNoTracking() on tu.TenantId equals t.Id
                          where tu.UserId == UserId
                          orderby t.Name
                          select new { t, tu.Accepted }).ToListAsync(cancellationToken);
        return rows.Select(x => new TenantDto(x.t.Id, x.t.Name, x.t.Title, x.t.Domain,
            x.t.IsActive, x.Accepted, x.Accepted && x.t.Id == TenantId)).ToArray();
    }

    [HttpPost]
    [RequirePermission("tenant.create"), RequireElevatedAccess]
    public async Task<TenantDto> Create(TenantDto request, CancellationToken cancellationToken)
    {
        var (name, title, domain) = await TenantValidator.ValidateAndNormalizeAsync(
            Db, null, request.Name, request.Title, request.Domain, HttpContext.Request.Host.Host, cancellationToken);

        var tenant = Tenant.Create(name, title, domain);
        Db.Tenants.Add(tenant);
        Db.TenantUsers.Add(TenantUser.Create(tenant.Id, UserId, isOwner: true, accepted: true));
        await TenantValidator.ProvisionTenantRolesAsync(Db, tenant.Id, UserId, cancellationToken);
        await Db.SaveChangesAsync(cancellationToken);
        TenantHttpItems.InvalidateLookupCache(cache);
        await TenantValidator.RunProvisioningHandlersAsync(HttpContext.RequestServices, tenant.Id, tenant.Name, cancellationToken);
        return new TenantDto(tenant.Id, tenant.Name, tenant.Title, tenant.Domain, tenant.IsActive, true, false);
    }

    [HttpPut]
    [RequirePermission("tenant.update"), RequireElevatedAccess]
    public async Task<TenantDto> Update(TenantDto request, CancellationToken cancellationToken)
    {
        if (TenantId is not { } currentTid || request.Id != currentTid)
            throw new UnauthorizedAccessException("只能修改当前已切入的租户。");

        var isAcceptedMember = await Db.TenantUsers.AsNoTracking()
            .AnyAsync(t => t.TenantId == currentTid && t.UserId == UserId && t.Accepted, cancellationToken);
        if (!isAcceptedMember)
            throw new UnauthorizedAccessException("您不是该租户的正式成员，无权修改。");

        var tenant = await Db.Tenants.FirstOrDefaultAsync(t => t.Id == currentTid, cancellationToken)
            ?? throw new KeyNotFoundException("租户不存在。");

        var (name, title, domain) = await TenantValidator.ValidateAndNormalizeAsync(
            Db, tenant.Id, request.Name, request.Title, request.Domain, HttpContext.Request.Host.Host, cancellationToken);

        // 租户内管理员不可自行修改 IsActive，仅平台全局管理员可通过 TenantManagementController 启停。
        tenant.Update(name, title, domain, tenant.IsActive);
        await Db.SaveChangesAsync(cancellationToken);
        TenantHttpItems.InvalidateLookupCache(cache);
        return new TenantDto(tenant.Id, tenant.Name, tenant.Title, tenant.Domain, tenant.IsActive, true, true);
    }

    [HttpPost]
    [RequirePermission("tenant.invite"), RequireElevatedAccess]
    public async Task InviteUser(InviteUserToTenantRequestDto request, CancellationToken cancellationToken)
    {
        if (TenantId is not { } currentTid || request.TenantId != currentTid)
            throw new UnauthorizedAccessException("只能向当前已切入的租户邀请成员。");

        var isAcceptedMember = await Db.TenantUsers.AsNoTracking()
            .AnyAsync(t => t.TenantId == currentTid && t.UserId == UserId && t.Accepted, cancellationToken);
        if (!isAcceptedMember)
            throw new UnauthorizedAccessException("您不是该租户的正式成员，无权邀请成员。");

        await TenantValidator.InviteUserAsync(Db, emailSender, smsSender, smtpSettingsProvider, logger, request.TenantId, UserId, request.Email, request.PhoneNumber, cancellationToken);
    }

    [HttpGet("{tenantId}")]
    [RequirePermission("tenant.invite")]
    public async Task<IReadOnlyList<TenantMemberDto>> GetMembers(Guid tenantId, CancellationToken cancellationToken)
    {
        if (TenantId is not { } currentTid || tenantId != currentTid)
            throw new UnauthorizedAccessException("只能查看当前已切入租户的成员。");

        var callerAccepted = await Db.TenantUsers.AsNoTracking()
            .AnyAsync(t => t.TenantId == currentTid && t.UserId == UserId && t.Accepted, cancellationToken);
        if (!callerAccepted)
            throw new UnauthorizedAccessException("您不是该租户的正式成员，无权查看成员列表。");

        return await (from tu in Db.TenantUsers.AsNoTracking()
                      join u in Db.Users.AsNoTracking() on tu.UserId equals u.Id
                      where tu.TenantId == currentTid
                      orderby tu.IsOwner descending, u.UserName
                      select new TenantMemberDto(u.Id, u.UserName, u.Email, u.PhoneNumber, u.DisplayName, tu.IsOwner, tu.Accepted))
            .ToArrayAsync(cancellationToken);
    }

    [HttpPost("{tenantId}/{userId}")]
    [RequirePermission("tenant.invite"), RequireElevatedAccess]
    public async Task RemoveUser(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        if (TenantId is not { } currentTid || tenantId != currentTid)
            throw new UnauthorizedAccessException("只能从当前已切入的租户移除成员。");

        var callerAccepted = await Db.TenantUsers.AsNoTracking()
            .AnyAsync(t => t.TenantId == currentTid && t.UserId == UserId && t.Accepted, cancellationToken);
        if (!callerAccepted)
            throw new UnauthorizedAccessException("您不是该租户的正式成员，无权移除成员。");

        var link = await Db.TenantUsers.FirstOrDefaultAsync(
            t => t.TenantId == tenantId && t.UserId == userId, cancellationToken)
            ?? throw new KeyNotFoundException("该用户不在当前租户中。");

        if (link.IsOwner)
        {
            var hasOtherOwner = await Db.TenantUsers.AsNoTracking().AnyAsync(
                t => t.TenantId == tenantId && t.UserId != userId && t.IsOwner, cancellationToken);
            if (!hasOtherOwner)
                throw new InvalidOperationException("无法移除该租户唯一的所有者。");
        }

        Db.TenantUsers.Remove(link);
        var tenantRoles = await Db.UserRoles
            .Where(ur => ur.UserId == userId && ur.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        if (tenantRoles.Count > 0)
            Db.UserRoles.RemoveRange(tenantRoles);

        var revokedIds = await TenantValidator.RevokeTenantUserSessionsAsync(
            Db, tenantId, userId, null, cancellationToken);
        await Db.SaveChangesAsync(cancellationToken);
        if (revokedIds.Count > 0)
        {
            var hub = HttpContext.RequestServices.GetRequiredService<IAppHubNotifier>();
            await TenantValidator.NotifySessionsRevokedAsync(hub, revokedIds, tenantId, "kicked_from_tenant", cancellationToken);
        }
    }

    [HttpPost("{tenantId}")]
    public async Task AcceptInvitation(Guid tenantId, CancellationToken cancellationToken)
    {
        var link = await Db.TenantUsers.FirstOrDefaultAsync(
            t => t.TenantId == tenantId && t.UserId == UserId, cancellationToken)
            ?? throw new KeyNotFoundException("邀请记录不存在。");
        link.Accept();
        await TenantValidator.EnsureTenantMemberRoleAsync(Db, tenantId, UserId, cancellationToken);
        await Db.SaveChangesAsync(cancellationToken);
    }

    [HttpPost("{tenantId}")]
    public async Task<TokenResponseDto> Switch(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await Db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null || !tenant.IsActive)
            throw new UnauthorizedAccessException("目标租户不存在或已停用。");

        var link = await Db.TenantUsers.FirstOrDefaultAsync(
            t => t.TenantId == tenantId && t.UserId == UserId, cancellationToken)
            ?? throw new UnauthorizedAccessException("无权访问该租户。");

        if (!link.Accepted)
        {
            link.Accept();
            await TenantValidator.EnsureTenantMemberRoleAsync(Db, tenantId, UserId, cancellationToken);
        }

        var session = await Db.UserSessions.FirstOrDefaultAsync(s => s.Id == SessionId, cancellationToken)
            ?? throw new UnauthorizedAccessException("会话不存在。");
        session.SwitchTenant(tenantId);
        var user = await Db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == UserId, cancellationToken)
            ?? throw new UnauthorizedAccessException("用户不存在。");
        var current = await factory.BuildAsync(user, tenantId, cancellationToken);
        // 与 Refresh 一致：切换租户只换 access_token（把新 tid 写进 claims），
        // refresh_token 保持登录时那一个，避免 HttpOnly Cookie 票据立刻失效。
        session.Renew();
        await Db.SaveChangesAsync(cancellationToken);
        var refresh = session.RefreshToken ?? string.Empty;
        var access = Tokens.IssueAccessToken(user.Id, user.UserName, user.Email, session.Id, tenantId,
            current.Roles, current.Permissions);
        return new TokenResponseDto(access, refresh, Tokens.AccessTokenLifetimeSeconds);
    }
}

/// <summary>平台全局租户管理（仅限平台超级管理员）。</summary>
[ApiController, Authorize]
[Route("api/v1/[controller]/[action]")]
[RequirePermission("tenant.manage.global")]
public sealed class TenantManagementController(
    IdentityDbContext db,
    JwtTokenService tokens,
    CurrentUserFactory factory,
    Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
    : AppControllerBase(db, tokens, factory), ITenantManagementController
{
    [HttpGet]
    public async Task<IReadOnlyList<TenantDto>> GetTenants(CancellationToken cancellationToken) =>
        await Db.Tenants.AsNoTracking().OrderBy(t => t.Name)
            .Select(t => new TenantDto(t.Id, t.Name, t.Title, t.Domain, t.IsActive, true, t.Id == TenantId))
            .ToArrayAsync(cancellationToken);

    private static readonly ODataFieldMap TenantODataFields = new(["id", "name", "title", "domain", "isactive"]);
    private static readonly ODataQueryApplier<TenantDto> TenantODataApplier = new(
        TenantODataFields,
        (t, f) => f.ToLowerInvariant() switch
        {
            "id" => t.Id,
            "name" => t.Name,
            "title" => t.Title,
            "domain" => t.Domain,
            "isactive" => t.IsActive,
            _ => null
        },
        (f, raw) => f.ToLowerInvariant() switch
        {
            "id" => Guid.Parse(raw),
            "isactive" => bool.Parse(raw),
            _ => raw
        });

    [HttpGet]
    public async Task<ODataPage<TenantDto>> QueryTenants(CancellationToken cancellationToken)
    {
        var items = await GetTenants(cancellationToken);
        var q = HttpContext.Request.Query;
        var query = new ODataQuery
        {
            Filter = q["$filter"].FirstOrDefault(),
            OrderBy = q["$orderby"].FirstOrDefault(),
            Top = int.TryParse(q["$top"].FirstOrDefault(), out var top) ? top : null,
            Skip = int.TryParse(q["$skip"].FirstOrDefault(), out var skip) ? skip : null,
            Count = string.Equals(q["$count"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase)
        };
        return TenantODataApplier.Apply(items, query);
    }

    [HttpPost, RequireElevatedAccess]
    public async Task<TenantDto> Create(TenantDto request, CancellationToken cancellationToken)
    {
        var (name, title, domain) = await TenantValidator.ValidateAndNormalizeAsync(
            Db, null, request.Name, request.Title, request.Domain, HttpContext.Request.Host.Host, cancellationToken);

        var tenant = Tenant.Create(name, title, domain);
        if (!request.IsActive)
            tenant.Update(name, title, domain, false);

        Db.Tenants.Add(tenant);
        Db.TenantUsers.Add(TenantUser.Create(tenant.Id, UserId, isOwner: true, accepted: true));
        await TenantValidator.ProvisionTenantRolesAsync(Db, tenant.Id, UserId, cancellationToken);
        await Db.SaveChangesAsync(cancellationToken);
        TenantHttpItems.InvalidateLookupCache(cache);
        await TenantValidator.RunProvisioningHandlersAsync(HttpContext.RequestServices, tenant.Id, tenant.Name, cancellationToken);
        return new TenantDto(tenant.Id, tenant.Name, tenant.Title, tenant.Domain, tenant.IsActive, true, false);
    }

    [HttpPut, RequireElevatedAccess]
    public async Task<TenantDto> Update(TenantDto request, CancellationToken cancellationToken)
    {
        var tenant = await Db.Tenants.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("租户不存在。");

        var wasActive = tenant.IsActive;
        var (name, title, domain) = await TenantValidator.ValidateAndNormalizeAsync(
            Db, tenant.Id, request.Name, request.Title, request.Domain, HttpContext.Request.Host.Host, cancellationToken);

        tenant.Update(name, title, domain, request.IsActive);

        List<Guid> revokedSessionIds = [];
        if (wasActive && !tenant.IsActive)
        {
            var sessions = await Db.UserSessions
                .Where(s => s.TenantId == tenant.Id && s.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var session in sessions)
            {
                session.Revoke();
                revokedSessionIds.Add(session.Id);
            }
        }

        await Db.SaveChangesAsync(cancellationToken);
        if (revokedSessionIds.Count > 0)
        {
            var hub = HttpContext.RequestServices.GetRequiredService<IAppHubNotifier>();
            await TenantValidator.NotifySessionsRevokedAsync(hub, revokedSessionIds, tenant.Id, "tenant_deactivated", cancellationToken);
        }
        TenantHttpItems.InvalidateLookupCache(cache);
        return new TenantDto(tenant.Id, tenant.Name, tenant.Title, tenant.Domain, tenant.IsActive, true, tenant.Id == TenantId);
    }
}

/// <summary>用户偏好（Bluebird 主要放设备端，这里额外做服务端跨设备同步）。</summary>
[ApiController, Authorize]
[Route("api/v1/[controller]/[action]")]
public sealed class UserPreferenceController(
    IdentityDbContext db,
    JwtTokenService tokens,
    CurrentUserFactory factory,
    SystemSettingsStore settings)
    : AppControllerBase(db, tokens, factory), IUserPreferenceController
{
    [HttpGet]
    public async Task<UserPreferenceDto> Get(CancellationToken cancellationToken)
    {
        var row = await Db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == UserId, cancellationToken);
        if (row is not null)
            return new UserPreferenceDto(row.Culture, row.TimeZone, row.Theme, row.AccentColor, row.NotificationsEnabled);

        var raw = settings.GetRaw();
        return new UserPreferenceDto(
            Culture: string.IsNullOrWhiteSpace(raw.DefaultCulture) ? "zh-CN" : raw.DefaultCulture,
            TimeZone: string.IsNullOrWhiteSpace(raw.DefaultTimeZone) ? "Asia/Shanghai" : raw.DefaultTimeZone);
    }

    [HttpPost]
    public async Task<UserPreferenceDto> Save(UserPreferenceDto request, CancellationToken cancellationToken)
    {
        var row = await Db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == UserId, cancellationToken);
        if (row is null)
        {
            row = UserPreference.Create(UserId, request.Culture, request.TimeZone, request.Theme,
                request.AccentColor, request.NotificationsEnabled);
            Db.UserPreferences.Add(row);
        }
        else
        {
            row.Update(request.Culture, request.TimeZone, request.Theme, request.AccentColor, request.NotificationsEnabled);
        }
        await Db.SaveChangesAsync(cancellationToken);
        return request;
    }
}

/// <summary>站内通知与邮件通知（通知开关 + SMTP 邮件通知）。</summary>
[ApiController, Authorize]
[Route("api/v1/[controller]/[action]")]
public sealed class NotificationController(
    IdentityDbContext db,
    JwtTokenService tokens,
    CurrentUserFactory factory,
    IEmailSender emailSender,
    ISmtpSettingsProvider smtpSettingsProvider,
    SystemSettingsStore settings,
    ILogger<NotificationController> logger)
    : AppControllerBase(db, tokens, factory), INotificationController
{
    [HttpGet]
    public async Task<IReadOnlyList<NotificationDto>> GetNotifications(CancellationToken cancellationToken) =>
        await Db.Notifications.AsNoTracking().Where(n => n.UserId == UserId)
            .OrderByDescending(n => n.Id).Take(100)
            .Select(n => new NotificationDto(n.Id, n.Title, n.Message, n.IsRead, n.CreatedAtUtc, n.PageUrl))
            .ToArrayAsync(cancellationToken);

    [HttpGet]
    public Task<int> GetUnreadCount(CancellationToken cancellationToken) =>
        Db.Notifications.AsNoTracking().CountAsync(n => n.UserId == UserId && !n.IsRead, cancellationToken);

    [HttpPost("{id}")]
    public async Task MarkRead(Guid id, CancellationToken cancellationToken)
    {
        var row = await Db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == UserId, cancellationToken);
        if (row is null) return;
        row.MarkRead();
        await Db.SaveChangesAsync(cancellationToken);
    }

    [HttpPost]
    public async Task MarkAllRead(CancellationToken cancellationToken)
    {
        var rows = await Db.Notifications.Where(n => n.UserId == UserId && !n.IsRead).ToListAsync(cancellationToken);
        foreach (var row in rows) row.MarkRead();
        await Db.SaveChangesAsync(cancellationToken);
    }

    [HttpPost]
    public async Task<NotificationDto> SendTestNotification(CancellationToken cancellationToken)
    {
        var enabled = await Db.UserPreferences.AsNoTracking()
            .Where(p => p.UserId == UserId).Select(p => p.NotificationsEnabled).FirstOrDefaultAsync(cancellationToken);
        if (!enabled) throw new InvalidOperationException("请先开启通知。");
        var siteName = settings.GetRaw().SystemName;
        var item = Notification.Create(UserId, "测试通知", $"这是一条来自 {siteName} 的测试通知。", "/notifications");
        Db.Notifications.Add(item);
        await Db.SaveChangesAsync(cancellationToken);

        if (smtpSettingsProvider.GetSettings().IsConfigured)
        {
            var user = await Db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == UserId, cancellationToken);
            if (user is not null && !string.IsNullOrWhiteSpace(user.Email))
            {
                try
                {
                    var body = EmailTemplateBuilder.BuildNotificationEmail(
                        title: item.Title,
                        message: item.Message,
                        recipientName: user.DisplayName,
                        appName: siteName);
                    await emailSender.SendAsync(user.Email, $"[{siteName}] {item.Title}", body, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "测试通知邮件发送失败（站内通知已保存）：To={Email}", user.Email);
                }
            }
        }

        return new NotificationDto(item.Id, item.Title, item.Message, item.IsRead, item.CreatedAtUtc, item.PageUrl);
    }

    [HttpPost]
    public async Task<SendEmailNotificationResultDto> SendTestEmail(
        SendEmailNotificationRequestDto request, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(cancellationToken);
        var toEmail = string.IsNullOrWhiteSpace(request.ToEmail) ? user.Email : request.ToEmail.Trim();
        if (string.IsNullOrWhiteSpace(toEmail) || !toEmail.Contains('@'))
            throw new InvalidOperationException("请提供有效的收件人邮箱地址。");

        var siteName = settings.GetRaw().SystemName;
        var subject = string.IsNullOrWhiteSpace(request.Subject)
            ? $"{siteName} 邮件通知测试"
            : request.Subject.Trim();
        var message = string.IsNullOrWhiteSpace(request.Message)
            ? $"这是一条来自「{siteName}」的测试邮件通知，表明您的 SMTP 邮件服务已配置成功。"
            : request.Message.Trim();

        var html = EmailTemplateBuilder.BuildNotificationEmail(
            title: subject,
            message: message,
            recipientName: user.DisplayName,
            appName: siteName);

        await emailSender.SendAsync(toEmail, subject, html, cancellationToken);

        var item = Notification.Create(UserId, $"邮件已发送：{subject}", $"收件人：{toEmail}\n{message}", "/notifications");
        Db.Notifications.Add(item);
        await Db.SaveChangesAsync(cancellationToken);

        return new SendEmailNotificationResultDto(toEmail, subject, DateTimeOffset.UtcNow);
    }
}

/// <summary>推送订阅（匿名可订阅；当前只持久化订阅记录，不接外部推送网关）。</summary>
[ApiController, AllowAnonymous]
[Route("api/v1/[controller]/[action]")]
public sealed class PushNotificationController(IdentityDbContext db, JwtTokenService tokens, CurrentUserFactory factory)
    : AppControllerBase(db, tokens, factory), IPushNotificationController
{
    [HttpPost]
    public async Task Subscribe(PushNotificationSubscriptionDto subscription, CancellationToken cancellationToken)
    {
        var row = await Db.PushNotificationSubscriptions.FirstOrDefaultAsync(
            s => s.DeviceId == subscription.DeviceId, cancellationToken);
        if (row is null)
        {
            Db.PushNotificationSubscriptions.Add(PushNotificationSubscription.Create(
                subscription.DeviceId, subscription.Platform, subscription.PushChannel,
                subscription.Endpoint, subscription.P256dh, subscription.Auth, UserId == Guid.Empty ? null : UserId));
        }
        else
        {
            row.Update(subscription.Platform, subscription.PushChannel, subscription.Endpoint,
                subscription.P256dh, subscription.Auth, UserId == Guid.Empty ? null : UserId);
        }
        await Db.SaveChangesAsync(cancellationToken);
    }

    [HttpPost]
    public async Task Unsubscribe(PushNotificationSubscriptionDto subscription, CancellationToken cancellationToken)
    {
        var row = await Db.PushNotificationSubscriptions.FirstOrDefaultAsync(
            s => s.DeviceId == subscription.DeviceId, cancellationToken);
        if (row is null) return;
        Db.PushNotificationSubscriptions.Remove(row);
        await Db.SaveChangesAsync(cancellationToken);
    }
}
