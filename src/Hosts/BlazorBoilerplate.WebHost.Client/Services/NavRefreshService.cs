using BlazorBoilerplate.Web.Shared.Services;

namespace BlazorBoilerplate.WebHost.Services;

/// <summary>
/// 导航刷新事件（scoped，随电路）。模块加载 / 卸载 / 上传安装 / 菜单变更后由操作方调用 Notify()，
/// MainLayout 订阅并重建侧边栏导航 —— 使模块菜单随生命周期与配置即时刷新。
/// </summary>
public sealed class NavRefreshService : INavRefreshService
{
    public event Action? OnChanged;

    public void Notify() => OnChanged?.Invoke();
}
