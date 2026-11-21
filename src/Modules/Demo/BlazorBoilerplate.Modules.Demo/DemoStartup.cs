using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Demo;

/// <summary>
/// 模块分片启动器示例（实现 <see cref="IModuleStartup"/>）。
/// 运行时会在 <see cref="DemoModule"/> 之后按 <see cref="Order"/> 升序自动扫描并执行。
/// 当模块体量变大时，可按子域拆分成多个 IModuleStartup。
/// </summary>
public sealed class DemoStartup : IModuleStartup
{
    public int Order => 3100;

    public void ConfigureServices(IServiceCollection services, IModuleContext context)
    {
        services.AddSingleton<DemoGreetingProvider>();
    }

    public void MapEndpoints(IModuleEndpointBuilder builder, IServiceProvider moduleServices)
    {
        builder.Map("/api/demo/startup-ping", "GET", "demo.read", async ctx =>
        {
            var provider = moduleServices.GetRequiredService<DemoGreetingProvider>();
            await ctx.Response.WriteAsJsonAsync(new
            {
                source = nameof(DemoStartup),
                order = Order,
                message = provider.GetBanner()
            }, ctx.RequestAborted);
        });
    }
}

public sealed class DemoGreetingProvider
{
    public string GetBanner() => "来自 DemoStartup (Order=3100) 的分片启动响应";
}

