using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Persistence.Platform;

/// <summary>平台与模块强类型配置持久化实体。</summary>
public sealed class PlatformSetting : AuditableEntity<Guid>
{
    public PlatformSetting()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid TenantId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

