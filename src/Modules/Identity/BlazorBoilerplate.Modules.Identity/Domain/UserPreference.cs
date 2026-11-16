using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Modules.Identity.Domain;

/// <summary>用户偏好（跨设备同步）。默认值与共享 UserPreferenceDto 保持一致。</summary>
public sealed class UserPreference : AuditableEntity<Guid>
{
    private UserPreference() { }

    private UserPreference(Guid userId, string culture, string timeZone, string theme,
        string? accentColor, bool notificationsEnabled)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        Culture = culture;
        TimeZone = timeZone;
        Theme = theme;
        AccentColor = accentColor;
        NotificationsEnabled = notificationsEnabled;
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid UserId { get; private set; }
    public string Culture { get; private set; } = default!;
    public string TimeZone { get; private set; } = default!;
    public string Theme { get; private set; } = default!;
    public string? AccentColor { get; private set; }
    public bool NotificationsEnabled { get; private set; }

    public User? User { get; private set; }

    public static UserPreference Create(Guid userId, string culture = "zh-CN", string timeZone = "Asia/Shanghai",
        string theme = "light", string? accentColor = null, bool notificationsEnabled = false) =>
        new(userId, culture, timeZone, theme, accentColor, notificationsEnabled);

    public void Update(string culture, string timeZone, string theme, string? accentColor, bool notificationsEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZone);
        ArgumentException.ThrowIfNullOrWhiteSpace(theme);
        Culture = culture;
        TimeZone = timeZone;
        Theme = theme;
        AccentColor = accentColor;
        NotificationsEnabled = notificationsEnabled;
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }
}

