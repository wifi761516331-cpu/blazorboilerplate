namespace BlazorBoilerplate.Application.Platform;

/// <summary>统一工作上下文（收敛当前用户、租户、语言与权限信息）。</summary>
public interface IWorkContext
{
    /// <summary>是否已认证。</summary>
    bool IsAuthenticated { get; }

    /// <summary>当前用户 ID。</summary>
    Guid? UserId { get; }

    /// <summary>当前用户名。</summary>
    string? UserName { get; }

    /// <summary>当前租户 ID。</summary>
    Guid? TenantId { get; }

    /// <summary>当前界面区域文化代码。</summary>
    string Culture { get; }

    /// <summary>当前角色列表。</summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>当前权限码集合。</summary>
    IReadOnlyCollection<string> Permissions { get; }
}

