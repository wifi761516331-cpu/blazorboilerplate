using BlazorBoilerplate.Modules.Catalog.Application;
using BlazorBoilerplate.Modules.Catalog.Infrastructure;
using BlazorBoilerplate.Modules.Catalog.Presentation;
using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Catalog;

/// <summary>
/// Catalog 模块入口。Host 通过稳定契约 IModule 调用；
/// 该类型在模块自己的 Collectible ALC 中实例化，卸载后随 ALC 回收。
/// </summary>
public sealed class CatalogModule : IModule
{
    public const string ModuleId = "catalog";

    public void ConfigureServices(IServiceCollection services, IModuleContext context)
    {
        services.AddSingleton<IProductRepository, InMemoryProductRepository>();
        services.AddSingleton<ProductService>();
    }

    public void MapEndpoints(IModuleEndpointBuilder builder, IServiceProvider moduleServices)
    {
        var endpoints = new ProductEndpoints(moduleServices.GetRequiredService<ProductService>());
        endpoints.Map(builder);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAcceptingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DrainAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
