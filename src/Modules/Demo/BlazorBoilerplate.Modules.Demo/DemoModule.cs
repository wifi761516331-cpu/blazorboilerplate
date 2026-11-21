using BlazorBoilerplate.Domain.Events;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.Modules.Demo;

/// <summary>
/// Demo 模块主入口（对应 manifest.json 中的 entryType）。
/// 负责模块核心服务注册、私有 DbContext 与增量迁移注册、主端点映射与运行期生命周期。
/// 分片服务注册可拆分到 <see cref="DemoStartup"/>（实现 <see cref="IModuleStartup"/>）。
/// </summary>
public sealed class DemoModule : IModule
{
    public const string ModuleId = "demo";
    public const string ChannelTopic = "demo.notifications";

    private IServiceProvider? _moduleServices;
    private IChannelSubscription? _channelSubscription;

    public void ConfigureServices(IServiceCollection services, IModuleContext context)
    {
        services.AddDbContext<DemoDbContext>((sp, o) => o.UsePlatformDbProvider(sp));
        services.AddSingleton<IModuleDatabaseMigrations, DemoDatabaseMigrations>();
        services.AddScoped<ITenantProvisioningHandler, DemoTenantProvisioningHandler>();
        services.AddScoped<DemoShowcaseService>();
    }

    public void MapEndpoints(IModuleEndpointBuilder builder, IServiceProvider moduleServices)
    {
        _moduleServices = moduleServices;
        var endpoints = new DemoEndpoints(moduleServices);
        endpoints.Map(builder);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // 3) 跨模块零契约频道广播订阅示例（IChannelBus）
        if (_moduleServices?.GetService<IChannelBus>() is { } channelBus)
        {
            var logger = _moduleServices.GetService<ILogger<DemoModule>>();
            _channelSubscription = await channelBus.SubscribeAsync(
                ChannelTopic,
                (channel, message) =>
                {
                    logger?.LogInformation("DemoModule captured IChannelBus [{Channel}]: {Message}", channel, message);
                    return Task.CompletedTask;
                },
                cancellationToken);
        }
    }

    public Task StopAcceptingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DrainAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channelSubscription is not null)
        {
            await _channelSubscription.DisposeAsync();
            _channelSubscription = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channelSubscription is not null)
        {
            await _channelSubscription.DisposeAsync();
            _channelSubscription = null;
        }
    }
}

