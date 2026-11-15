namespace BlazorBoilerplate.Web.Shared.Services;

/// <summary>
/// 当前登录用户的菜单与按钮权限查询接口（由 WebHost 的 AccountState 实现）。
/// 下沉到 Web.Shared 后，各个业务模块页面（Identity / Catalog / Attachments ...）
/// 可直接注入校验按钮权限与菜单权限，而不反向引用 WebHost。
/// </summary>
public interface ICurrentUserPermissionService
{
    bool IsAuthenticated { get; }
    bool IsAdmin { get; }
    IReadOnlySet<string> Permissions { get; }
    IReadOnlySet<string> MenuPaths { get; }

    bool HasPermission(string code);
    bool HasAnyPermission(params string[] codes);
    bool HasMenu(string path);

    Task EnsureLoadedAsync();
    event Action? Changed;
}

