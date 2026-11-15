using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Attachments;

/// <summary>
/// 附件管理 UI 模块：
/// 附件基础接口与存储位于 BuildingBlocks + ApiHost；
/// 安装并加载本模块后即提供 /attachments 附件管理前端页面与菜单，卸载后仅保留后端接口。
/// </summary>
public sealed class AttachmentsModule : IModule
{
    public const string ModuleId = "attachments";

    public void ConfigureServices(IServiceCollection services, IModuleContext context) { }

    public void MapEndpoints(IModuleEndpointBuilder builder, IServiceProvider moduleServices) { }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAcceptingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task DrainAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

