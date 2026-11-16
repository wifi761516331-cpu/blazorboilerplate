using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Modules.Identity.Domain;

/// <summary>角色-菜单关联（角色可访问的菜单授权）。</summary>
public sealed class RoleMenu : AuditableEntity<Guid>
{
    private RoleMenu() { }

    public RoleMenu(Guid roleId, Guid menuId)
    {
        Id = Guid.CreateVersion7();
        RoleId = roleId;
        MenuId = menuId;
    }

    public Guid RoleId { get; private set; }
    public Guid MenuId { get; private set; }

    public Role? Role { get; private set; }
    public Menu? Menu { get; private set; }
}

