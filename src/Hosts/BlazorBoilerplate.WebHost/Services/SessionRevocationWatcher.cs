using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.WebHost.Services;

/// <summary>
/// 监听 ApiHost SignalR AppHub 的 SessionRevoked 事件：
/// 当管理员停用当前租户或吊销会话时，实时通知 Blazor 线路踢出当前用户。
/// </summary>
public sealed class SessionRevocationWatcher(
    IAuthSession session,
    IConfiguration configuration,
    ILogger<SessionRevocationWatcher> logger) : IAsyncDisposable
{
    private HubConnection? _connection;
    private bool _started;

    public event Func<Task>? OnSessionRevoked;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started) return;
        _started = true;

        var token = await session.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return;

        var apiBase = (configuration["ApiHost:BaseUrl"] ?? "http://localhost:5100").TrimEnd('/');
        _connection = new HubConnectionBuilder()
            .WithUrl($"{apiBase}/hubs/app", options =>
            {
                options.AccessTokenProvider = () => session.GetAccessTokenAsync(CancellationToken.None);
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<object>("SessionRevoked", async _ =>
        {
            if (OnSessionRevoked is not null)
                await OnSessionRevoked.Invoke();
        });

        try
        {
            await _connection.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "SignalR SessionRevocationWatcher 连接失败（ApiHost 可能尚未就绪）。");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            try { await _connection.DisposeAsync(); }
            catch { /* 忽略线路关闭时的连接释放异常 */ }
            _connection = null;
        }
    }
}

