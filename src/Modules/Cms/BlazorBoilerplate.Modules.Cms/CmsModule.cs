using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Cms.Application.AI;
using BlazorBoilerplate.Modules.Cms.Application.Events;
using BlazorBoilerplate.Modules.Cms.Application.Services;
using BlazorBoilerplate.Modules.Cms.Domain.Repositories;
using BlazorBoilerplate.Modules.Cms.Infrastructure.Persistence;
using BlazorBoilerplate.Modules.Cms.Infrastructure.Repositories;
using BlazorBoilerplate.Modules.Cms.Infrastructure.Seed;
using BlazorBoilerplate.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorBoilerplate.Modules.Cms;

/// <summary>
/// CMS 官网内容管理模块入口（DDD 多类库整洁架构：Domain / Application / Infrastructure / Entry）。
/// </summary>
public sealed class CmsModule : IModule
{
    public const string ModuleId = "cms";

    public void ConfigureServices(IServiceCollection services, IModuleContext context)
    {
        services.AddDbContext<CmsDbContext>((sp, o) => o.UsePlatformDbProvider(sp));
        services.AddSingleton<IModuleDatabaseMigrations, CmsDatabaseMigrations>();

        services.AddScoped<ICmsSiteRepository, CmsSiteRepository>();
        services.AddScoped<ICmsLayoutRepository, CmsLayoutRepository>();
        services.AddScoped<ICmsSectionRepository, CmsSectionRepository>();
        services.AddScoped<ICmsWidgetRepository, CmsWidgetRepository>();
        services.AddScoped<ICmsLeadRepository, CmsLeadRepository>();
        services.AddScoped<ICmsUnitOfWork, CmsUnitOfWork>();

        services.AddScoped<CmsSiteAppService>();
        services.AddScoped<CmsLayoutAppService>();
        services.AddScoped<CmsSectionAppService>();
        services.AddScoped<CmsWidgetAppService>();
        services.AddScoped<CmsLeadAppService>();
        services.AddScoped<CmsSeedService>();
        services.AddScoped<CmsCacheInvalidationEventHandler>();
        services.AddScoped<CmsLeadSubmittedEventHandler>();

        // 预留 AI 官网设计器服务：允许宿主或外部插件优先注册自定义 LLM Provider 实现
        services.TryAddScoped<ICmsAiDesignerService, DefaultCmsAiDesignerService>();
    }

    public void MapEndpoints(IModuleEndpointBuilder builder, IServiceProvider moduleServices)
    {
        new CmsEndpoints(moduleServices).Map(builder);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAcceptingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DrainAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
