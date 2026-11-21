using BlazorBoilerplate.Modules.Identity.Domain;
using BlazorBoilerplate.Modules.Identity.Persistence;
using BlazorBoilerplate.Shared.Features.Identity;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.ApiHost.Auth;

/// <summary>
/// 从数据库实时聚合当前用户的角色 / 按钮权限码 / 菜单路径 / 租户 / 通知开关。
/// 登录与刷新 token 时调用（token 内嵌 roles/perms，WebHost 的按钮级权限直接消费）。
/// </summary>
public sealed class CurrentUserFactory(IdentityDbContext db)
{
    public async Task<CurrentUserDto> BuildAsync(User user, Guid? tenantId, CancellationToken ct)
    {
        // 初始化时的第一个用户（按 CreatedAtUtc / Id 最早）拥有全量权限，免校验（无论其角色是否被改动）
        var firstUserId = (await db.Users.AsNoTracking()
            .Select(u => new { u.Id, u.CreatedAtUtc })
            .ToListAsync(ct))
            .OrderBy(u => u.CreatedAtUtc)
            .ThenBy(u => u.Id)
            .Select(u => u.Id)
            .FirstOrDefault();

        var isAdmin = firstUserId == user.Id ||
                      await (from ur in db.UserRoles
                             join r in db.Roles on ur.RoleId equals r.Id
                             where ur.UserId == user.Id && r.TenantId == null && r.IsSystem && r.Name == "管理员"
                             select r.Id).AnyAsync(ct);

        var scopedRoleQuery = from ur in db.UserRoles.AsNoTracking()
                              join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                              where ur.UserId == user.Id && ur.TenantId == tenantId && r.TenantId == tenantId
                              select new { r.Id, r.Name };

        var roleNames = await scopedRoleQuery.Select(x => x.Name).Distinct().ToArrayAsync(ct);
        var scopedRoleIds = await scopedRoleQuery.Select(x => x.Id).Distinct().ToArrayAsync(ct);

        string[] permissions;
        if (isAdmin)
        {
            permissions = ["*"];
        }
        else
        {
            permissions = scopedRoleIds.Length == 0
                ? []
                : await db.RolePermissions.AsNoTracking()
                    .Where(p => scopedRoleIds.Contains(p.RoleId))
                    .Select(p => p.Code).Distinct().ToArrayAsync(ct);
        }

        string[] menuPaths;
        if (isAdmin)
        {
            menuPaths = await db.Menus.AsNoTracking().Where(m => m.IsVisible)
                .Select(m => m.Path).ToArrayAsync(ct);
        }
        else
        {
            var menuIds = scopedRoleIds.Length == 0
                ? []
                : await db.RoleMenus.AsNoTracking()
                    .Where(x => scopedRoleIds.Contains(x.RoleId)).Select(x => x.MenuId).Distinct().ToArrayAsync(ct);
            menuPaths = menuIds.Length == 0
                ? []
                : await db.Menus.AsNoTracking()
                    .Where(m => m.IsVisible && menuIds.Contains(m.Id))
                    .Select(m => m.Path).ToArrayAsync(ct);
        }

        Guid? effectiveTenantId = null;
        string? tenantName = null;
        if (tenantId is { } tid)
        {
            var isMember = await db.TenantUsers.AsNoTracking()
                .AnyAsync(tu => tu.UserId == user.Id && tu.TenantId == tid && tu.Accepted, ct);
            if (isMember)
            {
                effectiveTenantId = tid;
                tenantName = await db.Tenants.AsNoTracking().Where(t => t.Id == tid)
                    .Select(t => t.Title ?? t.Name).FirstOrDefaultAsync(ct);
            }
        }

        var notificationsEnabled = await db.UserPreferences.AsNoTracking()
            .Where(p => p.UserId == user.Id).Select(p => p.NotificationsEnabled).FirstOrDefaultAsync(ct);

        return new CurrentUserDto(
            user.Id, user.UserName, user.Email, user.DisplayName,
            roleNames, permissions, menuPaths, effectiveTenantId, tenantName, notificationsEnabled);
    }
}
