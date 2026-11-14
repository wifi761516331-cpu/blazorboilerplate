namespace BlazorBoilerplate.ModuleAbstractions;

/// <summary>
/// 模块数据库声明（manifest.json 的 database 段）。
/// 模块加载时由宿主的 ModuleMigrationRunner 执行增量迁移：只按 Sequence 递增应用未应用过的步骤，
/// 版本降级/卸载时【绝不】回滚或删表 —— 保证数据库结构只向前演进。
/// </summary>
public sealed class ModuleDatabaseManifest
{
    /// <summary>是否启用模块数据库迁移（纯内存模块保持 false）。</summary>
    public bool Enabled { get; set; }

    /// <summary>迁移目标连接标识；空表示平台默认连接（PlatformConnectionState）。</summary>
    public string? ConnectionKey { get; set; }
}

/// <summary>
/// 模块在 DI 中注册本接口，提供单调递增的增量迁移步骤。
/// 步骤必须是幂等的 CREATE TABLE IF NOT EXISTS / ALTER TABLE ADD COLUMN 等纯增量 DDL，
/// 严禁 DROP/DELETE/回滚；History 表持久化已应用的最大 Sequence，降级后不会重放也不会倒退。
/// </summary>
public interface IModuleDatabaseMigrations
{
    IReadOnlyList<ModuleMigrationStep> Steps { get; }
}

/// <summary>
/// 单个迁移步骤。Sql 为 SQLite 语法（默认），PostgreSql 为 PG 语法（为空时回退 Sql）。
/// 允许多条语句，用 ";" 分隔；runner 逐条执行。
/// </summary>
public sealed record ModuleMigrationStep(
    int Sequence,
    string Name,
    string Sql,
    string? PostgreSql = null);
