using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Localization.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Localization.Persistence;

/// <summary>
/// Localization 模块数据库迁移与幂等种子（全部基于 EF Core，自动适配四类数据库）。
/// </summary>
public sealed class LocalizationDatabaseMigrations : IModuleDatabaseMigrations
{
    public Task EnsureSchemaAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        LocalizationMigrationHelper.MigrateAsync(cancellationToken: cancellationToken);

    public IReadOnlyList<ModuleMigrationStep> Steps { get; } =
    [
        new ModuleMigrationStep(1, "EnsureLocalizationSchema", (_, _) => Task.CompletedTask),
        new ModuleMigrationStep(2, "SeedBuiltInLanguages", SeedBuiltInLanguagesAsync),
        new ModuleMigrationStep(3, "SyncInitialRegistryResources", SyncInitialRegistryResourcesAsync)
    ];

    private static async Task SeedBuiltInLanguagesAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<LocalizationDbContext>();
        var existingCultures = await db.Languages
            .Select(l => l.Culture)
            .ToListAsync(ct);
        var set = new HashSet<string>(existingCultures, StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        var added = false;
        foreach (var lang in LocalizationDefaults.BuiltInLanguages)
        {
            if (set.Contains(lang.Culture))
                continue;

            db.Languages.Add(new Language
            {
                Culture = lang.Culture,
                Name = lang.Name,
                DisplayName = lang.DisplayName,
                FlagCode = lang.FlagCode,
                IsRtl = lang.IsRtl,
                IsDefault = lang.IsDefault,
                IsEnabled = lang.IsEnabled,
                DisplayOrder = lang.DisplayOrder,
                CreatedAt = now,
                UpdatedAt = now
            });
            added = true;
        }

        if (added)
            await db.SaveChangesAsync(ct);
    }

    private static async Task SyncInitialRegistryResourcesAsync(IServiceProvider sp, CancellationToken ct)
    {
        var registry = sp.GetService<ILocalizationRegistry>();
        if (registry is null)
            return;

        var store = sp.GetRequiredService<ILocalizationStore>();
        var entriesByModule = registry.GetAllEntries()
            .GroupBy(e => e.ModuleId, StringComparer.OrdinalIgnoreCase);

        foreach (var group in entriesByModule)
            await store.SyncModuleResourcesAsync(group.Key, [.. group], ct);
    }
}

