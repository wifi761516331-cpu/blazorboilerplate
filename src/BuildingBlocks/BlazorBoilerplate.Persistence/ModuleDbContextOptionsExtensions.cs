using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Persistence;

/// <summary>
/// 模块 DbContextOptions 扩展：根据运行时或传入的数据库类型与连接串配置 EF Core Provider 与审计拦截器。
/// </summary>
public static class ModuleDbContextOptionsExtensions
{
    public static DbContextOptionsBuilder UsePlatformDbProvider(
        this DbContextOptionsBuilder builder,
        IServiceProvider? services = null,
        string? provider = null,
        string? connectionString = null,
        bool enableRetryOnFailure = false)
    {
        var system = PlatformConnectionState.ResolveSystemType(provider);
        var conn = string.IsNullOrWhiteSpace(connectionString)
            ? PlatformConnectionState.ResolveConnectionString(provider)
            : connectionString;

        builder.AddInterceptors(new AuditingSaveChangesInterceptor(services));
        builder.AddInterceptors(new DomainEventPublishingInterceptor(
            services,
            services?.GetService<Microsoft.Extensions.Logging.ILogger<DomainEventPublishingInterceptor>>()));
        return DbFactory.Create(system).ConfigureDbContext(builder, conn, enableRetryOnFailure);
    }
}
