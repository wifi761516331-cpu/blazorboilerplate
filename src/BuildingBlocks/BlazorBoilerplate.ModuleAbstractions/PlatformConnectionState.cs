namespace BlazorBoilerplate.ModuleAbstractions;

/// <summary>支持的数据库类型（参考 Smartstore DbSystemType）。</summary>
public enum DbSystemType
{
    PostgreSql,
    MySql,
    SqlServer,
    Sqlite
}

/// <summary>
/// 平台数据库连接状态：安装向导完成后由 SetupInitializer 填充，
/// ApiHost 启动时从 data/blazorboilerplate.settings.json 恢复；模块的 ConfigureServices
/// 在 ALC 中读取同一静态状态（契约程序集解析到默认上下文，实例唯一）。
/// 支持 PostgreSQL、MySQL、MSSQL (SQL Server) 与 SQLite 四种数据库。
/// </summary>
public static class PlatformConnectionState
{
    public const string PostgreSQLProvider = "PostgreSQL";
    public const string MySqlProvider = "MySQL";
    public const string SqlServerProvider = "MSSQL";
    public const string SqliteProvider = "SQLite";

    public static string DatabaseProvider { get; private set; } = PostgreSQLProvider;
    public static string? PostgreSQL { get; private set; }
    public static string? MySql { get; private set; }
    public static string? SqlServer { get; private set; }
    public static string? Sqlite { get; private set; }

    /// <summary>设置数据源。provider 支持 PostgreSQL / MySQL / MSSQL / SQLite（大小写不敏感）。</summary>
    public static void Set(
        string? provider,
        string? postgreSql,
        string? sqlite = null,
        string? mySql = null,
        string? sqlServer = null)
    {
        DatabaseProvider = Normalize(provider);
        PostgreSQL = postgreSql;
        Sqlite = sqlite;
        MySql = mySql;
        SqlServer = sqlServer;
    }

    public static string Normalize(string? provider) => ResolveSystemType(provider) switch
    {
        DbSystemType.Sqlite => SqliteProvider,
        DbSystemType.MySql => MySqlProvider,
        DbSystemType.SqlServer => SqlServerProvider,
        _ => PostgreSQLProvider
    };

    public static DbSystemType ResolveSystemType(string? provider = null)
    {
        var value = (provider ?? DatabaseProvider)?.Trim();
        if (string.Equals(value, SqliteProvider, StringComparison.OrdinalIgnoreCase))
            return DbSystemType.Sqlite;
        if (string.Equals(value, MySqlProvider, StringComparison.OrdinalIgnoreCase))
            return DbSystemType.MySql;
        if (string.Equals(value, SqlServerProvider, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "SqlServer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "SQL Server", StringComparison.OrdinalIgnoreCase))
            return DbSystemType.SqlServer;
        return DbSystemType.PostgreSql;
    }

    /// <summary>模块注册 DbContext 时的最终连接串（未配置时给出保守默认值）。</summary>
    public static string ResolveConnectionString(string? provider = null) => ResolveSystemType(provider) switch
    {
        DbSystemType.Sqlite => string.IsNullOrWhiteSpace(Sqlite)
            ? "Data Source=blazorboilerplate.db"
            : Sqlite!,
        DbSystemType.MySql => string.IsNullOrWhiteSpace(MySql)
            ? "Server=localhost;Port=3306;Database=blazorboilerplate;Uid=root;Pwd=root;"
            : MySql!,
        DbSystemType.SqlServer => string.IsNullOrWhiteSpace(SqlServer)
            ? "Server=localhost;Database=blazorboilerplate;Trusted_Connection=True;MultipleActiveResultSets=True;Encrypt=False;"
            : SqlServer!,
        _ => string.IsNullOrWhiteSpace(PostgreSQL)
            ? "Host=localhost;Port=5432;Database=blazorboilerplate;Username=blazorboilerplate;Password=blazorboilerplate"
            : PostgreSQL!
    };
}
