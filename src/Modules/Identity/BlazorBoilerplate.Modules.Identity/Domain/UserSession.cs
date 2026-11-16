using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Modules.Identity.Domain;

/// <summary>用户会话（登录会话 / 刷新令牌载体）。撤销为软删除：只置 RevokedAtUtc。</summary>
public sealed class UserSession : AuditableEntity<Guid>
{
    private UserSession() { }

    private UserSession(Guid userId, Guid? tenantId, string? ip, string? deviceInfo,
        string? cultureName, string? timeZone, bool notificationsEnabled, string? refreshToken)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        TenantId = tenantId;
        IP = ip;
        DeviceInfo = deviceInfo;
        CultureName = cultureName;
        TimeZone = timeZone;
        NotificationsEnabled = notificationsEnabled;
        RefreshToken = refreshToken;
        StartedOnUtc = DateTimeOffset.UtcNow;
        RenewedOnUtc = StartedOnUtc;
    }

    public Guid UserId { get; private set; }
    public Guid? TenantId { get; private set; }
    public string? IP { get; private set; }
    public string? DeviceInfo { get; private set; }
    public string? CultureName { get; private set; }
    public string? TimeZone { get; private set; }
    public bool NotificationsEnabled { get; private set; }
    public string? RefreshToken { get; private set; }
    public DateTimeOffset StartedOnUtc { get; private set; }
    public DateTimeOffset RenewedOnUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public User? User { get; private set; }
    public Tenant? Tenant { get; private set; }

    public static UserSession Create(Guid userId, Guid? tenantId = null, string? ip = null, string? deviceInfo = null,
        string? cultureName = null, string? timeZone = null, bool notificationsEnabled = false, string? refreshToken = null) =>
        new(userId, tenantId, ip, deviceInfo, cultureName, timeZone, notificationsEnabled, refreshToken);

    /// <summary>局部更新：null 表示「不修改」该字段，并刷新 RenewedOnUtc。</summary>
    public void Update(string? cultureName, string? timeZone, bool? notificationsEnabled, string? deviceInfo)
    {
        if (cultureName is not null) CultureName = cultureName;
        if (timeZone is not null) TimeZone = timeZone;
        if (notificationsEnabled is { } enabled) NotificationsEnabled = enabled;
        if (deviceInfo is not null) DeviceInfo = deviceInfo;
        RenewedOnUtc = DateTimeOffset.UtcNow;
    }

    public void SetNotificationsEnabled(bool enabled)
    {
        NotificationsEnabled = enabled;
        RenewedOnUtc = DateTimeOffset.UtcNow;
    }

    public void Renew(string? refreshToken = null)
    {
        if (refreshToken is not null) RefreshToken = refreshToken;
        RenewedOnUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>切换租户：只改会话的租户归属，不动任何数据库结构。</summary>
    public void SwitchTenant(Guid? tenantId)
    {
        TenantId = tenantId;
        RenewedOnUtc = DateTimeOffset.UtcNow;
    }

    public void Revoke()
    {
        RevokedAtUtc = DateTimeOffset.UtcNow;
        RefreshToken = null;
    }
}

