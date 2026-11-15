using BlazorBoilerplate.Modules.Identity.Domain;
using BlazorBoilerplate.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Modules.Identity.Services;

/// <summary>用户管理。唯一性校验在代码层（数据库无唯一约束，Nova 约定）。</summary>
public sealed class UserService(IdentityDbContext db)
{
    // 主键为 Guid v7（时间有序），用它排序等价于按创建时间排序；
    // SQLite 不支持 DateTimeOffset 出现在 ORDER BY，故不直接按 CreatedAtUtc 排序。
    public Task<List<User>> ListAsync(CancellationToken ct = default) =>
        db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync(ct);

    public async Task<User> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct)
        ?? throw new KeyNotFoundException($"用户不存在：{id}");

    public async Task<User> CreateAsync(string userName, string email, string displayName, string password, CancellationToken ct = default)
    {
        var name = userName.Trim();
        if (await db.Users.AnyAsync(u => u.UserName == name, ct))
            throw new InvalidOperationException($"用户名已存在：{name}");
        var mail = email.Trim();
        if (await db.Users.AnyAsync(u => u.Email == mail, ct))
            throw new InvalidOperationException($"邮箱已被占用：{mail}");

        var user = User.Create(name, mail, displayName, PasswordHasher.Hash(password));
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task<User> UpdateAsync(Guid id, string email, string displayName, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new KeyNotFoundException($"用户不存在：{id}");
        var mail = email.Trim();
        if (await db.Users.AnyAsync(u => u.Email == mail && u.Id != id, ct))
            throw new InvalidOperationException($"邮箱已被占用：{mail}");

        user.Update(mail, displayName);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task ResetPasswordAsync(Guid id, string newPassword, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new KeyNotFoundException($"用户不存在：{id}");
        user.SetPassword(PasswordHasher.Hash(newPassword));
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> VerifyPasswordAsync(Guid id, string password, CancellationToken ct = default) =>
        VerifyPasswordCoreAsync(id, password, ct);

    private async Task<bool> VerifyPasswordCoreAsync(Guid id, string password, CancellationToken ct)
    {
        var hash = await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.PasswordHash).FirstOrDefaultAsync(ct);
        return hash is not null && PasswordHasher.Verify(password, hash);
    }

    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new KeyNotFoundException($"用户不存在：{id}");
        if (isActive) user.Activate(); else user.Deactivate();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>删除用户并级联清理其角色关联（代码级级联 —— 无数据库外键）。</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new KeyNotFoundException($"用户不存在：{id}");
        var links = await db.UserRoles.Where(x => x.UserId == id).ToListAsync(ct);
        db.UserRoles.RemoveRange(links);
        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>整体重设用户角色（差量更新）。角色必须存在。</summary>
    public async Task AssignRolesAsync(Guid id, Guid[] roleIds, CancellationToken ct = default)
    {
        _ = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new KeyNotFoundException($"用户不存在：{id}");

        foreach (var roleId in roleIds)
            if (!await db.Roles.AnyAsync(r => r.Id == roleId, ct))
                throw new KeyNotFoundException($"角色不存在：{roleId}");

        var existing = await db.UserRoles.Where(x => x.UserId == id).ToListAsync(ct);
        foreach (var toRemove in existing.Where(x => !roleIds.Contains(x.RoleId)))
            db.UserRoles.Remove(toRemove);
        foreach (var roleId in roleIds.Where(rid => existing.All(x => x.RoleId != rid)))
            db.UserRoles.Add(new UserRole(id, roleId));

        await db.SaveChangesAsync(ct);
    }

    public Task<List<Guid>> GetRoleIdsAsync(Guid userId, CancellationToken ct = default) =>
        db.UserRoles.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.RoleId).ToListAsync(ct);

    // ---- 认证 / 授权查询（供 ApiHost 的 UserController、登录流程使用）----

    /// <summary>按账号（UserName 或 Email）查找，大小写不敏感。</summary>
    public Task<User?> FindByAccountAsync(string account, CancellationToken ct = default)
    {
        var normalized = (account ?? string.Empty).Trim().ToLowerInvariant();
        return db.Users.AsNoTracking().FirstOrDefaultAsync(
            u => u.UserName.ToLower() == normalized || u.Email.ToLower() == normalized, ct);
    }

    /// <summary>用户所属全部角色名。</summary>
    public async Task<string[]> GetRoleNamesAsync(Guid userId, CancellationToken ct = default)
    {
        var roleIds = await GetRoleIdsAsync(userId, ct);
        if (roleIds.Count == 0) return [];
        return await db.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id)).Select(r => r.Name).ToArrayAsync(ct);
    }

    /// <summary>
    /// 合并用户所有角色的 RolePermissions；系统内置管理员返回 ["*"]（全量权限）。
    /// </summary>
    public async Task<string[]> GetPermissionCodesAsync(Guid userId, CancellationToken ct = default)
    {
        if (await IsSystemAdminAsync(userId, ct)) return ["*"];

        var roleIds = await GetRoleIdsAsync(userId, ct);
        if (roleIds.Count == 0) return [];
        return await db.RolePermissions.AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.Code).Distinct().ToArrayAsync(ct);
    }

    /// <summary>管理员返回全部可见菜单 Path；其余按 RoleMenus 关联求并集。</summary>
    public async Task<string[]> GetMenuPathsAsync(Guid userId, CancellationToken ct = default)
    {
        if (await IsSystemAdminAsync(userId, ct))
            return await db.Menus.AsNoTracking().Where(m => m.IsVisible)
                .Select(m => m.Path).ToArrayAsync(ct);

        var roleIds = await GetRoleIdsAsync(userId, ct);
        if (roleIds.Count == 0) return [];
        var menuIds = await db.RoleMenus.AsNoTracking()
            .Where(rm => roleIds.Contains(rm.RoleId))
            .Select(rm => rm.MenuId).Distinct().ToListAsync(ct);
        return await db.Menus.AsNoTracking()
            .Where(m => m.IsVisible && menuIds.Contains(m.Id))
            .Select(m => m.Path).ToArrayAsync(ct);
    }

    private const string AdminRoleName = "管理员";

    private async Task<bool> IsSystemAdminAsync(Guid userId, CancellationToken ct)
    {
        var roleIds = await GetRoleIdsAsync(userId, ct);
        if (roleIds.Count == 0) return false;
        return await db.Roles.AsNoTracking()
            .AnyAsync(r => roleIds.Contains(r.Id) && r.IsSystem && r.Name == AdminRoleName, ct);
    }
}

