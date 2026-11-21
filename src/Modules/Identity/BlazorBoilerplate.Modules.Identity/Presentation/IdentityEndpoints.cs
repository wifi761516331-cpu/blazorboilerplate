using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Identity.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Identity.Presentation;

/// <summary>
/// Identity 模块端点：/api/identity/users|roles|menus。
/// DbContext 为 Scoped —— 每个请求在 moduleServices 上开子作用域解析服务；
/// KeyNotFoundException → 404，InvalidOperationException → 409（业务冲突）。
/// </summary>
public sealed class IdentityEndpoints(IServiceProvider moduleServices)
{
    public void Map(IModuleEndpointBuilder builder)
    {
        // 用户管理
        builder.Map("/api/identity/users", "GET", "identity.user.read", async ctx => await WithScope(ctx, async s =>
        {
            var users = await s.ServiceProvider.GetRequiredService<UserService>().ListAsync(ctx.RequestAborted, ResolveTenantId(ctx));
            await ctx.Response.WriteAsJsonAsync(users.Select(ToUserDto), ctx.RequestAborted);
        }));
        builder.Map("/api/identity/users", "POST", "identity.user.create", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<UserUpsertRequest>(ctx);
            var user = await s.ServiceProvider.GetRequiredService<UserService>().CreateAsync(req.UserName, req.Email, req.DisplayName, req.Password, ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(ToUserDto(user), ctx.RequestAborted);
        }));
        builder.Map("/api/identity/users/{id}/active", "PUT", "identity.user.update", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<SetActiveRequest>(ctx);
            await s.ServiceProvider.GetRequiredService<UserService>().SetActiveAsync(GetId(ctx), req.IsActive, ctx.RequestAborted);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }));
        builder.Map("/api/identity/users/{id}/password", "PUT", "identity.user.update", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<ResetPasswordRequest>(ctx);
            await s.ServiceProvider.GetRequiredService<UserService>().ResetPasswordAsync(GetId(ctx), req.Password, ctx.RequestAborted);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }));
        builder.Map("/api/identity/users/{id}/roles", "GET", "identity.user.read", async ctx => await WithScope(ctx, async s =>
        {
            var roleIds = await s.ServiceProvider.GetRequiredService<UserService>().GetRoleIdsAsync(GetId(ctx), ctx.RequestAborted, ResolveTenantId(ctx));
            await ctx.Response.WriteAsJsonAsync(roleIds, ctx.RequestAborted);
        }));
        builder.Map("/api/identity/users/{id}/roles", "PUT", "identity.user.update", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<AssignRequest>(ctx);
            await s.ServiceProvider.GetRequiredService<UserService>().AssignRolesAsync(GetId(ctx), req.Ids, ctx.RequestAborted, ResolveTenantId(ctx));
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }));
        builder.Map("/api/identity/users/{id}", "PUT", "identity.user.update", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<UserUpsertRequest>(ctx);
            var user = await s.ServiceProvider.GetRequiredService<UserService>().UpdateAsync(GetId(ctx), req.Email, req.DisplayName, ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(ToUserDto(user), ctx.RequestAborted);
        }));
        builder.Map("/api/identity/users/{id}", "DELETE", "identity.user.delete", async ctx => await WithScope(ctx, async s =>
        {
            await s.ServiceProvider.GetRequiredService<UserService>().DeleteAsync(GetId(ctx), ctx.RequestAborted);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }));

        // 角色管理
        builder.Map("/api/identity/roles", "GET", "identity.role.read", async ctx => await WithScope(ctx, async s =>
        {
            var roles = await s.ServiceProvider.GetRequiredService<RoleService>().ListAsync(ctx.RequestAborted, ResolveTenantId(ctx));
            await ctx.Response.WriteAsJsonAsync(roles, ctx.RequestAborted);
        }));
        builder.Map("/api/identity/roles", "POST", "identity.role.create", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<RoleUpsertRequest>(ctx);
            var role = await s.ServiceProvider.GetRequiredService<RoleService>().CreateAsync(req.Name, req.Description, ctx.RequestAborted, ResolveTenantId(ctx));
            await ctx.Response.WriteAsJsonAsync(role, ctx.RequestAborted);
        }));
        builder.Map("/api/identity/roles/{id}/menus", "GET", "identity.role.read", async ctx => await WithScope(ctx, async s =>
        {
            var menuIds = await s.ServiceProvider.GetRequiredService<RoleService>().GetMenuIdsAsync(GetId(ctx), ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(menuIds, ctx.RequestAborted);
        }));
        builder.Map("/api/identity/roles/{id}/menus", "PUT", "identity.role.update", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<AssignRequest>(ctx);
            await s.ServiceProvider.GetRequiredService<RoleService>().AssignMenusAsync(GetId(ctx), req.Ids, ctx.RequestAborted);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }));
        builder.Map("/api/identity/roles/{id}", "PUT", "identity.role.update", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<RoleUpsertRequest>(ctx);
            await s.ServiceProvider.GetRequiredService<RoleService>().UpdateAsync(GetId(ctx), req.Name, req.Description, ctx.RequestAborted);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }));
        builder.Map("/api/identity/roles/{id}", "DELETE", "identity.role.delete", async ctx => await WithScope(ctx, async s =>
        {
            await s.ServiceProvider.GetRequiredService<RoleService>().DeleteAsync(GetId(ctx), ctx.RequestAborted);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }));

        // 菜单管理
        builder.Map("/api/identity/menus", "GET", "identity.menu.read", async ctx => await WithScope(ctx, async s =>
        {
            var menus = await s.ServiceProvider.GetRequiredService<MenuService>().ListAsync(ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(menus, ctx.RequestAborted);
        }));
        // 侧边栏可见菜单：所有已认证用户均可读取菜单结构，前端再按 AccountState.MenuPaths / PermissionCode 裁剪
        builder.Map("/api/identity/menus/visible", "GET", "", async ctx => await WithScope(ctx, async s =>
        {
            var menus = await s.ServiceProvider.GetRequiredService<MenuService>().ListVisibleAsync(ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(menus, ctx.RequestAborted);
        }));
        builder.Map("/api/identity/menus/tree", "GET", "identity.menu.read", async ctx => await WithScope(ctx, async s =>
        {
            var tree = await s.ServiceProvider.GetRequiredService<MenuService>().GetTreeAsync(ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(tree, ctx.RequestAborted);
        }));
        builder.Map("/api/identity/menus", "POST", "identity.menu.create", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<MenuUpsertRequest>(ctx);
            var menu = await s.ServiceProvider.GetRequiredService<MenuService>().CreateAsync(
                req.Name, req.Path, req.Icon, req.ParentId, req.Sort, ctx.RequestAborted,
                req.ModuleId, req.PermissionCode, req.Kind, req.IsVisible);
            await ctx.Response.WriteAsJsonAsync(menu, ctx.RequestAborted);
        }));
        builder.Map("/api/identity/menus/{id}", "PUT", "identity.menu.update", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<MenuUpsertRequest>(ctx);
            await s.ServiceProvider.GetRequiredService<MenuService>().UpdateAsync(
                GetId(ctx), req.Name, req.Path, req.Icon, req.ParentId, req.Sort, req.IsVisible, ctx.RequestAborted,
                req.ModuleId, req.PermissionCode, req.Kind);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }));
        builder.Map("/api/identity/menus/{id}", "DELETE", "identity.menu.delete", async ctx => await WithScope(ctx, async s =>
        {
            await s.ServiceProvider.GetRequiredService<MenuService>().DeleteAsync(GetId(ctx), ctx.RequestAborted);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }));

        // 权限目录 / 角色按钮权限
        builder.Map("/api/identity/permissions", "GET", "identity.permission.read", async ctx => await WithScope(ctx, async s =>
        {
            var catalog = await s.ServiceProvider.GetRequiredService<PermissionService>().GetCatalogAsync(ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(catalog, ctx.RequestAborted);
        }));
        builder.Map("/api/identity/roles/{id}/permissions", "GET", "identity.permission.read", async ctx => await WithScope(ctx, async s =>
        {
            var codes = await s.ServiceProvider.GetRequiredService<RoleService>()
                .GetPermissionCodesAsync(GetId(ctx), ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(codes, ctx.RequestAborted);
        }));
        builder.Map("/api/identity/roles/{id}/permissions", "PUT", "identity.permission.write", async ctx => await WithScope(ctx, async s =>
        {
            var req = await ReadJsonAsync<PermissionCodesRequest>(ctx);
            await s.ServiceProvider.GetRequiredService<RoleService>()
                .AssignPermissionCodesAsync(GetId(ctx), req.Codes, ctx.RequestAborted);
            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        }));
        // 调试端点：查看某用户合并后的权限码（管理员为 ["*"]）。
        builder.Map("/api/identity/permissions/user/{userId}", "GET", "identity.permission.read", async ctx => await WithScope(ctx, async s =>
        {
            var codes = await s.ServiceProvider.GetRequiredService<UserService>()
                .GetPermissionCodesAsync(GetGuid(ctx, "userId"), ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(codes, ctx.RequestAborted);
        }));
    }

    // ---- 请求包装：作用域解析 + 统一异常映射 ----

    private async Task WithScope(HttpContext ctx, Func<IServiceScope, Task> handler)
    {
        try
        {
            using var scope = moduleServices.CreateScope();
            await handler(scope);
        }
        catch (KeyNotFoundException ex)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }, ctx.RequestAborted);
        }
        catch (InvalidOperationException ex)
        {
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }, ctx.RequestAborted);
        }
    }

    private static async Task<T> ReadJsonAsync<T>(HttpContext ctx) where T : class
    {
        return await ctx.Request.ReadFromJsonAsync<T>(ctx.RequestAborted)
            ?? throw new InvalidOperationException("请求体不能为空。");
    }

    private static Guid? ResolveTenantId(HttpContext ctx) =>
        ctx.RequestServices.GetService<ICurrentUserContext>()?.TenantId
        ?? (Guid.TryParse(ctx.User.FindFirst("tid")?.Value, out var tid) ? tid : null);

    private static Guid GetId(HttpContext ctx) => GetGuid(ctx, "id");

    private static Guid GetGuid(HttpContext ctx, string key) =>
        Guid.TryParse(ctx.Request.RouteValues[key]?.ToString(), out var id)
            ? id
            : throw new InvalidOperationException("无效的 Id。");

    private static object ToUserDto(Domain.User u) => new
    {
        u.Id,
        u.UserName,
        u.Email,
        u.DisplayName,
        u.IsActive,
        u.CreatedAtUtc,
        u.ModifiedAtUtc
    };

    // ---- 请求 DTO ----

    public sealed record UserUpsertRequest(string UserName, string Email, string DisplayName, string Password);
    public sealed record RoleUpsertRequest(string Name, string? Description);
    /// <summary>
    /// ModuleId / PermissionCode / Kind 为可选字段：旧前端不传时保持默认（平台菜单、Kind="menu"）。
    /// </summary>
    public sealed record MenuUpsertRequest(string Name, string Path, string? Icon, Guid? ParentId, int Sort, bool IsVisible,
        string? ModuleId = null, string? PermissionCode = null, string? Kind = null);
    public sealed record AssignRequest(Guid[] Ids);
    public sealed record ResetPasswordRequest(string Password);
    public sealed record SetActiveRequest(bool IsActive);
    public sealed record PermissionCodesRequest(string[] Codes);
}
