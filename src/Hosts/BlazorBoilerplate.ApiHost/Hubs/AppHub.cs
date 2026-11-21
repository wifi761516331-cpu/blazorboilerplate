using BlazorBoilerplate.ApiHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BlazorBoilerplate.ApiHost.Hubs;

/// <summary>实时推送中心：用于停用租户或吊销会话时实时通知客户端踢下线。</summary>
[Authorize]
public sealed class AppHub : Hub
{
    public const string HubPath = "/hubs/app";
    public const string SessionRevokedMethod = "SessionRevoked";

    public static string SessionGroup(Guid sessionId) => $"session:{sessionId:N}";

    public override async Task OnConnectedAsync()
    {
        var sessionId = Context.User?.GetSessionId() ?? Guid.Empty;
        if (sessionId != Guid.Empty)
            await Groups.AddToGroupAsync(Context.ConnectionId, SessionGroup(sessionId));

        await base.OnConnectedAsync();
    }
}

