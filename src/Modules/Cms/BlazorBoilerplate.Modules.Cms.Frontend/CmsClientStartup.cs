using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Cms.Frontend;

/// <summary>CMS 前端模块启动器。</summary>
public sealed class CmsClientStartup : IModuleClientStartup
{
    public int Order => 220;

    public void ConfigureServices(IServiceCollection services)
    {
        // CmsApiClient 实现 IClientService，由宿主的 ClientModuleCatalog 自动扫描注册。
    }
}
