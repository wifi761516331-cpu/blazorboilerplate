using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Modules.Identity.Persistence;

public sealed class PostgreSqlIdentityDbContext(DbContextOptions<PostgreSqlIdentityDbContext> options) : IdentityDbContext(options);
public sealed class MySqlIdentityDbContext(DbContextOptions<MySqlIdentityDbContext> options) : IdentityDbContext(options);
public sealed class SqlServerIdentityDbContext(DbContextOptions<SqlServerIdentityDbContext> options) : IdentityDbContext(options);
public sealed class SqliteIdentityDbContext(DbContextOptions<SqliteIdentityDbContext> options) : IdentityDbContext(options);

internal sealed class PostgreSqlIdentityDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<PostgreSqlIdentityDbContext>(DbSystemType.PostgreSql);
internal sealed class MySqlIdentityDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<MySqlIdentityDbContext>(DbSystemType.MySql);
internal sealed class SqlServerIdentityDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<SqlServerIdentityDbContext>(DbSystemType.SqlServer);
internal sealed class SqliteIdentityDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<SqliteIdentityDbContext>(DbSystemType.Sqlite);

/// <summary>
/// Identity 模块多数据库迁移入口，委托给公共 <see cref="ModuleDbMigrator"/> 执行。
/// </summary>
public static class IdentityMigrationHelper
{
    public static Task MigrateAsync(
        string? provider = null,
        string? connectionString = null,
        CancellationToken cancellationToken = default)
        => ModuleDbMigrator.MigrateAsync<
            PostgreSqlIdentityDbContext,
            MySqlIdentityDbContext,
            SqlServerIdentityDbContext,
            SqliteIdentityDbContext>(provider, connectionString, cancellationToken);
}
