using System.Text.Json;
using BlazorBoilerplate.ApiHost.Setup;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Identity.Domain;
using BlazorBoilerplate.Modules.Identity.Persistence;
using BlazorBoilerplate.ModuleRuntime.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.ApiHost.Modules;

public sealed record HostModuleMigration(string ModuleId, IModuleDatabaseMigrations Migrations);

public sealed class ModuleMigrationRunner(
    IServiceProvider rootServices,
    IdentityDbContext db,
    ModuleLifecycleManager manager,
    IEnumerable<HostModuleMigration> hostMigrations,
    ILogger<ModuleMigrationRunner> logger)
{
    public async Task<int> ApplyAsync(string moduleId, CancellationToken ct)
    {
        var moduleServices = manager.GetModuleServices(moduleId);
        var moduleMigrations = moduleServices?.GetService<IModuleDatabaseMigrations>();
        var hostMigration = hostMigrations.FirstOrDefault(h =>
            string.Equals(h.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase))?.Migrations;
        if (moduleMigrations is null && hostMigration is null) return 0;

        var mergedSteps = new Dictionary<int, ModuleMigrationStep>();
        if (hostMigration is not null)
            foreach (var step in hostMigration.Steps)
                mergedSteps[step.Sequence] = step;
        if (moduleMigrations is not null)
            foreach (var step in moduleMigrations.Steps)
                mergedSteps[step.Sequence] = step;

        await IdentityMigrationHelper.MigrateAsync(cancellationToken: ct);

        var applied = await db.ModuleMigrationHistories
            .AsNoTracking()
            .Where(h => h.ModuleId == moduleId)
            .Select(h => (int?)h.Sequence)
            .MaxAsync(ct) ?? 0;

        // 迁移步骤可能解析 scoped 服务（Identity 的 SeedPermissions 就要拿 IdentityDbContext）。
        // moduleServices / rootServices 都是根容器，直接传进去会抛 "Cannot resolve scoped service from root provider"，
        // 导致整个 OnLoaded 钩子中断（Identity 权限和菜单同步因此全被跳过）。必须用 scope 执行。
        var executionProvider = moduleServices ?? rootServices;
        await using var executionScope = executionProvider.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        var executionServices = executionScope.ServiceProvider;

        var count = 0;
        foreach (var step in mergedSteps.Values.OrderBy(s => s.Sequence))
        {
            if (step.Sequence <= applied) continue;
            await step.ExecuteAsync(executionServices, ct);
            db.ModuleMigrationHistories.Add(ModuleMigrationHistory.Create(moduleId, step.Sequence, step.Name));
            await db.SaveChangesAsync(ct);
            applied = step.Sequence;
            count++;
            logger.LogInformation("模块 {ModuleId} EF Core 增量迁移已应用：#{Sequence} {Name}", moduleId, step.Sequence, step.Name);
        }
        return count;
    }
}

