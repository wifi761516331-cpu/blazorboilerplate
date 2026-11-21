using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Modules.Domains;

public sealed class PostgreSqlDomainsDbContext(DbContextOptions<PostgreSqlDomainsDbContext> options) : DomainsDbContext(options);
public sealed class MySqlDomainsDbContext(DbContextOptions<MySqlDomainsDbContext> options) : DomainsDbContext(options);
public sealed class SqlServerDomainsDbContext(DbContextOptions<SqlServerDomainsDbContext> options) : DomainsDbContext(options);
public sealed class SqliteDomainsDbContext(DbContextOptions<SqliteDomainsDbContext> options) : DomainsDbContext(options);

internal sealed class PostgreSqlDomainsDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<PostgreSqlDomainsDbContext>(DbSystemType.PostgreSql);
internal sealed class MySqlDomainsDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<MySqlDomainsDbContext>(DbSystemType.MySql);
internal sealed class SqlServerDomainsDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<SqlServerDomainsDbContext>(DbSystemType.SqlServer);
internal sealed class SqliteDomainsDbContextFactory() : ModuleDesignTimeDbContextFactoryBase<SqliteDomainsDbContext>(DbSystemType.Sqlite);

public static class DomainsMigrationHelper
{
    public static Task MigrateAsync(
        string? provider = null,
        string? connectionString = null,
        CancellationToken cancellationToken = default)
        => ModuleDbMigrator.MigrateAsync<
            PostgreSqlDomainsDbContext,
            MySqlDomainsDbContext,
            SqlServerDomainsDbContext,
            SqliteDomainsDbContext>(provider, connectionString, cancellationToken);
}