/// <summary>角色管理。IsSystem 角色受保护；删除时级联清理 UserRoles/RoleMenus（代码级）。</summary>
public sealed class RoleService(IdentityDbContext db)
{
    public Task<List<Role>> ListAsync(CancellationToken ct = default) =>
        db.Roles.AsNoTracking().OrderBy(r => r.Id).ToListAsync(ct);

    public async Task<Role> CreateAsync(string name, string? description, CancellationToken ct = default)
    {
        var roleName = name.Trim();
        if (await db.Roles.AnyAsync(r => r.Name == roleName, ct))
            throw new InvalidOperationException($"角色名已存在：{roleName}");

        var role = Role.Create(roleName, description);
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        return role;
    }

    public async Task UpdateAsync(Guid id, string name, string? description, CancellationToken ct = default)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new KeyNotFoundException($"角色不存在：{id}");
        if (role.IsSystem && !string.Equals(role.Name, name.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException("系统内置角色不允许改名。");
        var roleName = name.Trim();
        if (await db.Roles.AnyAsync(r => r.Name == roleName && r.Id != id, ct))
            throw new InvalidOperationException($"角色名已存在：{roleName}");

        role.Update(roleName, description);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new KeyNotFoundException($"角色不存在：{id}");
        if (role.IsSystem)
            throw new InvalidOperationException("系统内置角色不允许删除。");

        db.UserRoles.RemoveRange(await db.UserRoles.Where(x => x.RoleId == id).ToListAsync(ct));
        db.RoleMenus.RemoveRange(await db.RoleMenus.Where(x => x.RoleId == id).ToListAsync(ct));
        db.Roles.Remove(role);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>整体重设角色可访问菜单（差量更新）。菜单必须存在。</summary>
    public async Task AssignMenusAsync(Guid id, Guid[] menuIds, CancellationToken ct = default)
    {
        _ = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new KeyNotFoundException($"角色不存在：{id}");

        foreach (var menuId in menuIds)
            if (!await db.Menus.AnyAsync(m => m.Id == menuId, ct))
                throw new KeyNotFoundException($"菜单不存在：{menuId}");

        var existing = await db.RoleMenus.Where(x => x.RoleId == id).ToListAsync(ct);
        foreach (var toRemove in existing.Where(x => !menuIds.Contains(x.MenuId)))
            db.RoleMenus.Remove(toRemove);
        foreach (var menuId in menuIds.Where(mid => existing.All(x => x.MenuId != mid)))
            db.RoleMenus.Add(new RoleMenu(id, menuId));

        await db.SaveChangesAsync(ct);
    }

    public Task<List<Guid>> GetMenuIdsAsync(Guid roleId, CancellationToken ct = default) =>
        db.RoleMenus.AsNoTracking().Where(x => x.RoleId == roleId).Select(x => x.MenuId).ToListAsync(ct);

    // ---- 按钮级权限 ----

    public Task<string[]> GetPermissionCodesAsync(Guid roleId, CancellationToken ct = default) =>
        db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Code).Distinct().ToArrayAsync(ct);

    /// <summary>整体重设角色权限码（差量更新：只增删差集，保留未变动的行）。</summary>
    public async Task AssignPermissionCodesAsync(Guid roleId, string[] codes, CancellationToken ct = default)
    {
        _ = await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct)
            ?? throw new KeyNotFoundException($"角色不存在：{roleId}");

        var wanted = (codes ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var existing = await db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync(ct);
        foreach (var toRemove in existing.Where(x => !wanted.Contains(x.Code)))
            db.RolePermissions.Remove(toRemove);
        foreach (var code in wanted.Where(c => existing.All(x => x.Code != c)))
            db.RolePermissions.Add(new RolePermission(roleId, code));

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>菜单管理（自引用树，代码层校验父节点存在性与循环引用）。</summary>
public sealed class MenuService(IdentityDbContext db)
{
    public Task<List<Menu>> ListAsync(CancellationToken ct = default) =>
        db.Menus.AsNoTracking().OrderBy(m => m.Sort).ThenBy(m => m.Id).ToListAsync(ct);

    /// <summary>可见菜单（侧边栏数据源），按 Sort 升序。</summary>
    public Task<List<Menu>> ListVisibleAsync(CancellationToken ct = default) =>
        db.Menus.AsNoTracking().Where(m => m.IsVisible).OrderBy(m => m.Sort).ThenBy(m => m.Id).ToListAsync(ct);

    // 新增的 moduleId / permissionCode / kind 放在 ct 之后：既保持旧调用点（位置参数到 ct）
    // 编译通过，又能让端点用命名/位置参数把 MenuUpsertRequest 的新字段透传进来。
    public async Task<Menu> CreateAsync(string name, string path, string? icon, Guid? parentId, int sort,
        CancellationToken ct = default, string? moduleId = null, string? permissionCode = null, string? kind = null,
        bool isVisible = true)
    {
        if (parentId is { } pid && !await db.Menus.AnyAsync(m => m.Id == pid, ct))
            throw new KeyNotFoundException($"父菜单不存在：{pid}");

        var menu = Menu.Create(name, path, icon, parentId, sort, moduleId, permissionCode, kind ?? "menu");
        if (!isVisible)
            menu.Update(name, path, icon, parentId, sort, false, moduleId, permissionCode, kind ?? "menu");
        db.Menus.Add(menu);
        await db.SaveChangesAsync(ct);
        return menu;
    }

    public async Task UpdateAsync(Guid id, string name, string path, string? icon, Guid? parentId, int sort, bool isVisible,
        CancellationToken ct = default, string? moduleId = null, string? permissionCode = null, string? kind = null)
    {
        var menu = await db.Menus.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new KeyNotFoundException($"菜单不存在：{id}");

        if (parentId is { } pid)
        {
            if (pid == id)
                throw new InvalidOperationException("菜单不能以自己为父节点。");
            if (!await db.Menus.AnyAsync(m => m.Id == pid, ct))
                throw new KeyNotFoundException($"父菜单不存在：{pid}");
            if (!await IsDescendantFreeAsync(id, pid, ct))
                throw new InvalidOperationException("不能把菜单移动到自己的子树之下（会形成循环）。");
        }

        menu.Update(name, path, icon, parentId, sort, isVisible, moduleId, permissionCode, kind ?? "menu");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 检查把 nodeId 挂到 targetParentId 下是否会形成循环：
    /// 必须沿 ParentId 【向上】追溯 targetParent 的祖先链，若遇到 nodeId 说明 node 是它的祖先（成环）。
    /// 向下遍历子树是错误方向 —— 那样只会把 node 当成 targetParent 的后代。
    /// </summary>
    private async Task<bool> IsDescendantFreeAsync(Guid nodeId, Guid targetParentId, CancellationToken ct)
    {
        var all = await db.Menus.AsNoTracking().Select(m => new { m.Id, m.ParentId }).ToListAsync(ct);
        var parentOf = all.ToDictionary(x => x.Id, x => x.ParentId);

        var current = targetParentId;
        var visited = new HashSet<Guid>();
        while (visited.Add(current))
        {
            if (current == nodeId)
                return false;
            if (!parentOf.TryGetValue(current, out var parent) || parent is null)
                return true; // 已到根节点，无环
            current = parent.Value;
        }
        return false; // 祖先链本身就存在环（历史脏数据），保守拒绝
    }

    /// <summary>删除菜单。存在子菜单时拒绝（代码级 Restrict）；级联清理 RoleMenus。</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var menu = await db.Menus.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new KeyNotFoundException($"菜单不存在：{id}");
        if (await db.Menus.AnyAsync(m => m.ParentId == id, ct))
            throw new InvalidOperationException("该菜单存在子菜单，请先删除或移动子菜单。");

        db.RoleMenus.RemoveRange(await db.RoleMenus.Where(x => x.MenuId == id).ToListAsync(ct));
        db.Menus.Remove(menu);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>构建菜单树（管理端），父节点在前。</summary>
    public async Task<List<MenuNode>> GetTreeAsync(CancellationToken ct = default)
    {
        var all = await ListAsync(ct);
        return BuildTree(all, parentId: null);
    }

    private static List<MenuNode> BuildTree(List<Menu> all, Guid? parentId) =>
        all.Where(m => m.ParentId == parentId)
            .Select(m => new MenuNode(
                m.Id, m.Name, m.Path, m.Icon, m.Sort, m.IsVisible,
                BuildTree(all, m.Id)))
            .ToList();
}

/// <summary>菜单树节点 DTO。</summary>
public sealed record MenuNode(
    Guid Id,
    string Name,
    string Path,
    string? Icon,
    int Sort,
    bool IsVisible,
    List<MenuNode> Children);
