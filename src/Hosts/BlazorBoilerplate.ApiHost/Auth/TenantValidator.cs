using BlazorBoilerplate.ApiHost.Hubs;
using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Infrastructure.Email;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Identity.Domain;
using BlazorBoilerplate.Modules.Identity.Persistence;
using BlazorBoilerplate.Modules.Identity.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.ApiHost.Auth;

/// <summary>
/// 租户名称/域名校验与租户级角色初始化。
/// </summary>
public static class TenantValidator
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "api", "admin", "administrator", "app", "auth", "login", "signin", "signup",
        "register", "setup", "health", "mail", "smtp", "ftp", "cdn", "static", "assets",
        "localhost", "system", "platform", "host", "root", "default", "support", "help"
    };

    public static (string Name, string? Title, string? Domain) Normalize(string? rawName, string? rawTitle, string? rawDomain)
    {
        var name = (rawName ?? string.Empty).Trim();
        var title = string.IsNullOrWhiteSpace(rawTitle) ? null : rawTitle.Trim();
        var domain = TenantHttpItems.NormalizeHost(rawDomain);
        return (name, title, domain);
    }

    public static async Task<(string Name, string? Title, string? Domain)> ValidateAndNormalizeAsync(
        IdentityDbContext db,
        Guid? existingTenantId,
        string? rawName,
        string? rawTitle,
        string? rawDomain,
        string? requestHost,
        CancellationToken ct)
    {
        var (name, title, domain) = Normalize(rawName, rawTitle, rawDomain);

        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("租户名称不能为空。");

        if (ReservedNames.Contains(name))
            throw new InvalidOperationException($"租户名称「{name}」为系统保留名称，不可使用。");

        var nameLower = name.ToLowerInvariant();
        var nameExists = await db.Tenants.AsNoTracking()
            .AnyAsync(t => t.Id != existingTenantId && t.Name.ToLower() == nameLower, ct);
        if (nameExists)
            throw new InvalidOperationException("租户名已存在。");

        if (!string.IsNullOrWhiteSpace(domain))
        {
            var normalizedRequestHost = TenantHttpItems.NormalizeHost(requestHost);
            if (!string.IsNullOrWhiteSpace(normalizedRequestHost) &&
                string.Equals(domain, normalizedRequestHost, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"域名「{domain}」为系统保留域名，不可绑定为租户域名。");
            }

            if (ReservedNames.Contains(domain))
                throw new InvalidOperationException($"域名「{domain}」为系统保留域名，不可使用。");

            var domainExists = await db.Tenants.AsNoTracking()
                .AnyAsync(t => t.Id != existingTenantId && t.Domain != null && t.Domain.ToLower() == domain, ct);
            if (domainExists)
                throw new InvalidOperationException($"域名「{domain}」已被其他租户使用。");
        }

        return (name, title, domain);
    }

    /// <summary>为新创建的租户初始化租户级默认角色（租户管理员 / 租户成员），并授予创建者租户管理员角色。</summary>
    public static async Task ProvisionTenantRolesAsync(
        IdentityDbContext db,
        Guid tenantId,
        Guid ownerUserId,
        CancellationToken ct)
    {
        var adminRole = Role.Create("租户管理员", "租户所有者与管理员角色", isSystem: true, tenantId: tenantId);
        var memberRole = Role.Create("租户成员", "租户普通成员角色", isSystem: true, tenantId: tenantId);
        db.Roles.AddRange(adminRole, memberRole);

        string[] adminPerms = ["tenant.create", "tenant.update", "tenant.invite"];
        foreach (var code in adminPerms)
            db.RolePermissions.Add(new RolePermission(adminRole.Id, code));

        db.RolePermissions.Add(new RolePermission(memberRole.Id, "tenant.create"));
        db.UserRoles.Add(new UserRole(ownerUserId, adminRole.Id, tenantId));

        await Task.CompletedTask;
    }

    /// <summary>为受邀加入租户的用户分配该租户的「租户成员」角色（幂等）。</summary>
    public static async Task EnsureTenantMemberRoleAsync(
        IdentityDbContext db,
        Guid tenantId,
        Guid userId,
        CancellationToken ct)
    {
        var memberRole = await db.Roles
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Name == "租户成员", ct);
        if (memberRole is null)
        {
            memberRole = Role.Create("租户成员", "租户普通成员角色", isSystem: true, tenantId: tenantId);
            db.Roles.Add(memberRole);
            db.RolePermissions.Add(new RolePermission(memberRole.Id, "tenant.create"));
        }

        var hasAnyRoleInTenant = await db.UserRoles.AnyAsync(
            ur => ur.TenantId == tenantId && ur.UserId == userId, ct);
        if (!hasAnyRoleInTenant)
            db.UserRoles.Add(new UserRole(userId, memberRole.Id, tenantId));
    }

    public static string? NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var chars = raw.Trim().Where(c => char.IsDigit(c) || c == '+').ToArray();
        return chars.Length < 5 ? null : new string(chars);
    }

    public static async Task RunProvisioningHandlersAsync(
        IServiceProvider services,
        Guid tenantId,
        string tenantName,
        CancellationToken ct)
    {
        foreach (var handler in services.GetServices<ITenantProvisioningHandler>())
            await handler.OnTenantCreatedAsync(tenantId, tenantName, ct);
    }

    /// <summary>支持邮箱或手机号邀请；若用户未注册则自动建号，并发送邮件/短信通知。</summary>
    public static async Task InviteUserAsync(
        IdentityDbContext db,
        IEmailSender emailSender,
        ISmsSender smsSender,
        ISmtpSettingsProvider smtpSettingsProvider,
        ILogger logger,
        Guid tenantId,
        Guid inviterUserId,
        string? rawEmail,
        string? rawPhone,
        CancellationToken ct)
    {
        var email = string.IsNullOrWhiteSpace(rawEmail) ? null : rawEmail.Trim().ToLowerInvariant();
        var phone = NormalizePhone(rawPhone);
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone))
            throw new InvalidOperationException("请填写受邀人的邮箱或手机号。");

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new KeyNotFoundException("租户不存在。");

        var user = await db.Users.FirstOrDefaultAsync(
            u => (email != null && u.Email.ToLower() == email) ||
                 (phone != null && u.PhoneNumber == phone),
            ct);

        if (user is null)
        {
            var baseName = email is not null
                ? email.Split('@')[0]
                : $"u_{phone!.TrimStart('+')}";
            var userName = baseName;
            while (await db.Users.AnyAsync(u => u.UserName.ToLower() == userName.ToLower(), ct))
                userName = $"{baseName}_{Guid.NewGuid().ToString("N")[..4]}";

            var randomPassword = PasswordHasher.Hash(Guid.NewGuid().ToString("N") + "Aa1!");
            user = User.Create(userName, email ?? string.Empty, baseName, randomPassword, phone);
            db.Users.Add(user);
        }
        else if (phone is not null && string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            user.SetPhoneNumber(phone);
        }

        var existingLink = await db.TenantUsers.FirstOrDefaultAsync(
            t => t.TenantId == tenantId && t.UserId == user.Id, ct);
        if (existingLink is not null && existingLink.Accepted)
            throw new InvalidOperationException("该用户已是当前租户的成员。");
        if (existingLink is null)
            db.TenantUsers.Add(TenantUser.Create(tenantId, user.Id));

        await EnsureTenantMemberRoleAsync(db, tenantId, user.Id, ct);
        await db.SaveChangesAsync(ct);

        var inviter = await db.Users.AsNoTracking()
            .Where(u => u.Id == inviterUserId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct);
        var inviterName = string.IsNullOrWhiteSpace(inviter) ? "管理员" : inviter;
        var tenantDisplay = tenant.Title ?? tenant.Name;

        var notifyTasks = new List<Task>();
        if (smtpSettingsProvider.GetSettings().IsConfigured && !string.IsNullOrWhiteSpace(user.Email))
        {
            notifyTasks.Add(Task.Run(async () =>
            {
                try
                {
                    var title = $"租户邀请：{tenantDisplay}";
                    var body = EmailTemplateBuilder.BuildNotificationEmail(
                        title: title,
                        message: $"{inviterName} 邀请您加入租户「{tenantDisplay}」，请登录平台查看并接受邀请。",
                        recipientName: user.DisplayName);
                    await emailSender.SendAsync(user.Email, title, body, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "租户邀请邮件发送失败：TenantId={TenantId}, Email={Email}", tenantId, user.Email);
                }
            }, ct));
        }

        if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            notifyTasks.Add(Task.Run(async () =>
            {
                try
                {
                    var smsText = $"{inviterName} 邀请您加入租户「{tenantDisplay}」，请登录平台查看并接受邀请。";
                    await smsSender.SendAsync(user.PhoneNumber, smsText, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "租户邀请短信发送失败：TenantId={TenantId}, Phone={Phone}", tenantId, user.PhoneNumber);
                }
            }, ct));
        }

        if (notifyTasks.Count > 0)
            await Task.WhenAll(notifyTasks);
    }

    /// <summary>吊销用户在指定租户下的所有活跃会话并收集会话 ID 以便 SignalR 实时踢下线。</summary>
    public static async Task<List<Guid>> RevokeTenantUserSessionsAsync(
        IdentityDbContext db,
        Guid tenantId,
        Guid userId,
        Guid? extraSessionId,
        CancellationToken ct)
    {
        var sessions = await db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null &&
                        (s.TenantId == tenantId || (extraSessionId != null && s.Id == extraSessionId)))
            .ToListAsync(ct);

        List<Guid> ids = [];
        foreach (var s in sessions)
        {
            s.Revoke();
            ids.Add(s.Id);
        }
        return ids;
    }

    public static Task NotifySessionsRevokedAsync(
        IHubContext<AppHub> hub,
        IReadOnlyCollection<Guid> sessionIds,
        Guid tenantId,
        string reason,
        CancellationToken ct) =>
        sessionIds.Count == 0
            ? Task.CompletedTask
            : Task.WhenAll(sessionIds.Select(sid =>
                hub.Clients.Group(AppHub.SessionGroup(sid))
                    .SendAsync(AppHub.SessionRevokedMethod, new { tenantId, reason }, ct)));
}
