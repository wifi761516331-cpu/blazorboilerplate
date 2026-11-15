namespace BlazorBoilerplate.Web.Shared.Services;

/// <summary>
/// 导航刷新通知契约：供模块页面（如菜单管理、模块管理）触发宿主侧边栏即时重建。
/// </summary>
public interface INavRefreshService
{
    void Notify();
}

