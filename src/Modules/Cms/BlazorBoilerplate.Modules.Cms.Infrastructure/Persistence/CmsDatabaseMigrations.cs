using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Cms.Infrastructure.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Cms.Infrastructure.Persistence;

public sealed class CmsDatabaseMigrations : IModuleDatabaseMigrations
{
    public async Task EnsureSchemaAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await CmsMigrationHelper.MigrateAsync(cancellationToken: cancellationToken);
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetService<CmsSeedService>();
        if (seeder is not null)
            await seeder.EnsureSeededAsync(cancellationToken);
    }

    public IReadOnlyList<ModuleMigrationStep> Steps => [];
}
