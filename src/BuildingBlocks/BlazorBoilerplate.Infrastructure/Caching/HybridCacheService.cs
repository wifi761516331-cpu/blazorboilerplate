using BlazorBoilerplate.Application.Abstractions;

namespace BlazorBoilerplate.Infrastructure.Caching;

/// <summary>
/// L1 = 进程内 IMemoryCache，L2 = Redis。Redis 未配置时 L2 全部 no-op，行为等价于纯内存缓存。
/// 宿主单例持有的 L1 仍然只能放共享契约/DTO/基础类型，不能放模块 ALC 内部对象。
/// </summary>
public sealed class HybridCacheService(
    MemoryCacheService l1,
    IRedisCacheStore l2,
    KeyedLockPool? locks = null) : ICacheService
{
    private readonly KeyedLockPool _locks = locks ?? new KeyedLockPool();

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        ValidateKey(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (l1.TryGet(key, out T? local))
            return local;

        var remote = await l2.TryGetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (!remote.Hit)
            return default;

        await l1.SetWithOptionsAsync(key, remote.Value, remote.Options, cancellationToken).ConfigureAwait(false);
        return remote.Value;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl, string? tag, CancellationToken cancellationToken)
    {
        ValidateKey(key);
        cancellationToken.ThrowIfCancellationRequested();

        var options = new CacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl,
            Tag = tag
        };
        await SetCoreAsync(key, value, options, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        ValidateKey(key);
        cancellationToken.ThrowIfCancellationRequested();

        await l1.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        await l2.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveByTagAsync(string tag, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        cancellationToken.ThrowIfCancellationRequested();

        await l1.RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);
        await l2.RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);
    }

    public async Task<T?> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory,
        CacheEntryOptions? options, CancellationToken cancellationToken)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(factory);
        cancellationToken.ThrowIfCancellationRequested();

        if (l1.TryGet(key, out T? hit))
            return hit;

        var remote = await l2.TryGetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (remote.Hit)
        {
            await l1.SetWithOptionsAsync(key, remote.Value, remote.Options, cancellationToken).ConfigureAwait(false);
            return remote.Value;
        }

        using var keyLock = await _locks.LockAsync(key, cancellationToken).ConfigureAwait(false);

        if (l1.TryGet(key, out T? second))
            return second;

        remote = await l2.TryGetAsync<T>(key, cancellationToken).ConfigureAwait(false);
        if (remote.Hit)
        {
            await l1.SetWithOptionsAsync(key, remote.Value, remote.Options, cancellationToken).ConfigureAwait(false);
            return remote.Value;
        }

        var value = await factory(cancellationToken).ConfigureAwait(false);
        if (value is not null || options?.CacheNullValues == true)
            await SetCoreAsync(key, value, options, cancellationToken).ConfigureAwait(false);
        return value;
    }

    private async Task SetCoreAsync<T>(string key, T value, CacheEntryOptions? options,
        CancellationToken cancellationToken)
    {
        await l1.SetWithOptionsAsync(key, value, options, cancellationToken).ConfigureAwait(false);
        await l2.SetAsync(key, value, options, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateKey(string key) => ArgumentException.ThrowIfNullOrWhiteSpace(key);
}
