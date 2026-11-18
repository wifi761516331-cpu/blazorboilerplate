using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Localization.Application;
using BlazorBoilerplate.Modules.Localization.Infrastructure;
using BlazorBoilerplate.Modules.Localization.Persistence;
using BlazorBoilerplate.Modules.Localization.Presentation;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Localization;

/// <summary>
/// 本地化管理模块入口（动态模块，支持安装/卸载，依赖宿主 ILocalizationStoreBridge 自动接入平台）。
/// </summary>
public sealed class LocalizationModule : IModule
{
    public const string ModuleId = "localization";

    public void ConfigureServices(IServiceCollection services, IModuleContext context)
    {
        services.AddDbContext<LocalizationDbContext>((sp, o) => o.UseLocalizationProvider(sp));
        services.AddSingleton<IModuleDatabaseMigrations, LocalizationDatabaseMigrations>();

        // 宿主内置词典注册中心透传给模块（用于初始同步、重置默认值与手动补全）
        if (context.HostServices.GetService<ILocalizationRegistry>() is { } registry)
            services.AddSingleton(registry);

        services.AddSingleton<LocalizationStoreCache>();
        services.AddSingleton<ILocalizationCacheInvalidator>(sp => sp.GetRequiredService<LocalizationStoreCache>());
        services.AddScoped<ILocalizationStore, EfLocalizationStore>();
        services.AddScoped<ILocalizationRepository, EfLocalizationRepository>();
        services.AddScoped<LocalizationAdminService>();
    }

    public void MapEndpoints(IModuleEndpointBuilder builder, IServiceProvider moduleServices)
    {
        var endpoints = new LocalizationAdminEndpoints(moduleServices);
        endpoints.Map(builder);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAcceptingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DrainAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

