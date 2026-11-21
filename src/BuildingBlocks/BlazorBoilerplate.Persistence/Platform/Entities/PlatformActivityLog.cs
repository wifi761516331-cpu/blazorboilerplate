using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Persistence.Platform;

/// <summary>业务活动日志持久化实体。</summary>
public sealed class PlatformActivityLog : AuditableEntity<Guid>
{
    public PlatformActivityLog()
    {
        Id = Guid.CreateVersion7();
    }

    public string SystemKeyword { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? EntityName { get; set; }
    public string? EntityId { get; set; }
    public string? IpAddress { get; set; }
}
