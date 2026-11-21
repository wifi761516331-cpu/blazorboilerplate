using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Modules.Identity.Domain;

/// <summary>用户-角色关联（数据库无外键，完整性由服务层保证）。</summary>
public sealed class UserRole : AuditableEntity<Guid>
{
    private UserRole() { }

    public UserRole(Guid userId, Guid roleId, Guid? tenantId = null)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        RoleId = roleId;
        TenantId = tenantId;
        AssignedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid? TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public DateTimeOffset AssignedAtUtc { get; private set; }

    public User? User { get; private set; }
    public Role? Role { get; private set; }
}

