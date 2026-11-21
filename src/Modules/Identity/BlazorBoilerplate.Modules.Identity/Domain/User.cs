using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Modules.Identity.Domain;

/// <summary>
/// 平台用户。唯一性（UserName/Email）由 UserService 在代码层校验，
/// 数据库只建非唯一索引 —— 与 Nova 一致：不依赖数据库级约束。
/// </summary>
public sealed class User : AuditableEntity<Guid>
{
    private User() { }

    private User(string userName, string email, string displayName, string passwordHash, string? phoneNumber = null)
    {
        Id = Guid.CreateVersion7();
        UserName = userName;
        Email = email;
        PhoneNumber = phoneNumber;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        IsActive = true;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string UserName { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string? PhoneNumber { get; private set; }
    public string DisplayName { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public bool IsActive { get; private set; }

    public ICollection<UserRole> UserRoles { get; private set; } = new List<UserRole>();
    public ICollection<TenantUser> TenantUsers { get; private set; } = new List<TenantUser>();
    public ICollection<UserSession> Sessions { get; private set; } = new List<UserSession>();
    public ICollection<Notification> Notifications { get; private set; } = new List<Notification>();

    public static User Create(string userName, string email, string displayName, string passwordHash, string? phoneNumber = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        var normalizedEmail = email?.Trim() ?? string.Empty;
        var normalizedPhone = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        if (string.IsNullOrWhiteSpace(normalizedEmail) && string.IsNullOrWhiteSpace(normalizedPhone))
            throw new ArgumentException("邮箱和手机号不能同时为空。", nameof(email));
        return new User(userName.Trim(), normalizedEmail, string.IsNullOrWhiteSpace(displayName) ? userName : displayName, passwordHash, normalizedPhone);
    }

    public void SetPhoneNumber(string? phoneNumber)
    {
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Update(string email, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        Email = email.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? UserName : displayName;
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }

    public void SetPassword(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Activate() { IsActive = true; ModifiedAtUtc = DateTimeOffset.UtcNow; }
    public void Deactivate() { IsActive = false; ModifiedAtUtc = DateTimeOffset.UtcNow; }
}