/// <summary>
/// 模块 ↔ 菜单绑定：
/// - 安装/加载时读取 manifest 的 frontend.menuItems 与 permissions，缺的菜单自动新增（已存在则保留，不覆盖用户改动），
///   并把模块声明的权限码登记为按钮级权限定义；
/// - 卸载时把属于该模块的菜单「禁用」（IsVisible=false），不删除、不清理角色授权，重新加载即恢复；
/// - 数据库迁移与菜单同步彼此独立，降级同样不会删菜单。
/// </summary>
public sealed class ModuleMenuSyncService(
    IdentityDbContext db,
    ModuleCatalog catalog,
    ILogger<ModuleMenuSyncService> logger)
{
    private static readonly JsonSerializerOptions ManifestOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<int> SyncAsync(string moduleId, string? packagePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(packagePath)) return 0;
        var manifestPath = Path.Combine(packagePath, "manifest.json");
        if (!File.Exists(manifestPath)) return 0;

        ModuleManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ModuleManifest>(await File.ReadAllTextAsync(manifestPath, ct), ManifestOptions)
                       ?? new ModuleManifest();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "模块 {ModuleId} manifest 解析失败，跳过菜单同步", moduleId);
            return 0;
        }

        var added = 0;
        foreach (var item in manifest.Frontend.MenuItems.Where(m => !string.IsNullOrWhiteSpace(m.Route)))
        {
            var route = item.Route.Trim();
            var existing = await db.Menus.FirstOrDefaultAsync(m => m.Path == route, ct);
            if (existing is null)
            {
                db.Menus.Add(Menu.Create(item.Text, route, item.Icon, null, item.Sort, moduleId,
                    permissionCode: null, kind: "menu"));
                added++;
            }
            else if (string.IsNullOrWhiteSpace(existing.ModuleId))
            {
                // 兼容早期 SetupInitializer 未写入 ModuleId 的模块菜单：补齐归属并确保可见。
                existing.Update(existing.Name, existing.Path, existing.Icon, existing.ParentId,
                    existing.Sort, true, moduleId, existing.PermissionCode, existing.Kind);
                added++;
            }
            else if (string.Equals(existing.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase) && !existing.IsVisible)
            {
                // 模块重新加载：恢复被卸载流程禁用的菜单（保留管理员改过的名称/图标/排序）
                existing.Update(existing.Name, existing.Path, existing.Icon, existing.ParentId,
                    existing.Sort, true, existing.ModuleId, existing.PermissionCode, existing.Kind);
                added++;
            }
        }

        // 优先同步 manifest.PermissionItems（带模块自声明的中文名称）；若库中已有同名且 Name 仍为 Code 的旧行则补全中文名
        var existingPerms = await db.PermissionDefinitions.ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase, ct);

        // 清理已拆分为 create/update/delete 的历史粗粒度 *.write 权限定义（仅针对当前模块不再声明的项）
        string[] deprecatedWriteCodes = ["identity.user.write", "identity.role.write", "identity.menu.write"];
        if (string.Equals(moduleId, "identity", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var oldCode in deprecatedWriteCodes)
            {
                if (existingPerms.TryGetValue(oldCode, out var oldEntity))
                {
                    db.PermissionDefinitions.Remove(oldEntity);
                    existingPerms.Remove(oldCode);
                }
            }
        }
        foreach (var item in manifest.PermissionItems.Where(p => !string.IsNullOrWhiteSpace(p.Code)))
        {
            var code = item.Code.Trim();
            var name = string.IsNullOrWhiteSpace(item.Name) ? code : item.Name.Trim();
            var kind = string.IsNullOrWhiteSpace(item.Kind) ? "button" : item.Kind.Trim();
            if (existingPerms.TryGetValue(code, out var existing))
            {
                if (string.IsNullOrWhiteSpace(existing.Name) || string.Equals(existing.Name, existing.Code, StringComparison.OrdinalIgnoreCase))
                    existing.Update(name, existing.ModuleId ?? moduleId, existing.MenuPath, existing.Kind);
            }
            else
            {
                var created = PermissionDefinition.Create(code, name, moduleId, null, kind);
                db.PermissionDefinitions.Add(created);
                existingPerms[code] = created;
            }
        }

        foreach (var rawCode in manifest.Permissions.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct())
        {
            var code = rawCode.Trim();
            if (existingPerms.ContainsKey(code)) continue;
            var created = PermissionDefinition.Create(code, code, moduleId, null, "button");
            db.PermissionDefinitions.Add(created);
            existingPerms[code] = created;
        }

        await db.SaveChangesAsync(ct);
        if (added > 0) logger.LogInformation("模块 {ModuleId} 菜单同步：新增/恢复 {Count} 项", moduleId, added);
        return added;
    }

    public async Task<int> DisableAsync(string moduleId, CancellationToken ct)
    {
        var routeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (catalog.TryGet(moduleId, out var entry))
        {
            foreach (var r in entry.Descriptor.Routes)
                if (!string.IsNullOrWhiteSpace(r)) routeSet.Add(r.Trim());
            foreach (var mi in entry.Manifest.Frontend.MenuItems)
                if (!string.IsNullOrWhiteSpace(mi.Route)) routeSet.Add(mi.Route.Trim());
        }

        var visibleMenus = await db.Menus.Where(m => m.IsVisible).ToListAsync(ct);
        var rows = visibleMenus
            .Where(m =>
                string.Equals(m.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase) ||
                (string.IsNullOrWhiteSpace(m.ModuleId) && routeSet.Contains(m.Path.Trim())))
            .ToList();

        foreach (var menu in rows)
        {
            var boundModuleId = string.IsNullOrWhiteSpace(menu.ModuleId) ? moduleId : menu.ModuleId;
            menu.Update(menu.Name, menu.Path, menu.Icon, menu.ParentId, menu.Sort, false,
                boundModuleId, menu.PermissionCode, menu.Kind);
        }
        await db.SaveChangesAsync(ct);
        if (rows.Count > 0) logger.LogInformation("模块 {ModuleId} 已卸载：禁用 {Count} 项菜单（未删除）", moduleId, rows.Count);
        return rows.Count;
    }
}

