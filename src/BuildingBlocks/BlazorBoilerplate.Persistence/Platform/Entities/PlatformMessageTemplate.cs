using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Persistence.Platform;

/// <summary>消息模板持久化实体。</summary>
public sealed class PlatformMessageTemplate : AuditableEntity<Guid>
{
    public PlatformMessageTemplate()
    {
        Id = Guid.CreateVersion7();
    }

    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
