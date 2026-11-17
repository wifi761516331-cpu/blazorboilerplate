using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.Infrastructure.Caching;

/// <summary>
/// 低频清理 Redis tag 索引中因 TTL 自然过期残留的成员。多节点同时跑是幂等的。
/// </summary>
public sealed class RedisCacheTagIndexCleaner(
    RedisConnectionProvider connection,
    IRedisCacheStore store,
    ILogger<RedisCacheTagIndexCleaner> logger) : BackgroundService
{
    private const string TagPrefix = "bb:cache:t:";
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (!RedisCacheState.IsEnabled)
                continue;

            try
            {
                var servers = await connection.GetServersAsync(stoppingToken).ConfigureAwait(false);
                foreach (var server in servers)
                {
                    var keys = server.KeysAsync(pattern: TagPrefix + "*", pageSize: 500);
                    await foreach (var key in keys.WithCancellation(stoppingToken).ConfigureAwait(false))
                        await store.CleanupStaleTagIndexAsync(key.ToString(), stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Redis cache tag index cleanup skipped.");
            }
        }
    }
}
