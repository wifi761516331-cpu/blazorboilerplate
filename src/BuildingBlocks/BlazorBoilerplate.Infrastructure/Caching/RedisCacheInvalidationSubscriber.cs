using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BlazorBoilerplate.Infrastructure.Caching;

/// <summary>
/// 订阅 Redis 失效消息，只清本机 L1。连接串变化时退订旧连接并重新订阅。
/// </summary>
public sealed class RedisCacheInvalidationSubscriber(
    RedisConnectionProvider connection,
    MemoryCacheService l1,
    ILogger<RedisCacheInvalidationSubscriber> logger) : BackgroundService
{
    private const string InvalidationChannel = "bb:cache:invalidate";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RedisChannel _channel = RedisChannel.Literal(InvalidationChannel);

    private ISubscriber? _subscriber;
    private string? _observedConnectionString;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var desired = RedisCacheState.ConnectionString;
            if (string.Equals(desired, _observedConnectionString, StringComparison.Ordinal))
            {
                await DelayAsync(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            await UnsubscribeAsync(stoppingToken).ConfigureAwait(false);
            _observedConnectionString = desired;

            if (desired is null)
            {
                await DelayAsync(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            try
            {
                var subscriber = await connection.GetSubscriberAsync(stoppingToken).ConfigureAwait(false);
                await subscriber.SubscribeAsync(_channel, OnMessage, CommandFlags.None).ConfigureAwait(false);
                _subscriber = subscriber;
                logger.LogInformation("Redis cache invalidation subscriber ready. Node={NodeId}", RedisCacheState.NodeId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Redis cache invalidation subscriber failed; retrying in 5s.");
                await DelayAsync(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        await UnsubscribeAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task UnsubscribeAsync(CancellationToken cancellationToken)
    {
        var subscriber = _subscriber;
        _subscriber = null;
        if (subscriber is null)
            return;

        try
        {
            await subscriber.UnsubscribeAsync(_channel, OnMessage).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Redis cache invalidation unsubscribe failed; connection is likely already disposed.");
        }
    }

    private void OnMessage(RedisChannel channel, RedisValue message)
    {
        try
        {
            var json = (string?)message;
            if (string.IsNullOrWhiteSpace(json))
                return;

            var payload = JsonSerializer.Deserialize<CacheInvalidationMessage>(json, JsonOptions);
            if (payload is null || payload.NodeId == RedisCacheState.NodeId)
                return;

            if (string.Equals(payload.Type, InvalidationKind.Key, StringComparison.Ordinal))
                l1.RemoveLocal(payload.Value);
            else if (string.Equals(payload.Type, InvalidationKind.Tag, StringComparison.Ordinal))
                l1.RemoveLocalByTag(payload.Value);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Ignored invalid Redis cache invalidation message.");
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
