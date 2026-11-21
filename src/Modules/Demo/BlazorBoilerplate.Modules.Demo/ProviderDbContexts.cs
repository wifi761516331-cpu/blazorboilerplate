using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Modules.Demo;

public sealed class PostgreSqlDemoDbContext(DbContextOptions<PostgreSqlDemoDbContext> options) : DemoDbContext(options);
public sealed class MySqlDemoDbContext(DbContextOptions<MySqlDemoDbContext> options) : DemoDbContext(options);
public sealed class SqlServerDemoDbContext(DbContextOptions<SqlServerDemoDbContext> options) : DemoDbContext(options);
public sealed class SqliteDemoDbContext(DbContextOptions<SqliteDemoDbContext> options) : DemoDbContext(options);

internal sealed class PostgreSqlDemoDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<PostgreSqlDemoDbContext>(DbSystemType.PostgreSql);
internal sealed class MySqlDemoDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<MySqlDemoDbContext>(DbSystemType.MySql);
internal sealed class SqlServerDemoDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<SqlServerDemoDbContext>(DbSystemType.SqlServer);
internal sealed class SqliteDemoDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<SqliteDemoDbContext>(DbSystemType.Sqlite);

/// <summary>
/// Demo 模块多数据库迁移入口，委托给公共 <see cref="ModuleDbMigrator"/> 执行四类数据库各自对应的 EF Core Migrations。
/// </summary>
public static class DemoMigrationHelper
{
    public static Task MigrateAsync(
        string? provider = null,
        string? connectionString = null,
        CancellationToken cancellationToken = default)
        => ModuleDbMigrator.MigrateAsync<
            PostgreSqlDemoDbContext,
            MySqlDemoDbContext,
            SqlServerDemoDbContext,
            SqliteDemoDbContext>(provider, connectionString, cancellationToken);
}

