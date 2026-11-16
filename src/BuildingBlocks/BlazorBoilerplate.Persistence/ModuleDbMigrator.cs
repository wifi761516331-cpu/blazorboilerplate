using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Persistence;

/// <summary>
/// 多数据库模块迁移执行器：根据当前 <see cref="DbSystemType"/> 选择对应的 Provider 专属 DbContext 子类并执行 EF Core MigrateAsync。
/// </summary>
public static class ModuleDbMigrator
{
    public static Task MigrateAsync<TPg, TMySql, TSqlServer, TSqlite>(
        string? provider = null,
        string? connectionString = null,
        CancellationToken cancellationToken = default)
        where TPg : DbContext
        where TMySql : DbContext
        where TSqlServer : DbContext
        where TSqlite : DbContext
    {
        var system = PlatformConnectionState.ResolveSystemType(provider);
        var conn = string.IsNullOrWhiteSpace(connectionString)
            ? PlatformConnectionState.ResolveConnectionString(provider)
            : connectionString;
        var factory = DbFactory.Create(system);

        return system switch
        {
            DbSystemType.PostgreSql => MigrateCoreAsync<TPg>(factory, conn, cancellationToken),
            DbSystemType.MySql => MigrateCoreAsync<TMySql>(factory, conn, cancellationToken),
            DbSystemType.SqlServer => MigrateCoreAsync<TSqlServer>(factory, conn, cancellationToken),
            DbSystemType.Sqlite => MigrateCoreAsync<TSqlite>(factory, conn, cancellationToken),
            _ => throw new NotSupportedException($"Unsupported DbSystemType: {system}")
        };
    }

    private static async Task MigrateCoreAsync<TContext>(
        DbFactory factory,
        string connectionString,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        factory.ConfigureDbContext(builder, connectionString, enableRetryOnFailure: true);

        await using var db = (TContext)(Activator.CreateInstance(typeof(TContext), builder.Options)
            ?? throw new InvalidOperationException($"Failed to instantiate {typeof(TContext).Name}."));
        await db.Database.MigrateAsync(cancellationToken);
    }
}
