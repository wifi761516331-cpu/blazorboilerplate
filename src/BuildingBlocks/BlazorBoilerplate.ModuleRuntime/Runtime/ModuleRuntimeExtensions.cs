using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.ModuleRuntime.Endpoints;
using BlazorBoilerplate.ModuleRuntime.LoadContext;
using BlazorBoilerplate.ModuleRuntime.Runtime;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.ModuleRuntime;

public static class ModuleRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddBlazorBoilerplateModuleRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ModuleRuntimeOptions>()
            .Bind(configuration.GetSection(ModuleRuntimeOptions.SectionName));

        services.AddSingleton<InMemoryModuleAudit>();
        services.AddSingleton<IModuleAudit>(sp => sp.GetRequiredService<InMemoryModuleAudit>());
        services.AddSingleton<ModulePackageManager>();
        services.AddSingleton<ModuleDiscovery>();
        services.AddSingleton<ModuleCatalog>();
        services.AddSingleton<ModuleLoadContextFactory>();
        services.AddSingleton<ModuleEndpointDataSource>();
        services.AddSingleton<ModuleEndpointRegistry>();
        services.AddSingleton<ModuleLifecycleManager>();
        return services;
    }

    /// <summary>
    /// 把可变 EndpointDataSource 加入路由构建器。
    /// UseRouting 会订阅它的变更令牌；Registry 每次原子替换快照后 matcher 会重建。
    /// </summary>
    public static IEndpointRouteBuilder UseModuleEndpointDataSource(this IEndpointRouteBuilder endpoints)
    {
        var dataSource = endpoints.ServiceProvider.GetRequiredService<ModuleEndpointDataSource>();
        endpoints.DataSources.Add(dataSource);
        return endpoints;
    }
}
