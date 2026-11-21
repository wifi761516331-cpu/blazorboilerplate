using System.Text.Json;
using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.Modules.Demo;

/// <summary>
/// SignalR /hubs/app 模块扩展示例：
/// 模块加载时由 ModuleLifecycleManager 自动扫描注册到 IAppHubExtensionRegistry，卸载时自动注销。
/// 客户端连接 /hubs/app 后可通过 connection.InvokeAsync("InvokeAsync", "demo.ping", ...) 调用本模块扩展动作。
/// </summary>
public sealed class DemoHubExtension(
    IAppHubNotifier notifier,
    ILogger<DemoHubExtension> logger) : IAppHubExtension
{
    public const string DemoGroup = "module:demo";

    public async Task OnConnectedAsync(HubCallerContext context, IGroupManager groups, CancellationToken ct = default)
    {
        await groups.AddToGroupAsync(context.ConnectionId, DemoGroup, ct);
        logger.LogDebug("DemoHubExtension: Connection {ConnectionId} joined {Group}", context.ConnectionId, DemoGroup);
    }

    public bool CanHandle(string action) =>
        action.StartsWith("demo.", StringComparison.OrdinalIgnoreCase);

    public async Task<object?> InvokeAsync(
        string action,
        JsonElement? payload,
        HubCallerContext context,
        IGroupManager groups,
        CancellationToken ct = default)
    {
        if (string.Equals(action, "demo.ping", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                ok = true,
                module = "demo",
                connectionId = context.ConnectionId,
                serverTimeUtc = DateTimeOffset.UtcNow,
                echo = payload
            };
        }

        if (string.Equals(action, "demo.broadcast", StringComparison.OrdinalIgnoreCase))
        {
            await notifier.SendToGroupAsync(DemoGroup, "DemoMessage", new
            {
                from = context.UserIdentifier ?? context.ConnectionId,
                payload,
                sentAtUtc = DateTimeOffset.UtcNow
            }, ct);
            return new { sent = true, group = DemoGroup };
        }

        throw new HubException($"Unknown demo hub action: {action}");
    }
}

