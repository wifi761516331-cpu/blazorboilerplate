using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Modules.Identity.Domain;

/// <summary>角色。IsSystem 的内置角色不允许删除/改名，由 RoleService 保护。</summary>
public sealed class Role : AuditableEntity<Guid>
{
    private Role() { }

    private Role(string name, string? description, bool isSystem)
    {
        Id = Guid.CreateVersion7();
        Name = name;
        Description = description;
        IsSystem = isSystem;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public bool IsSystem { get; private set; }

    public ICollection<UserRole> UserRoles { get; private set; } = new List<UserRole>();
    public ICollection<RoleMenu> RoleMenus { get; private set; } = new List<RoleMenu>();
    public ICollection<RolePermission> RolePermissions { get; private set; } = new List<RolePermission>();

    public static Role Create(string name, string? description = null, bool isSystem = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Role(name.Trim(), description?.Trim(), isSystem);
    }

    public void Update(string name, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Description = description?.Trim();
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }
}

