using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Modules.Identity.Domain;

/// <summary>
/// 菜单（自引用树：ParentId 为标量列，可通过导航属性连表查询，数据库级不生成外键）。
/// 树形结构由 MenuService 在代码层构建与校验（循环引用/父节点存在性）。
/// </summary>
public sealed class Menu : AuditableEntity<Guid>
{
    private Menu() { }

    private Menu(string name, string path, string? icon, Guid? parentId, int sort,
        string? moduleId, string? permissionCode, string kind)
    {
        Id = Guid.CreateVersion7();
        Name = name;
        Path = path;
        Icon = icon;
        ParentId = parentId;
        Sort = sort;
        IsVisible = true;
        ModuleId = moduleId;
        PermissionCode = permissionCode;
        Kind = kind;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = default!;
    public string Path { get; private set; } = default!;
    public string? Icon { get; private set; }
    public int Sort { get; private set; }
    public bool IsVisible { get; private set; }
    /// <summary>归属模块 Id（可空表示平台核心菜单）。</summary>
    public string? ModuleId { get; private set; }
    /// <summary>访问该菜单所需的权限码；为空表示不做按钮级校验。</summary>
    public string? PermissionCode { get; private set; }
    /// <summary>菜单类型："menu"（默认）/ "button"。</summary>
    public string Kind { get; private set; } = "menu";

    public Menu? Parent { get; private set; }
    public ICollection<Menu> Children { get; private set; } = new List<Menu>();
    public ICollection<RoleMenu> RoleMenus { get; private set; } = new List<RoleMenu>();

    // moduleId / permissionCode / kind 均为可选参数：旧调用点（安装向导种子、旧前端）
    // 不传时仍等价于「平台菜单」，保持源码兼容。
    public static Menu Create(string name, string path, string? icon = null, Guid? parentId = null, int sort = 0,
        string? moduleId = null, string? permissionCode = null, string kind = "menu")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new Menu(name.Trim(), path.Trim(), icon?.Trim(), parentId, sort,
            moduleId?.Trim(), permissionCode?.Trim(), NormalizeKind(kind));
    }

    public void Update(string name, string path, string? icon, Guid? parentId, int sort, bool isVisible,
        string? moduleId = null, string? permissionCode = null, string kind = "menu")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Name = name.Trim();
        Path = path.Trim();
        Icon = icon?.Trim();
        ParentId = parentId;
        Sort = sort;
        IsVisible = isVisible;
        ModuleId = moduleId?.Trim();
        PermissionCode = permissionCode?.Trim();
        Kind = NormalizeKind(kind);
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string NormalizeKind(string kind) =>
        kind is "menu" or "button" or "group" ? kind : "menu";
}

