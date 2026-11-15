using BlazorBoilerplate.Shared.Features.Attachments;
using BlazorBoilerplate.Shared.Features.Identity;
using BlazorBoilerplate.Web.Shared.Services;

namespace BlazorBoilerplate.WebHost.Services;

/// <summary>
/// 当前登录用户状态（scoped，随 Blazor 线路）：用户资料、权限码、可见菜单路径。
/// 通过 <see cref="BlazorBoilerplateApiClient.GetCurrentUserAsync"/> 读取；
/// 401 / 异常一律视为未认证，不向调用方抛出。
/// </summary>
public sealed class AccountState(BlazorBoilerplateApiClient api) : ICurrentUserPermissionService
{
    private static readonly IReadOnlySet<string> Empty =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly object _gate = new();
    private Task? _inflight;
    private volatile bool _loaded;

    public CurrentUserDto? CurrentUser { get; private set; }
    public string? AvatarUrl { get; private set; }
    public bool IsAuthenticated => CurrentUser is not null;
    public bool IsAdmin => Permissions.Contains("*");
    public event Action? Changed;
    public IReadOnlySet<string> Permissions { get; private set; } = Empty;
    public IReadOnlySet<string> MenuPaths { get; private set; } = Empty;

    /// <summary>最近一次加载失败的原因（未认证 / ApiHost 不可达），供 UI 内联提示。</summary>
    public string? LoadError { get; private set; }

    /// <summary>含 "*" 通配或精确匹配即放行。</summary>
    public bool HasPermission(string code) =>
        !string.IsNullOrWhiteSpace(code) &&
        (Permissions.Contains("*") || Permissions.Contains(code));

    /// <summary>满足任一权限码或管理员 "*" 通配即放行。</summary>
    public bool HasAnyPermission(params string[] codes)
    {
        if (Permissions.Contains("*")) return true;
        foreach (var code in codes)
        {
            if (!string.IsNullOrWhiteSpace(code) && Permissions.Contains(code))
                return true;
        }
        return false;
    }

    /// <summary>菜单路径白名单；"*" 放行；忽略尾部斜杠与大小写，支持子路由前缀匹配（如 /system/settings/smtp 匹配 /system/settings）。</summary>
    public bool HasMenu(string path)
    {
        if (MenuPaths.Contains("*")) return true;
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (MenuPaths.Contains(path)) return true;
        var normalized = path.TrimEnd('/');
        foreach (var menu in MenuPaths)
        {
            var menuTrimmed = menu.TrimEnd('/');
            if (string.Equals(menuTrimmed, normalized, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrEmpty(menuTrimmed)
                && normalized.StartsWith(menuTrimmed + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 首次调用加载并缓存（缓存「已认证」与「未认证」两种结果），
    /// 并发调用共享同一个请求，避免每个组件都打一次 GetCurrentUser。
    /// </summary>
    public Task EnsureLoadedAsync()
    {
        if (_loaded) return Task.CompletedTask;
        lock (_gate)
        {
            if (_loaded) return Task.CompletedTask;
            return _inflight ??= LoadAndMarkAsync();
        }
    }

    /// <summary>强制重载（切换账号 / 租户后）。</summary>
    public Task RefreshAsync()
    {
        Invalidate();
        return EnsureLoadedAsync();
    }

    /// <summary>登出时清空状态。</summary>
    public void Invalidate()
    {
        lock (_gate) _inflight = null;
        _loaded = false;
        CurrentUser = null;
        AvatarUrl = null;
        Permissions = Empty;
        MenuPaths = Empty;
        LoadError = null;
        Changed?.Invoke();
    }

    public void UpdateCurrentUser(CurrentUserDto user)
    {
        CurrentUser = user;
        Permissions = ToSet(user.Permissions);
        MenuPaths = ToSet(user.MenuPaths);
        LoadError = null;
        Changed?.Invoke();
    }

    public void SetAvatarUrl(string? url)
    {
        AvatarUrl = url;
        Changed?.Invoke();
    }

    private async Task LoadAndMarkAsync()
    {
        try
        {
            await LoadCoreAsync();
        }
        finally
        {
            _loaded = true;
            lock (_gate) _inflight = null;
        }
    }

    private async Task LoadCoreAsync()
    {
        try
        {
            var user = await api.GetCurrentUserAsync();
            CurrentUser = user;
            Permissions = ToSet(user.Permissions);
            MenuPaths = ToSet(user.MenuPaths);
            LoadError = null;
            try
            {
                var list = await api.GetAttachmentsAsync(AttachmentKind.UserProfileImage, user.Id);
                var avatar = list.FirstOrDefault();
                AvatarUrl = avatar is null
                    ? null
                    : api.GetAttachmentUrl(avatar.Id, avatar.Kind, cacheBust: avatar.CreatedAtUtc.ToUnixTimeMilliseconds().ToString());
            }
            catch
            {
                AvatarUrl = null;
            }
        }
        catch (Exception ex)
        {
            CurrentUser = null;
            AvatarUrl = null;
            Permissions = Empty;
            MenuPaths = Empty;
            LoadError = ex.Message;
        }
        finally
        {
            Changed?.Invoke();
        }
    }

    private static IReadOnlySet<string> ToSet(string[]? values) =>
        values is { Length: > 0 }
            ? new HashSet<string>(values, StringComparer.OrdinalIgnoreCase)
            : Empty;
}
