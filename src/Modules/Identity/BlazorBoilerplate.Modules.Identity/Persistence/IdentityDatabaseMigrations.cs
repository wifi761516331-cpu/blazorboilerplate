using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Identity.Persistence;

/// <summary>
/// Identity 模块数据库迁移与幂等种子（全部基于 EF Core，无任何原生 SQL 字符串，
/// 自动适配 PostgreSQL / MySQL / MSSQL / SQLite 四种数据库）。
/// </summary>
public sealed class IdentityDatabaseMigrations : IModuleDatabaseMigrations
{
    public IReadOnlyList<ModuleMigrationStep> Steps { get; } =
    [
        new ModuleMigrationStep(1, "EnsureIdentitySchema", EnsureSchemaAsync),
        new ModuleMigrationStep(2, "SeedButtonPermissions", (sp, ct) => SeedPermissionsAsync(sp, ButtonPermissions, ct)),
        new ModuleMigrationStep(3, "SeedPlatformMenusAndPermissions", SeedPlatformMenusAndPermissionsAsync)
    ];

    private static readonly (string Code, string Name, string ModuleId)[] ButtonPermissions =
    [
        ("identity.user.create", "创建用户", "identity"),
        ("identity.user.read", "查看用户", "identity"),
        ("identity.user.update", "编辑用户", "identity"),
        ("identity.user.delete", "删除用户", "identity"),
        ("identity.role.create", "创建角色", "identity"),
        ("identity.role.update", "编辑角色", "identity"),
        ("identity.role.delete", "删除角色", "identity"),
        ("identity.menu.create", "创建菜单", "identity"),
        ("identity.menu.update", "编辑菜单", "identity"),
        ("identity.menu.delete", "删除菜单", "identity"),
        ("module.upload", "上传模块", "module"),
        ("module.pack", "打包模块", "module"),
        ("module.download", "下载模块", "module"),
        ("module.load", "加载模块", "module"),
        ("module.unload", "卸载模块", "module")
    ];

    private static readonly (string Code, string Name, string ModuleId)[] PlatformPermissions =
    [
        ("system.settings.read", "查看系统设置", "system"),
        ("system.settings.update", "修改系统设置", "system"),
        ("tenant.create", "创建租户", "tenant"),
        ("tenant.update", "编辑租户", "tenant"),
        ("tenant.invite", "邀请租户成员", "tenant"),
        ("module.upload", "上传模块", "module"),
        ("module.pack", "打包模块", "module"),
        ("module.download", "下载模块", "module"),
        ("module.load", "加载模块", "module"),
        ("module.unload", "卸载模块", "module")
    ];

    private static Task EnsureSchemaAsync(IServiceProvider sp, CancellationToken ct) =>
        IdentityMigrationHelper.MigrateAsync(cancellationToken: ct);

    private static Task SeedPermissionsAsync(
        IServiceProvider sp,
        (string Code, string Name, string ModuleId)[] items,
        CancellationToken ct)
    {
        var db = sp.GetRequiredService<IdentityDbContext>();
        return SeedPermissionsCoreAsync(db, items, ct);
    }

    private static async Task SeedPlatformMenusAndPermissionsAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<IdentityDbContext>();

        const string settingsPath = "/system/settings";
        var settingsMenu = await db.Menus.FirstOrDefaultAsync(m => m.Path == settingsPath, ct);
        if (settingsMenu is null)
        {
            settingsMenu = Menu.Create("系统设置", settingsPath, "Settings", parentId: null, sort: 950, kind: "menu");
            db.Menus.Add(settingsMenu);
        }

        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.IsSystem && r.Name == "管理员", ct);
        if (adminRole is not null)
        {
            var hasMenu = await db.RoleMenus.AnyAsync(
                rm => rm.RoleId == adminRole.Id && rm.MenuId == settingsMenu.Id, ct);
            if (!hasMenu)
                db.RoleMenus.Add(new RoleMenu(adminRole.Id, settingsMenu.Id));
        }

        await SeedPermissionsCoreAsync(db, PlatformPermissions, ct);
    }

    private static async Task SeedPermissionsCoreAsync(
        IdentityDbContext db,
        (string Code, string Name, string ModuleId)[] items,
        CancellationToken ct)
    {
        var existingCodes = await db.PermissionDefinitions
            .AsNoTracking()
            .Select(p => p.Code)
            .ToHashSetAsync(StringComparer.OrdinalIgnoreCase, ct);

        foreach (var (code, name, moduleId) in items)
        {
            if (existingCodes.Add(code))
                db.PermissionDefinitions.Add(PermissionDefinition.Create(code, name, moduleId, null, "button"));
        }

        await db.SaveChangesAsync(ct);

        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.IsSystem && r.Name == "管理员", ct);
        if (adminRole is not null)
        {
            var allCodes = await db.PermissionDefinitions.AsNoTracking().Select(p => p.Code).ToListAsync(ct);
            var grantedCodes = await db.RolePermissions
                .AsNoTracking()
                .Where(rp => rp.RoleId == adminRole.Id)
                .Select(rp => rp.Code)
                .ToHashSetAsync(StringComparer.OrdinalIgnoreCase, ct);

            foreach (var code in allCodes)
            {
                if (grantedCodes.Add(code))
                    db.RolePermissions.Add(new RolePermission(adminRole.Id, code));
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
