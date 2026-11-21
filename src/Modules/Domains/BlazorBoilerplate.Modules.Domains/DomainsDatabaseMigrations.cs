using BlazorBoilerplate.ModuleAbstractions;

namespace BlazorBoilerplate.Modules.Domains;

public sealed class DomainsDatabaseMigrations : IModuleDatabaseMigrations
{
    public Task EnsureSchemaAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        DomainsMigrationHelper.MigrateAsync(cancellationToken: cancellationToken);

    public IReadOnlyList<ModuleMigrationStep> Steps => [];
}

