using System.Security.Claims;
using System.Text.Json;
using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BlazorBoilerplate.ModuleRuntime.Realtime;

/// <summary>
/// 平台统一实时推送中心（挂载于 /hubs/app）：
/// 1) 内置会话/用户/租户基础分组加入（用于实时踢下线与租户级广播）；
/// 2) 支持各动态模块通过实现 IAppHubExtension 热插拔连接钩子与 InvokeAsync 动作分发。
/// </summary>
[Authorize]
public sealed class AppHub(IAppHubExtensionRegistry registry) : Hub
{
    public const string HubPath = AppHubRoutes.HubPath;
    public const string SessionRevokedMethod = AppHubRoutes.SessionRevokedMethod;

    public static string SessionGroup(Guid sessionId) => AppHubRoutes.SessionGroup(sessionId);

    public override async Task OnConnectedAsync()
    {
        var sidRaw = Context.User?.FindFirst("sid")?.Value;
        if (Guid.TryParse(sidRaw, out var sessionId) && sessionId != Guid.Empty)
            await Groups.AddToGroupAsync(Context.ConnectionId, AppHubRoutes.SessionGroup(sessionId));

        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;
        if (!string.IsNullOrWhiteSpace(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, AppHubRoutes.UserGroup(userId));

        var tidRaw = Context.User?.FindFirst("tid")?.Value;
        if (Guid.TryParse(tidRaw, out var tenantId) && tenantId != Guid.Empty)
            await Groups.AddToGroupAsync(Context.ConnectionId, AppHubRoutes.TenantGroup(tenantId));

        foreach (var ext in registry.GetExtensions())
            await ext.OnConnectedAsync(Context, Groups, Context.ConnectionAborted);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var ext in registry.GetExtensions())
            await ext.OnDisconnectedAsync(Context, exception, CancellationToken.None);

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>动态模块扩展动作统一入口：按 IAppHubExtension.CanHandle(action) 分发到对应模块。</summary>
    public async Task<object?> InvokeAsync(string action, JsonElement? payload = null)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw new HubException("Action cannot be empty.");

        foreach (var ext in registry.GetExtensions())
        {
            if (ext.CanHandle(action))
                return await ext.InvokeAsync(action, payload, Context, Groups, Context.ConnectionAborted);
        }

        throw new HubException($"No registered AppHub extension handles action '{action}'.");
    }
}

