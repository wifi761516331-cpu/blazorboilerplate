using System.Text.Json;
using Microsoft.AspNetCore.SignalR;

namespace BlazorBoilerplate.ModuleAbstractions;

/// <summary>平台 SignalR Hub 路由、方法名与分组命名约定。</summary>
public static class AppHubRoutes
{
    public const string HubPath = "/hubs/app";
    public const string SessionRevokedMethod = "SessionRevoked";

    public static string SessionGroup(Guid sessionId) => $"session:{sessionId:N}";
    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId:N}";
    public static string UserGroup(string userId) => $"user:{userId}";
}

/// <summary>
/// 平台统一实时推送抽象（供宿主与任意动态模块注入使用，无需引用 ApiHost）。
/// </summary>
public interface IAppHubNotifier
{
    Task SendToAllAsync(string method, object? arg = null, CancellationToken ct = default);
    Task SendToGroupAsync(string groupName, string method, object? arg = null, CancellationToken ct = default);
    Task SendToGroupsAsync(IEnumerable<string> groupNames, string method, object? arg = null, CancellationToken ct = default);
    Task SendToUserAsync(string userId, string method, object? arg = null, CancellationToken ct = default);
    Task SendToSessionAsync(Guid sessionId, string method, object? arg = null, CancellationToken ct = default);
}

/// <summary>
/// 动态模块扩展 /hubs/app（AppHub）的契约：
/// 模块可实现该接口并在连接建立、断开或客户端调用 InvokeAsync(action, payload) 时介入处理，随模块热加载/卸载自动注册与注销。
/// </summary>
public interface IAppHubExtension
{
    Task OnConnectedAsync(HubCallerContext context, IGroupManager groups, CancellationToken ct = default) => Task.CompletedTask;

    Task OnDisconnectedAsync(HubCallerContext context, Exception? exception, CancellationToken ct = default) => Task.CompletedTask;

    bool CanHandle(string action) => false;

    Task<object?> InvokeAsync(string action, JsonElement? payload, HubCallerContext context, IGroupManager groups, CancellationToken ct = default) =>
        Task.FromResult<object?>(null);
}

/// <summary>AppHub 模块扩展注册表（按 moduleId 隔离，支持热加载/卸载）。</summary>
public interface IAppHubExtensionRegistry
{
    void RegisterModule(string moduleId, IReadOnlyList<IAppHubExtension> extensions);
    void UnregisterModule(string moduleId);
    IReadOnlyList<IAppHubExtension> GetExtensions();
}

