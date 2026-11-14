namespace BlazorBoilerplate.WebHost.Services;

/// <summary>
/// 导航刷新事件（scoped，随电路）。模块加载 / 卸载 / 上传安装后由操作方调用 Notify()，
/// MainLayout 订阅并重建侧边栏导航 —— 使模块菜单随模块生命周期出现 / 消失。
/// </summary>
public sealed class NavRefreshService
{
    public event Action? OnChanged;

    public void Notify() => OnChanged?.Invoke();
}
