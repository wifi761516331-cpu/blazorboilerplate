using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Demo;

/// <summary>
/// Demo 模块增量数据库迁移示例（实现 <see cref="IModuleDatabaseMigrations"/>）。
/// 模块加载时由宿主 <c>ModuleMigrationRunner</c> 按 Sequence 递增幂等执行并记录在迁移历史表中。
/// </summary>
public sealed class DemoDatabaseMigrations : IModuleDatabaseMigrations
{
    public Task EnsureSchemaAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        DemoMigrationHelper.MigrateAsync(cancellationToken: cancellationToken);

    public IReadOnlyList<ModuleMigrationStep> Steps { get; } =
    [
        new ModuleMigrationStep(1, "SeedDemoInitialNote", SeedDemoInitialNoteAsync)
    ];

    private static async Task SeedDemoInitialNoteAsync(IServiceProvider sp, CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DemoDbContext>();
        if (!await db.Notes.AnyAsync(ct))
        {
            db.Notes.Add(new DemoNote
            {
                Title = "初始化种子记录（由 DemoDatabaseMigrations Step 2 写入）"
            });
            await db.SaveChangesAsync(ct);
        }
    }
}
