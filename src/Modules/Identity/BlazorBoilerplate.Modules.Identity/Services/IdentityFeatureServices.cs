using BlazorBoilerplate.Modules.Identity.Domain;
using BlazorBoilerplate.Modules.Identity.Persistence;
using BlazorBoilerplate.Shared.Features.Account;
using BlazorBoilerplate.Shared.Features.Identity;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Modules.Identity.Services;

/// <summary>
/// 权限目录。Code 唯一性在代码层保证（数据库只建普通索引）。
/// 菜单级权限由 EnsureSeedAsync 从 Menus 表幂等补充，按钮级权限由增量迁移 SQL 种子。
/// </summary>
public sealed class PermissionService(IdentityDbContext db)
{
    /// <summary>全部权限定义，按 Code 排序。</summary>
    public Task<List<PermissionDefinition>> GetCatalogAsync(CancellationToken ct = default) =>
        db.PermissionDefinitions.AsNoTracking().OrderBy(p => p.Code).ToListAsync(ct);

    /// <summary>
    /// 幂等补充菜单级权限定义：把 Menus 表中带 PermissionCode 的项写入 PermissionDefinitions，
    /// Kind = "menu"，Name 取菜单名。已存在的 Code 不重复插入。
    /// </summary>
    public async Task EnsureSeedAsync(CancellationToken ct = default)
    {
        var menus = await db.Menus.AsNoTracking()
            .Where(m => m.PermissionCode != null && m.PermissionCode != "")
            .Select(m => new { m.PermissionCode, m.Name, m.ModuleId, m.Path })
            .ToListAsync(ct);
        if (menus.Count == 0) return;

        var known = new HashSet<string>(
            await db.PermissionDefinitions.AsNoTracking().Select(p => p.Code).ToListAsync(ct),
            StringComparer.Ordinal);

        foreach (var menu in menus)
        {
            var code = menu.PermissionCode!;
            if (!known.Add(code)) continue;
            db.PermissionDefinitions.Add(PermissionDefinition.Create(
                code, menu.Name, menu.ModuleId, menu.Path, kind: "menu"));
        }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>租户与成员管理（无外键：成员关系、邮箱匹配、接受邀请均在代码层校验）。</summary>
public sealed class TenantService(IdentityDbContext db)
{
    public async Task<List<Tenant>> ListForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var tenantIds = await db.TenantUsers.AsNoTracking()
            .Where(tu => tu.UserId == userId).Select(tu => tu.TenantId).ToListAsync(ct);
        if (tenantIds.Count == 0) return [];
        return await db.Tenants.AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id)).OrderBy(t => t.Id).ToListAsync(ct);
    }

    public async Task<Tenant?> GetCurrentAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        var isAcceptedMember = await db.TenantUsers.AsNoTracking()
            .AnyAsync(tu => tu.UserId == userId && tu.TenantId == tenantId && tu.Accepted, ct);
        if (!isAcceptedMember) return null;
        return await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
    }

    public async Task<Tenant> CreateAsync(string name, string? title, string? domain, Guid ownerUserId, CancellationToken ct = default)
    {
        _ = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == ownerUserId, ct)
            ?? throw new KeyNotFoundException($"用户不存在：{ownerUserId}");

        var tenant = Tenant.Create(name, title, domain);
        db.Tenants.Add(tenant);
        // 创建者即为已接受的所有者。
        db.TenantUsers.Add(TenantUser.Create(tenant.Id, ownerUserId, isOwner: true, accepted: true));
        await db.SaveChangesAsync(ct);
        return tenant;
    }

    public async Task<Tenant> UpdateAsync(Guid id, string name, string? title, string? domain, bool isActive, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new KeyNotFoundException($"租户不存在：{id}");
        tenant.Update(name, title, domain, isActive);
        await db.SaveChangesAsync(ct);
        return tenant;
    }

    /// <summary>按邮箱邀请：匹配到用户才加入 TenantUser（Accepted=false）；已是成员则幂等返回。</summary>
    public async Task InviteUserAsync(Guid tenantId, string email, CancellationToken ct = default)
    {
        _ = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new KeyNotFoundException($"租户不存在：{tenantId}");

        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalized, ct)
            ?? throw new InvalidOperationException($"未找到邮箱对应的用户：{email}");

        if (await db.TenantUsers.AnyAsync(tu => tu.TenantId == tenantId && tu.UserId == user.Id, ct))
            return;

        db.TenantUsers.Add(TenantUser.Create(tenantId, user.Id, isOwner: false, accepted: false));
        await db.SaveChangesAsync(ct);
    }

    public async Task AcceptAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var link = await db.TenantUsers
            .FirstOrDefaultAsync(tu => tu.TenantId == tenantId && tu.UserId == userId, ct)
            ?? throw new KeyNotFoundException("邀请记录不存在。");
        link.Accept();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>退出租户：仅删除 TenantUser 关联，不删除租户本身。</summary>
    public async Task LeaveAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var links = await db.TenantUsers
            .Where(tu => tu.TenantId == tenantId && tu.UserId == userId).ToListAsync(ct);
        if (links.Count == 0) return;
        db.TenantUsers.RemoveRange(links);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>用户会话 / 刷新令牌 / 通知开关。</summary>
