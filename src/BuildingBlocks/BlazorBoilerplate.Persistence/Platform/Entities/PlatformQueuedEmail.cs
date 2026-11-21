using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Persistence.Platform;

/// <summary>异步邮件队列持久化实体。</summary>
public sealed class PlatformQueuedEmail : AuditableEntity<Guid>
{
    public PlatformQueuedEmail()
    {
        Id = Guid.CreateVersion7();
    }

    public int Priority { get; set; }
    public string? From { get; set; }
    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedOnUtc { get; set; }
    public int SentTries { get; set; }
    public DateTimeOffset? SentOnUtc { get; set; }
    public string? LastError { get; set; }
}
