using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.AspNetCore.SignalR;

namespace BlazorBoilerplate.ModuleRuntime.Realtime;

/// <summary>基于 IHubContext&lt;AppHub&gt; 的实时推送实现。</summary>
internal sealed class AppHubNotifier(IHubContext<AppHub> hub) : IAppHubNotifier
{
    public Task SendToAllAsync(string method, object? arg = null, CancellationToken ct = default) =>
        hub.Clients.All.SendAsync(method, arg, ct);

    public Task SendToGroupAsync(string groupName, string method, object? arg = null, CancellationToken ct = default) =>
        hub.Clients.Group(groupName).SendAsync(method, arg, ct);

    public Task SendToGroupsAsync(IEnumerable<string> groupNames, string method, object? arg = null, CancellationToken ct = default) =>
        hub.Clients.Groups(groupNames.ToArray()).SendAsync(method, arg, ct);

    public Task SendToUserAsync(string userId, string method, object? arg = null, CancellationToken ct = default) =>
        hub.Clients.Group(AppHubRoutes.UserGroup(userId)).SendAsync(method, arg, ct);

    public Task SendToSessionAsync(Guid sessionId, string method, object? arg = null, CancellationToken ct = default) =>
        hub.Clients.Group(AppHubRoutes.SessionGroup(sessionId)).SendAsync(method, arg, ct);
}

