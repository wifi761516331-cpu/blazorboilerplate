using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Modules.Identity.Domain;

/// <summary>权限定义目录。Kind = "menu"（菜单级）/ "button"（按钮级）；Code 唯一性由服务层保证。</summary>
public sealed class PermissionDefinition : AuditableEntity<Guid>
{
    private PermissionDefinition() { }

    private PermissionDefinition(string code, string name, string? moduleId, string? menuPath, string kind)
    {
        Id = Guid.CreateVersion7();
        Code = code;
        Name = name;
        ModuleId = moduleId;
        MenuPath = menuPath;
        Kind = kind;
    }

    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? ModuleId { get; private set; }
    public string? MenuPath { get; private set; }
    public string Kind { get; private set; } = default!;

    public static PermissionDefinition Create(string code, string name, string? moduleId = null,
        string? menuPath = null, string kind = "button")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new PermissionDefinition(code.Trim(), name.Trim(), moduleId?.Trim(), menuPath?.Trim(), NormalizeKind(kind));
    }

    public void Update(string name, string? moduleId, string? menuPath, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        ModuleId = moduleId?.Trim();
        MenuPath = menuPath?.Trim();
        Kind = NormalizeKind(kind);
    }

    private static string NormalizeKind(string kind) =>
        kind is "menu" or "button" ? kind : "button";
}

