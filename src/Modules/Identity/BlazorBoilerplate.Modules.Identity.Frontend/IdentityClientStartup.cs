using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Identity.Widgets;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Identity;

/// <summary>Identity 模块前端启动器：向首页插槽注册用户统计卡。</summary>
public sealed class IdentityClientStartup : IModuleClientStartup
{
    public int Order => 150;

    public void ConfigureServices(IServiceCollection services)
    {
        WidgetZoneRegistry.Register("home_stats", typeof(IdentityUserStatWidget), "identity", order: 300, requiredPermission: "identity.user.read");
    }
}
