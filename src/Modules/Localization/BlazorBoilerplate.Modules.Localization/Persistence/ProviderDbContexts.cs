using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Modules.Localization.Persistence;

public static class LocalizationDbContextOptionsExtensions
{
    public static DbContextOptionsBuilder UseLocalizationProvider(
        this DbContextOptionsBuilder builder,
        IServiceProvider? services = null,
        string? provider = null,
        string? connectionString = null,
        bool enableRetryOnFailure = false)
        => builder.UsePlatformDbProvider(services, provider, connectionString, enableRetryOnFailure);
}

public sealed class PostgreSqlLocalizationDbContext(DbContextOptions<PostgreSqlLocalizationDbContext> options) : LocalizationDbContext(options);
public sealed class MySqlLocalizationDbContext(DbContextOptions<MySqlLocalizationDbContext> options) : LocalizationDbContext(options);
public sealed class SqlServerLocalizationDbContext(DbContextOptions<SqlServerLocalizationDbContext> options) : LocalizationDbContext(options);
public sealed class SqliteLocalizationDbContext(DbContextOptions<SqliteLocalizationDbContext> options) : LocalizationDbContext(options);

internal sealed class PostgreSqlLocalizationDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<PostgreSqlLocalizationDbContext>(DbSystemType.PostgreSql);
internal sealed class MySqlLocalizationDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<MySqlLocalizationDbContext>(DbSystemType.MySql);
internal sealed class SqlServerLocalizationDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<SqlServerLocalizationDbContext>(DbSystemType.SqlServer);
internal sealed class SqliteLocalizationDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<SqliteLocalizationDbContext>(DbSystemType.Sqlite);

/// <summary>
/// Localization 模块多数据库迁移入口，委托给公共 <see cref="ModuleDbMigrator"/> 执行。
/// </summary>
public static class LocalizationMigrationHelper
{
    public static Task MigrateAsync(
        string? provider = null,
        string? connectionString = null,
        CancellationToken cancellationToken = default)
        => ModuleDbMigrator.MigrateAsync<
            PostgreSqlLocalizationDbContext,
            MySqlLocalizationDbContext,
            SqlServerLocalizationDbContext,
            SqliteLocalizationDbContext>(provider, connectionString, cancellationToken);
}

