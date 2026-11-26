using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Cms.Application.AI;
using BlazorBoilerplate.Modules.Cms.Application.Events;
using BlazorBoilerplate.Modules.Cms.Application.Services;
using BlazorBoilerplate.Modules.Cms.Domain.Repositories;
using BlazorBoilerplate.Modules.Cms.Infrastructure.Persistence;
using BlazorBoilerplate.Modules.Cms.Infrastructure.Repositories;
using BlazorBoilerplate.Modules.Cms.Infrastructure.Seed;
using BlazorBoilerplate.Persistence;
using Microsoft.AspNetCore.Hosting;
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

        // AI 官网设计器：配置来自 CMS 模块自有的 data/cms/ai.json（data/ 已 gitignore，不入库）。
        // 未配置则回退 Schema 占位器。TryAdd 保留宿主/插件覆盖的扩展点。
        var env = context.HostServices.GetService(typeof(IWebHostEnvironment)) as IWebHostEnvironment;
        var aiOptions = CmsAiOptions.LoadFromFile(
            CmsAiOptions.DefaultPath(env?.ContentRootPath ?? AppContext.BaseDirectory));
        services.AddSingleton(aiOptions);

        if (aiOptions.Enabled)
        {
            services.AddSingleton<ICmsAiChatClient, MeaiCmsAiChatClient>();
            services.TryAddScoped<ICmsAiDesignerService, LlmCmsAiDesignerService>();
        }
        else
        {
            services.TryAddScoped<ICmsAiDesignerService, DefaultCmsAiDesignerService>();
        }
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
