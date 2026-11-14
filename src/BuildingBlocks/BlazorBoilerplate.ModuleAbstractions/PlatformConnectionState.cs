namespace BlazorBoilerplate.ModuleAbstractions;

/// <summary>
/// 平台数据库连接状态（批次 2）：安装向导完成后由 SetupInitializer 填充，
/// ApiHost 启动时从 data/blazorboilerplate.settings.json 恢复；模块的 ConfigureServices
/// 在 ALC 中读取同一静态状态（契约程序集解析到默认上下文，实例唯一）。
/// </summary>
public static class PlatformConnectionState
{
    public const string PostgreSQLProvider = "PostgreSQL";
    public const string SqliteProvider = "SQLite";

    public static string DatabaseProvider { get; private set; } = PostgreSQLProvider;
    public static string? PostgreSQL { get; private set; }
    public static string? Sqlite { get; private set; }

    /// <summary>设置数据源。provider 为 "PostgreSQL" 或 "SQLite"（大小写不敏感）。</summary>
    public static void Set(string? provider, string? postgreSql, string? sqlite = null)
    {
        DatabaseProvider = Normalize(provider);
        PostgreSQL = postgreSql;
        Sqlite = sqlite;
    }

    public static string Normalize(string? provider) =>
        string.Equals(provider, SqliteProvider, StringComparison.OrdinalIgnoreCase)
            ? SqliteProvider
            : PostgreSQLProvider;

    /// <summary>模块注册 DbContext 时的最终连接串（未配置时给出保守默认值）。</summary>
    public static string ResolveConnectionString()
    {
        if (DatabaseProvider == SqliteProvider)
            return string.IsNullOrWhiteSpace(Sqlite) ? "Data Source=blazorboilerplate.db" : Sqlite!;
        return string.IsNullOrWhiteSpace(PostgreSQL)
            ? "Host=localhost;Port=5432;Database=blazorboilerplate;Username=blazorboilerplate;Password=blazorboilerplate"
            : PostgreSQL!;
    }
}
