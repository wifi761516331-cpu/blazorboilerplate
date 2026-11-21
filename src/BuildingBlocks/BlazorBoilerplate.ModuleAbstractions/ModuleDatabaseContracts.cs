namespace BlazorBoilerplate.ModuleAbstractions;

/// <summary>
/// 模块数据库声明（manifest.json 的 database 段）。
/// 模块加载时由宿主的 ModuleMigrationRunner 基于 EF Core 执行增量迁移/种子：
/// 只按 Sequence 递增应用未应用过的步骤，版本降级/卸载时绝不回滚或删表。
/// </summary>
public sealed class ModuleDatabaseManifest
{
    /// <summary>是否启用模块数据库迁移（纯内存模块保持 false）。</summary>
    public bool Enabled { get; set; }

    /// <summary>迁移目标连接标识；空表示平台默认连接（PlatformConnectionState）。</summary>
    public string? ConnectionKey { get; set; }
}

/// <summary>
/// 模块在 DI 中注册本接口，提供基于 EF Core 的单调递增迁移/种子步骤。
/// 已应用的 Sequence 持久化在 ModuleMigrationHistory 表，降级后不会重放也不会倒退。
/// </summary>
public interface IModuleDatabaseMigrations
{
    /// <summary>
    /// 每次模块加载时先执行一次 EF Core 表结构迁移（由 __EFMigrationsHistory 保证幂等），
    /// 新增 EF 迁移文件后无需再手动追加 Steps 序号。
    /// </summary>
    Task EnsureSchemaAsync(IServiceProvider services, CancellationToken cancellationToken) => Task.CompletedTask;

    IReadOnlyList<ModuleMigrationStep> Steps { get; }
}

/// <summary>单个 EF Core 迁移/种子步骤（不再使用原生 SQL 字符串）。</summary>
public sealed record ModuleMigrationStep(
    int Sequence,
    string Name,
    Func<IServiceProvider, CancellationToken, Task> ExecuteAsync);

/// <summary>新租户创建后的业务种子数据初始化契约（各模块可注册实现）。</summary>
public interface ITenantProvisioningHandler
{
    Task OnTenantCreatedAsync(Guid tenantId, string tenantName, CancellationToken cancellationToken);
}
