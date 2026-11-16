using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Modules.Identity.Domain;

/// <summary>站内通知。已读为软状态，由 NotificationService 保证只能操作自己的通知。</summary>
public sealed class Notification : AuditableEntity<Guid>
{
    private Notification() { }

    private Notification(Guid userId, string title, string message, string? pageUrl)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        Title = title;
        Message = message;
        IsRead = false;
        PageUrl = pageUrl;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid UserId { get; private set; }
    public string Title { get; private set; } = default!;
    public string Message { get; private set; } = default!;
    public bool IsRead { get; private set; }
    public string? PageUrl { get; private set; }
    public DateTimeOffset? ReadAtUtc { get; private set; }

    public User? User { get; private set; }

    public static Notification Create(Guid userId, string title, string message, string? pageUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return new Notification(userId, title.Trim(), message, pageUrl?.Trim());
    }

    public void MarkRead()
    {
        if (IsRead) return;
        IsRead = true;
        ReadAtUtc = DateTimeOffset.UtcNow;
    }
}

