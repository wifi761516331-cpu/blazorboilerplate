using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Domains;

public sealed class DomainsModule : IModule
{
    public const string ModuleId = "domains";

    public void ConfigureServices(IServiceCollection services, IModuleContext context)
    {
        services.AddDbContext<DomainsDbContext>((sp, o) => o.UsePlatformDbProvider(sp));
        services.AddSingleton<IModuleDatabaseMigrations, DomainsDatabaseMigrations>();
        services.AddScoped<DomainsService>();
    }

    public void MapEndpoints(IModuleEndpointBuilder builder, IServiceProvider moduleServices)
    {
        new DomainsEndpoints(moduleServices).Map(builder);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAcceptingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DrainAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