/// <summary>
/// 模块生命周期 → 数据库 / 菜单的宿主侧钩子。
/// 由启动自动加载、/api/modules/{id}/load、/load-all、/install、/upload、/unload 调用。
/// Bootstrap（未安装，数据库还不存在）时全部跳过；异常只记日志，绝不阻断模块加载/卸载本身。
/// 关键不变式：降级 / 切旧版本 / 卸载都不会回滚数据库，也不会删除菜单（只禁用）。
/// </summary>
public static class ModuleDataHooks
{
    public static async Task OnLoadedAsync(
        IServiceProvider services, string moduleId, string? packagePath, CancellationToken ct)
    {
        if (!Ready(services)) return;
        try
        {
            await services.GetRequiredService<ModuleMigrationRunner>().ApplyAsync(moduleId, ct);
            await services.GetRequiredService<ModuleMenuSyncService>().SyncAsync(moduleId, packagePath, ct);
        }
        catch (Exception ex)
        {
            Log(services).LogWarning(ex, "模块 {ModuleId} 加载后的数据库迁移/菜单同步失败（不影响模块运行）", moduleId);
        }
    }

    /// <summary>安装（尚未 Load）：只补菜单与权限码，不执行迁移（模块 DI 容器还不存在）。</summary>
    public static async Task OnInstalledAsync(
        IServiceProvider services, string moduleId, string? packagePath, CancellationToken ct)
    {
        if (!Ready(services)) return;
        try
        {
            await services.GetRequiredService<ModuleMenuSyncService>().SyncAsync(moduleId, packagePath, ct);
        }
        catch (Exception ex)
        {
            Log(services).LogWarning(ex, "模块 {ModuleId} 安装后的菜单同步失败（不影响安装）", moduleId);
        }
    }

    public static async Task OnUnloadedAsync(IServiceProvider services, string moduleId, CancellationToken ct)
    {
        if (!Ready(services)) return;
        try
        {
            await services.GetRequiredService<ModuleMenuSyncService>().DisableAsync(moduleId, ct);
        }
        catch (Exception ex)
        {
            Log(services).LogWarning(ex, "模块 {ModuleId} 卸载后的菜单禁用失败（不影响卸载）", moduleId);
        }
    }

    private static bool Ready(IServiceProvider services) =>
        services.GetRequiredService<SetupStateProvider>().CurrentMode == SetupMode.Normal;

    private static ILogger Log(IServiceProvider services) =>
        services.GetRequiredService<ILoggerFactory>().CreateLogger("BlazorBoilerplate.ModuleDataHooks");
}
