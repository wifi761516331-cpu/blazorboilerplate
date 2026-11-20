using System.Collections.Concurrent;
using BlazorBoilerplate.Application.Platform;
using StackExchange.Redis;

namespace BlazorBoilerplate.Infrastructure.Caching;

/// <summary>
/// 分布式互斥锁服务：已配置 Redis 时使用 Redis SET NX PX + Lua 原子释放；
/// 未配置 Redis 时自动退化为本机进程内锁，单机/集群无缝兼容。
/// </summary>
public sealed class RedisDistributedLockService(RedisConnectionProvider? redis = null) : IDistributedLockService
{
    private const string ReleaseScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _localLocks = new(StringComparer.Ordinal);

    public async Task<IDistributedLockHandle?> TryAcquireAsync(
        string resourceKey,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);

        if (redis is not null && !string.IsNullOrWhiteSpace(RedisCacheState.ConnectionString))
        {
            var db = await redis.GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
            var token = Guid.NewGuid().ToString("N");
            var acquired = await db.StringSetAsync(
                resourceKey,
                token,
                expiry,
                When.NotExists).ConfigureAwait(false);
            if (!acquired)
                return null;

            return new RedisLockHandle(db, resourceKey, token);
        }

        var sem = _localLocks.GetOrAdd(resourceKey, _ => new SemaphoreSlim(1, 1));
        if (!await sem.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return null;

        return new LocalLockHandle(resourceKey, sem);
    }

    private sealed class RedisLockHandle(IDatabase db, string key, string token) : IDistributedLockHandle
    {
        public string ResourceKey => key;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await db.ScriptEvaluateAsync(ReleaseScript, [key], [token]).ConfigureAwait(false);
            }
            catch
            {
                // 锁已过期或 Redis 断开，等待 TTL 自然释放
            }
        }
    }

    private sealed class LocalLockHandle(string key, SemaphoreSlim semaphore) : IDistributedLockHandle
    {
        private int _released;
        public string ResourceKey => key;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
