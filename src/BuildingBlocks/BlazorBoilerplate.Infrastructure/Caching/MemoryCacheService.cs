using System.Collections.Concurrent;
using BlazorBoilerplate.Application.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace BlazorBoilerplate.Infrastructure.Caching;

/// <summary>
/// 基于 IMemoryCache 的单进程缓存实现（HybridCacheService 的 L1）。
/// <para>
/// 提供 per-key 锁、tag 失效与绝对/滑动过期；Redis L2 与跨节点失效由 HybridCacheService 组合。
/// </para>
/// <para>
/// ALC 安全约定：注入的 value 必须是共享契约程序集（Application / ModuleAbstractions / Domain）
/// 中的类型或基础类型。缓存由宿主单例持有，任何模块 ALC 内部类型被缓存都会成为 GC root，
/// 阻止 collectible ALC 卸载——直接破坏模块热插拔。
/// </para>
/// </summary>
public sealed class MemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly KeyedLockPool _locks;

    // tag → key 集合。SetAsync 时维护，RemoveByTagAsync 时按集合精确删除（优于正则遍历）。
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _tagIndex = new(StringComparer.Ordinal);

    // key → 当前 tag，换 tag 时用于从旧 tag 索引摘除，避免陈旧条目残留。
    private readonly ConcurrentDictionary<string, string> _keyTags = new(StringComparer.Ordinal);

    public MemoryCacheService(IMemoryCache cache, KeyedLockPool? locks = null)
    {
        _cache = cache;
        _locks = locks ?? new KeyedLockPool();
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        _cache.TryGetValue(key, out T? value);
        return Task.FromResult(value);
    }

    public bool TryGet<T>(string key, out T? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _cache.TryGetValue(key, out value);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl, string? tag, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        SetCore(key, value, ttl, tag);
        return Task.CompletedTask;
    }

    public Task SetWithOptionsAsync<T>(string key, T value, CacheEntryOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        SetCore(key, value, null, options?.Tag, options);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveLocal(key);
        return Task.CompletedTask;
    }

    public Task RemoveByTagAsync(string tag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveLocalByTag(tag);
        return Task.CompletedTask;
    }

    public void RemoveLocal(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        RemoveCore(key);
    }

    public void RemoveLocalByTag(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        if (_tagIndex.TryRemove(tag, out var keys))
        {
            foreach (var key in keys.Keys)
            {
                _cache.Remove(key);
                _keyTags.TryRemove(key, out _);
            }
        }
    }

    public async Task<T?> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory,
        CacheEntryOptions? options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        cancellationToken.ThrowIfCancellationRequested();

        // 快路径：已命中直接返回，不进锁。
        if (_cache.TryGetValue(key, out T? hit))
            return hit;

        using var keyLock = await _locks.LockAsync(key, cancellationToken).ConfigureAwait(false);

        // 二次检查：等待锁期间可能已被其他调用者填充。
        if (_cache.TryGetValue(key, out T? second))
            return second;

        var value = await factory(cancellationToken).ConfigureAwait(false);
        if (value is not null || options?.CacheNullValues == true)
            SetCore(key, value, options?.AbsoluteExpirationRelativeToNow, options?.Tag, options);

        return value;
    }

    private void SetCore<T>(string key, T value, TimeSpan? ttl, string? tag, CacheEntryOptions? options = null)
    {
        TimeSpan? absolute = ttl;
        TimeSpan? sliding = null;
        if (options is not null)
        {
            absolute = options.AbsoluteExpirationRelativeToNow;
            sliding = options.SlidingExpiration;
            tag = options.Tag;
        }

        var entryOptions = new MemoryCacheEntryOptions();
        if (absolute is { } abs) entryOptions.AbsoluteExpirationRelativeToNow = abs;
        if (sliding is { } slide) entryOptions.SlidingExpiration = slide;

        // 换 tag 时从旧 tag 索引摘除，避免已失效条目残留在新 tag 下。
        if (_keyTags.TryGetValue(key, out var oldTag) && !string.Equals(oldTag, tag, StringComparison.Ordinal))
        {
            if (_tagIndex.TryGetValue(oldTag, out var oldSet))
                oldSet.TryRemove(key, out _);
        }

        // TTL 过期 / 替换 / 手动删除都会触发回调，同步清理 tag 索引——
        // 否则过期 key 在 _tagIndex / _keyTags 中永久残留（长时间运行的内存泄漏）。
        entryOptions.RegisterPostEvictionCallback(static (_, _, _, state) =>
        {
            if (state is not EvictionState s) return;
            // 仅当当前记录仍指向被淘汰条目的 tag 时才清理，避免并发写入误删新 tag。
            if (s.Service._keyTags.TryGetValue(s.Key, out var current)
                && string.Equals(current, s.Tag, StringComparison.Ordinal))
            {
                s.Service._keyTags.TryRemove(s.Key, out _);
                if (s.Service._tagIndex.TryGetValue(s.Tag, out var set))
                    set.TryRemove(s.Key, out _);
            }
        }, tag is null ? null : new EvictionState(this, key, tag));

        _cache.Set(key, value!, entryOptions);

        if (tag is not null)
        {
            var set = _tagIndex.GetOrAdd(tag, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
            set[key] = 0;
            _keyTags[key] = tag;
        }
        else
        {
            _keyTags.TryRemove(key, out _);
        }
    }

    private void RemoveCore(string key) => _cache.Remove(key);

    private sealed record EvictionState(MemoryCacheService Service, string Key, string Tag);
}