public sealed class UserSessionService(IdentityDbContext db)
{
    public async Task<UserSession> CreateAsync(Guid userId, Guid? tenantId, string? ip, string? deviceInfo,
        string? cultureName, string? timeZone, bool notificationsEnabled, string? refreshToken,
        CancellationToken ct = default)
    {
        var session = UserSession.Create(userId, tenantId, ip, deviceInfo, cultureName, timeZone,
            notificationsEnabled, refreshToken);
        db.UserSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>未撤销的会话列表，currentSessionId 用于标记 UserSessionDto.Current。</summary>
    public async Task<List<UserSessionDto>> ListAsync(Guid userId, Guid? currentSessionId, CancellationToken ct = default)
    {
        var sessions = await db.UserSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null)
            .OrderBy(s => s.Id).ToListAsync(ct);
        return [.. sessions.Select(s => new UserSessionDto(
            s.Id, s.IP, s.DeviceInfo, s.CultureName, s.TimeZone, s.NotificationsEnabled,
            s.StartedOnUtc, currentSessionId.HasValue && s.Id == currentSessionId.Value))];
    }

    /// <summary>撤销会话（软删除：置 RevokedAtUtc 并清空 RefreshToken）。</summary>
    public async Task RevokeAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new KeyNotFoundException($"会话不存在：{sessionId}");
        session.Revoke();
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Guid sessionId, string? culture, string? timezone,
        bool? notifications, string? device, CancellationToken ct = default)
    {
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new KeyNotFoundException($"会话不存在：{sessionId}");
        session.Update(culture, timezone, notifications, device);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>通知开关作用于该用户全部会话，并同步已存在的偏好记录。</summary>
    public async Task SetNotificationsEnabledAsync(Guid userId, bool enabled, CancellationToken ct = default)
    {
        var sessions = await db.UserSessions.Where(s => s.UserId == userId).ToListAsync(ct);
        foreach (var session in sessions) session.SetNotificationsEnabled(enabled);

        var preference = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (preference is not null)
            preference.Update(preference.Culture, preference.TimeZone, preference.Theme,
                preference.AccentColor, enabled);

        await db.SaveChangesAsync(ct);
    }

    public Task<UserSession?> GetByRefreshTokenAsync(string refreshToken, CancellationToken ct = default) =>
        db.UserSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.RefreshToken == refreshToken && s.RevokedAtUtc == null, ct);
}

/// <summary>用户偏好。GetAsync 在记录不存在时新建默认值（与 UserPreferenceDto 默认值一致）。</summary>
public sealed class UserPreferenceService(IdentityDbContext db)
{
    public async Task<UserPreference> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var preference = await db.UserPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (preference is not null) return preference;

        var created = UserPreference.Create(userId);
        db.UserPreferences.Add(created);
        await db.SaveChangesAsync(ct);
        return created;
    }

    public async Task<UserPreference> SaveAsync(Guid userId, UserPreferenceDto dto, CancellationToken ct = default)
    {
        var preference = await db.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (preference is null)
        {
            preference = UserPreference.Create(userId, dto.Culture, dto.TimeZone, dto.Theme,
                dto.AccentColor, dto.NotificationsEnabled);
            db.UserPreferences.Add(preference);
        }
        else
        {
            preference.Update(dto.Culture, dto.TimeZone, dto.Theme, dto.AccentColor, dto.NotificationsEnabled);
        }
        await db.SaveChangesAsync(ct);
        return preference;
    }
}

/// <summary>站内通知。所有读写都按 UserId 限定，避免跨用户操作。</summary>
public sealed class NotificationService(IdentityDbContext db)
{
    public Task<List<Notification>> ListAsync(Guid userId, CancellationToken ct = default) =>
        db.Notifications.AsNoTracking().Where(n => n.UserId == userId)
            .OrderBy(n => n.Id).ToListAsync(ct);

    public Task<int> UnreadCountAsync(Guid userId, CancellationToken ct = default) =>
        db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);

    public async Task MarkReadAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct)
            ?? throw new KeyNotFoundException($"通知不存在：{id}");
        notification.MarkRead();
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(Guid userId, CancellationToken ct = default)
    {
        var list = await db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync(ct);
        foreach (var notification in list) notification.MarkRead();
        await db.SaveChangesAsync(ct);
    }

    public async Task<Notification> CreateAsync(Guid userId, string title, string message,
        string? pageUrl, CancellationToken ct = default)
    {
        var notification = Notification.Create(userId, title, message, pageUrl);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);
        return notification;
    }
}

/// <summary>推送订阅（按 DeviceId 幂等订阅 / 取消订阅；不接外部推送网关）。</summary>
public sealed class PushSubscriptionService(IdentityDbContext db)
{
    public async Task SubscribeAsync(PushNotificationSubscriptionDto dto, Guid? userId = null, CancellationToken ct = default)
    {
        var deviceId = dto.DeviceId.Trim();
        var existing = await db.PushNotificationSubscriptions
            .FirstOrDefaultAsync(s => s.DeviceId == deviceId, ct);
        if (existing is null)
            db.PushNotificationSubscriptions.Add(PushNotificationSubscription.Create(
                deviceId, dto.Platform, dto.PushChannel, dto.Endpoint, dto.P256dh, dto.Auth, userId));
        else
            existing.Update(dto.Platform, dto.PushChannel, dto.Endpoint, dto.P256dh, dto.Auth, userId);
        await db.SaveChangesAsync(ct);
    }

    public async Task UnsubscribeAsync(PushNotificationSubscriptionDto dto, CancellationToken ct = default)
    {
        var deviceId = dto.DeviceId.Trim();
        var list = await db.PushNotificationSubscriptions
            .Where(s => s.DeviceId == deviceId).ToListAsync(ct);
        if (list.Count == 0) return;
        db.PushNotificationSubscriptions.RemoveRange(list);
        await db.SaveChangesAsync(ct);
    }
}
