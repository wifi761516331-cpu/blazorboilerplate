using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BlazorBoilerplate.Persistence;

/// <summary>
/// 参考 Smartstore DbFactory 架构：按 DbSystemType 统一封装
/// PostgreSQL / MySQL / MSSQL (SQL Server) / SQLite 四种 EF Core 提供程序配置。
/// </summary>
public abstract class DbFactory
{
    public abstract DbSystemType DbSystem { get; }

    public virtual string NormalizeConnectionString(string connectionString) => connectionString;

    public abstract DbContextOptionsBuilder ConfigureDbContext(
        DbContextOptionsBuilder builder,
        string connectionString,
        bool enableRetryOnFailure = false);

    public static DbFactory Create(DbSystemType system) => system switch
    {
        DbSystemType.MySql => new MySqlDbFactory(),
        DbSystemType.SqlServer => new SqlServerDbFactory(),
        DbSystemType.Sqlite => new SqliteDbFactory(),
        _ => new PostgreSqlDbFactory()
    };
}

internal sealed class PostgreSqlDbFactory : DbFactory
{
    public override DbSystemType DbSystem => DbSystemType.PostgreSql;

    public override DbContextOptionsBuilder ConfigureDbContext(
        DbContextOptionsBuilder builder,
        string connectionString,
        bool enableRetryOnFailure = false) =>
        builder
            .UseNpgsql(connectionString, npgsql =>
            {
                if (enableRetryOnFailure)
                    npgsql.EnableRetryOnFailure();
            })
            .ReplaceService<IMigrationsSqlGenerator, CustomNpgsqlMigrationsSqlGenerator>();
}

internal sealed class MySqlDbFactory : DbFactory
{
    public override DbSystemType DbSystem => DbSystemType.MySql;

    public override DbContextOptionsBuilder ConfigureDbContext(
        DbContextOptionsBuilder builder,
        string connectionString,
        bool enableRetryOnFailure = false) =>
        builder
            .UseMySQL(connectionString)
            .ReplaceService<IMigrationsSqlGenerator, CustomMySqlMigrationsSqlGenerator>();
}

internal sealed class SqlServerDbFactory : DbFactory
{
    public override DbSystemType DbSystem => DbSystemType.SqlServer;

    public override string NormalizeConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        var normalized = connectionString.Trim();
        if (!normalized.Contains("MultipleActiveResultSets=", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.TrimEnd(';') + ";MultipleActiveResultSets=True";
        if (!normalized.Contains("Encrypt=", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.TrimEnd(';') + ";Encrypt=False";
        return normalized;
    }

    public override DbContextOptionsBuilder ConfigureDbContext(
        DbContextOptionsBuilder builder,
        string connectionString,
        bool enableRetryOnFailure = false) =>
        builder
            .UseSqlServer(NormalizeConnectionString(connectionString), sql =>
            {
                if (enableRetryOnFailure)
                    sql.EnableRetryOnFailure();
            })
            .ReplaceService<IMigrationsSqlGenerator, CustomSqlServerMigrationsSqlGenerator>();
}

internal sealed class SqliteDbFactory : DbFactory
{
    public override DbSystemType DbSystem => DbSystemType.Sqlite;

    public override DbContextOptionsBuilder ConfigureDbContext(
        DbContextOptionsBuilder builder,
        string connectionString,
        bool enableRetryOnFailure = false) =>
        builder
            .UseSqlite(connectionString)
            .ReplaceService<IMigrationsSqlGenerator, CustomSqliteMigrationsSqlGenerator>();
}

