using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Modules.Cms.Infrastructure.Persistence;

public sealed class PostgreSqlCmsDbContext(DbContextOptions<PostgreSqlCmsDbContext> options) : CmsDbContext(options);
public sealed class MySqlCmsDbContext(DbContextOptions<MySqlCmsDbContext> options) : CmsDbContext(options);
public sealed class SqlServerCmsDbContext(DbContextOptions<SqlServerCmsDbContext> options) : CmsDbContext(options);
public sealed class SqliteCmsDbContext(DbContextOptions<SqliteCmsDbContext> options) : CmsDbContext(options);

internal sealed class PostgreSqlCmsDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<PostgreSqlCmsDbContext>(DbSystemType.PostgreSql);
internal sealed class MySqlCmsDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<MySqlCmsDbContext>(DbSystemType.MySql);
internal sealed class SqlServerCmsDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<SqlServerCmsDbContext>(DbSystemType.SqlServer);
internal sealed class SqliteCmsDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<SqliteCmsDbContext>(DbSystemType.Sqlite);

public static class CmsMigrationHelper
{
    public static Task MigrateAsync(
        string? provider = null,
        string? connectionString = null,
        CancellationToken cancellationToken = default)
        => ModuleDbMigrator.MigrateAsync<
            PostgreSqlCmsDbContext,
            MySqlCmsDbContext,
            SqlServerCmsDbContext,
            SqliteCmsDbContext>(provider, connectionString, cancellationToken);
}
