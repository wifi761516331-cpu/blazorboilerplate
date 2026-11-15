using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Identity.Persistence;
using BlazorBoilerplate.Modules.Identity.Presentation;
using BlazorBoilerplate.Modules.Identity.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Identity;

/// <summary>
/// Identity 模块入口（必要模块，isEssential=true，不可卸载）。
/// 承载用户管理 / 角色管理 / 菜单管理：EF Core + PostgreSQL 迁移
/// （参考 Nova 方案：迁移产物不含数据库级外键，完整性由服务层保证）。
/// 连接串来自安装向导（PlatformConnectionState），模块在安装完成后才会被加载。
/// </summary>
public sealed class IdentityModule : IModule
{
    public const string ModuleId = "identity";

    public void ConfigureServices(IServiceCollection services, IModuleContext context)
    {
        services.AddDbContext<IdentityDbContext>((sp, o) => o.UseIdentityProvider(sp));
        // 增量迁移：宿主在模块加载时按 Sequence 递增执行（幂等、只增不减、绝不回滚）。
        services.AddSingleton<IModuleDatabaseMigrations, IdentityDatabaseMigrations>();

        services.AddScoped<UserService>();
        services.AddScoped<RoleService>();
        services.AddScoped<MenuService>();
        services.AddScoped<PermissionService>();
        services.AddScoped<TenantService>();
        services.AddScoped<UserSessionService>();
        services.AddScoped<UserPreferenceService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<PushSubscriptionService>();
    }

    public void MapEndpoints(IModuleEndpointBuilder builder, IServiceProvider moduleServices)
    {
        var endpoints = new IdentityEndpoints(moduleServices);
        endpoints.Map(builder);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAcceptingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DrainAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
