using System.Text.Json;
using System.Threading;
using BlazorBoilerplate.Domain.Events;
using BlazorBoilerplate.Infrastructure.Caching;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BlazorBoilerplate.Infrastructure.Events;

/// <summary>
/// 字符串频道总线（第二层）。默认纯进程内工作，不依赖任何外部组件；
/// 配置了 Redis 时叠加跨节点投递。类名不绑定具体传输技术。
/// <para>进程内强类型事件由第一层 <see cref="IEventPublisher"/> 负责；本类面向"字符串消息"场景
/// （模块私有事件序列化成 JSON 后走这里），消息体始终是字符串，不传递 CLR 对象，不会钉住模块 ALC。</para>
/// <para>发布：先分发给本进程订阅者，再在 Redis 启用时 PUBLISH 给其他节点。</para>
/// <para>接收：首次使用时惰性建立一次 pattern 订阅 <c>bb:evt:*</c>，按 nodeId 过滤自身回投；
/// Redis 配置在实例生命周期内固定，运行期变更需重启宿主。</para>
/// </summary>
public sealed class ChannelBus : IChannelBus, IAsyncDisposable
{
    private const string ChannelPrefix = "bb:evt:";
    private static readonly RedisChannel Pattern = RedisChannel.Pattern(ChannelPrefix + "*");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RedisConnectionProvider _connection;
    private readonly ILogger<ChannelBus> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<string, List<HandlerEntry>> _handlers = new(StringComparer.Ordinal);

    // 惰性订阅：首次 Publish/Subscribe 时建立一次，未配置 Redis 时缓存 null。
    // ExecutionAndPublication 保证并发首次调用只执行一次订阅。
    private readonly Lazy<Task<ISubscriber?>> _subscription;

    public ChannelBus(RedisConnectionProvider connection, ILogger<ChannelBus> logger)
    {
        _connection = connection;
        _logger = logger;
        _subscription = new Lazy<Task<ISubscriber?>>(
            SubscribeToRedisAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task PublishAsync(string channel, string message, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        cancellationToken.ThrowIfCancellationRequested();

        // 1) 本地订阅者立即收到 —— 无 Redis 时这就是全部行为，发布订阅照常可用。
        await DispatchLocalAsync(channel, message).ConfigureAwait(false);

        // 2) 确保跨节点订阅已建立（惰性、仅一次）；未配置 Redis 时为 null。
        var subscriber = await _subscription.Value.ConfigureAwait(false);
        if (subscriber is null)
            return;

        // 3) PUBLISH 给其他节点；本机订阅连接也会收到，靠 nodeId 过滤避免重复。
        try
        {
            var envelope = JsonSerializer.Serialize(new ChannelEnvelope(RedisCacheState.NodeId, message), JsonOptions);
            await subscriber.PublishAsync(RedisChannel.Literal(ChannelPrefix + channel), envelope).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 本地已投递，跨节点失败只记日志，不让事件故障拖垮业务。
            _logger.LogWarning(ex, "Redis channel publish failed; delivered locally only. Channel={Channel}", channel);
        }
    }

    public async Task<IChannelSubscription> SubscribeAsync(string channel,
        Func<string, string, Task> handler, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(handler);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = new HandlerEntry(handler);
        lock (_gate)
        {
            if (!_handlers.TryGetValue(channel, out var list))
                _handlers[channel] = list = [];
            list.Add(entry);
        }

        // 订阅本地 handler 时一并确保跨节点订阅已建立（惰性、仅一次）。
        _ = await _subscription.Value.ConfigureAwait(false);
        return new Subscription(this, channel, entry);
    }

    private async Task<ISubscriber?> SubscribeToRedisAsync()
    {
        if (RedisCacheState.ConnectionString is null)
            return null;

        try
        {
            var subscriber = await _connection.GetSubscriberAsync(CancellationToken.None).ConfigureAwait(false);
            await subscriber.SubscribeAsync(Pattern, OnMessage).ConfigureAwait(false);
            _logger.LogInformation("Channel bus subscribed to Redis. Node={NodeId}", RedisCacheState.NodeId);
            return subscriber;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Channel bus Redis subscribe failed; cross-node delivery disabled for this process.");
            return null;
        }
    }

    private void OnMessage(RedisChannel channel, RedisValue value)
    {
        try
        {
            var json = (string?)value;
            if (string.IsNullOrWhiteSpace(json))
                return;

            var envelope = JsonSerializer.Deserialize<ChannelEnvelope>(json, JsonOptions);
            if (envelope is null || envelope.NodeId == RedisCacheState.NodeId)
                return; // 自身节点已本地分发，跳过 Redis 回投。

            var name = channel.ToString();
            if (!name.StartsWith(ChannelPrefix, StringComparison.Ordinal))
                return;
            var actual = name[ChannelPrefix.Length..];

            _ = DispatchLocalAsync(actual, envelope.Message);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ignored invalid channel message.");
        }
    }

    private async Task DispatchLocalAsync(string channel, string message)
    {
        HandlerEntry[] snapshot;
        lock (_gate)
        {
            if (!_handlers.TryGetValue(channel, out var list))
                return;
            snapshot = list.ToArray();
        }

        foreach (var entry in snapshot)
        {
            try
            {
                await entry.Handler(channel, message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 与强类型总线一致：单个订阅者异常记日志，不影响发布方与其他订阅者。
                _logger.LogError(ex, "Channel handler failed. Channel={Channel}", channel);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription.IsValueCreated && await _subscription.Value.ConfigureAwait(false) is { } subscriber)
        {
            try
            {
                await subscriber.UnsubscribeAsync(Pattern, OnMessage).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Channel bus unsubscribe failed; connection was likely disposed.");
            }
        }
    }

    private sealed record HandlerEntry(Func<string, string, Task> Handler);

    private sealed record ChannelEnvelope(Guid NodeId, string Message);

    private sealed class Subscription(ChannelBus owner, string channel, HandlerEntry entry)
        : IChannelSubscription
    {
        public ValueTask DisposeAsync()
        {
            lock (owner._gate)
            {
                if (owner._handlers.TryGetValue(channel, out var list))
                {
                    list.Remove(entry);
                    if (list.Count == 0)
                        owner._handlers.Remove(channel);
                }
            }
            return ValueTask.CompletedTask;
        }
    }
}
