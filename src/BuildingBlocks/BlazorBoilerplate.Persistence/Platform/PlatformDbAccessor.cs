using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace BlazorBoilerplate.Persistence.Platform;

/// <summary>
/// 平台库连接与自动建表助手：仅在安装向导完成配置后启用；首次访问时幂等创建缺失的平台表。
/// </summary>
public static class PlatformDbAccessor
{
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);
    private static volatile bool _schemaEnsured;

    public static bool IsReady => HasConfiguredConnection();

    private static bool HasConfiguredConnection() => PlatformConnectionState.ResolveSystemType() switch
    {
        DbSystemType.Sqlite => !string.IsNullOrWhiteSpace(PlatformConnectionState.Sqlite),
        DbSystemType.MySql => !string.IsNullOrWhiteSpace(PlatformConnectionState.MySql),
        DbSystemType.SqlServer => !string.IsNullOrWhiteSpace(PlatformConnectionState.SqlServer),
        _ => !string.IsNullOrWhiteSpace(PlatformConnectionState.PostgreSQL)
    };

    public static async Task<PlatformDbContext?> CreateReadyDbContextAsync(CancellationToken ct = default)
    {
        if (!HasConfiguredConnection())
            return null;

        var system = PlatformConnectionState.ResolveSystemType();
        var conn = PlatformConnectionState.ResolveConnectionString();
        var builder = new DbContextOptionsBuilder<PlatformDbContext>();
        DbFactory.Create(system).ConfigureDbContext(builder, conn, enableRetryOnFailure: true);

        var db = new PlatformDbContext(builder.Options);
        if (!_schemaEnsured)
        {
            await SchemaLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!_schemaEnsured)
                {
                    await EnsureSchemaAsync(db, ct).ConfigureAwait(false);
                    _schemaEnsured = true;
                }
            }
            finally
            {
                SchemaLock.Release();
            }
        }

        return db;
    }

    private static async Task EnsureSchemaAsync(PlatformDbContext db, CancellationToken ct)
    {
        try
        {
            await db.Settings.AsNoTracking().Take(1).AnyAsync(ct).ConfigureAwait(false);
            await db.ActivityLogs.AsNoTracking().Take(1).AnyAsync(ct).ConfigureAwait(false);
            await db.MessageTemplates.AsNoTracking().Take(1).AnyAsync(ct).ConfigureAwait(false);
            await db.QueuedEmails.AsNoTracking().Take(1).AnyAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            var creator = db.Database.GetService<IRelationalDatabaseCreator>();
            await creator.CreateTablesAsync(ct).ConfigureAwait(false);
        }
    }
}
