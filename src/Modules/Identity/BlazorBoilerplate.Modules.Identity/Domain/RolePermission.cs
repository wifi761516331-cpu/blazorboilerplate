using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Modules.Identity.Domain;

/// <summary>角色-权限码关联（数据库无外键）。差量更新由 RoleService.AssignPermissionCodesAsync 保证。</summary>
public sealed class RolePermission : AuditableEntity<Guid>
{
    private RolePermission() { }

    public RolePermission(Guid roleId, string code)
    {
        Id = Guid.CreateVersion7();
        RoleId = roleId;
        Code = code;
    }

    public Guid RoleId { get; private set; }
    public string Code { get; private set; } = default!;

    public Role? Role { get; private set; }
}

