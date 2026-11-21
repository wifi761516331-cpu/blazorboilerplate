using BlazorBoilerplate.Application.Platform;
using BlazorBoilerplate.Application.Search;
using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Demo;

/// <summary>
/// 模块安装 / 跨版本升级 / 彻底卸载业务钩子示例。
/// 运行时会在首次安装、版本升级、彻底卸载（/api/modules/demo/uninstall）时自动触发。
/// </summary>
public sealed class DemoLifecycleHooks : IModuleLifecycleHooks
{
    public const string WelcomeTemplateName = "Demo.Welcome";

    public async Task InstallAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        if (context.HostServices.GetService<ISettingService>() is { } settings)
        {
            await settings.SaveSettingAsync(new DemoSettings(), cancellationToken: cancellationToken);
        }

        if (context.HostServices.GetService<IMessageTemplateService>() is { } templates)
        {
            await templates.SaveAsync(new MessageTemplateItem(
                Name: WelcomeTemplateName,
                Subject: "欢迎使用 %ModuleName% (v%Version%)",
                Body: "您好 %UserName%，模块 %ModuleName% 已就绪。",
                IsActive: true,
                UpdatedAtUtc: DateTimeOffset.UtcNow), cancellationToken);
        }

        if (context.HostServices.GetService<IActivityLogService>() is { } audit)
        {
            await audit.LogAsync(
                "Demo.Install",
                $"Demo 模块 v{context.Version} 首次安装初始化完成。",
                entityName: "Module",
                entityId: context.ModuleId,
                cancellationToken: cancellationToken);
        }
    }

    public async Task UpdateAsync(
        IModuleContext context,
        string previousVersion,
        string targetVersion,
        CancellationToken cancellationToken)
    {
        if (context.HostServices.GetService<IActivityLogService>() is { } audit)
        {
            await audit.LogAsync(
                "Demo.Update",
                $"Demo 模块从 v{previousVersion} 升级到 v{targetVersion}。",
                entityName: "Module",
                entityId: context.ModuleId,
                cancellationToken: cancellationToken);
        }
    }

    public async Task UninstallAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        if (context.HostServices.GetService<ISettingService>() is { } settings)
        {
            await settings.DeleteSettingAsync<DemoSettings>(cancellationToken: cancellationToken);
        }

        if (context.HostServices.GetService<ISearchService>() is { } search)
        {
            await search.RemoveAsync("demo", "demo-doc-1", cancellationToken);
        }

        if (context.HostServices.GetService<IActivityLogService>() is { } audit)
        {
            await audit.LogAsync(
                "Demo.Uninstall",
                $"Demo 模块 v{context.Version} 已彻底卸载并完成清理。",
                entityName: "Module",
                entityId: context.ModuleId,
                cancellationToken: cancellationToken);
        }
    }
}

