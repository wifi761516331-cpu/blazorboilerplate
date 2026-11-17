using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BlazorBoilerplate.ModuleRuntime.Tests;

/// <summary>
/// MemoryCacheService 行为测试：读写入口、tag 失效、per-key 并发单飞（防击穿）。
/// </summary>
public class CacheServiceTests
{
    private static MemoryCacheService NewService() =>
        new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task SetGet_RoundTripsValue()
    {
        var cache = NewService();
        await cache.SetAsync("k", 42, null, null, default);
        Assert.Equal(42, await cache.GetAsync<int>("k", default));
    }

    [Fact]
    public async Task Get_MissingKey_ReturnsDefault()
    {
        var cache = NewService();
        Assert.Equal(0, await cache.GetAsync<int>("missing", default));
        Assert.Null(await cache.GetAsync<string?>("missing", default));
    }

    [Fact]
    public async Task RemoveByTag_EvictsOnlyTaggedKeys()
    {
        var cache = NewService();
        await cache.SetAsync("u:1", "alice", null, "users", default);
        await cache.SetAsync("u:2", "bob", null, "users", default);
        await cache.SetAsync("r:1", "admin", null, "roles", default);

        await cache.RemoveByTagAsync("users", default);

        Assert.Null(await cache.GetAsync<string?>("u:1", default));
        Assert.Null(await cache.GetAsync<string?>("u:2", default));
        Assert.Equal("admin", await cache.GetAsync<string?>("r:1", default));
    }

    [Fact]
    public async Task SetWithDifferentTag_DetachesFromOldTag()
    {
        var cache = NewService();
        await cache.SetAsync("k", "v1", null, "old", default);
        await cache.SetAsync("k", "v2", null, "new", default);

        await cache.RemoveByTagAsync("old", default);
        Assert.Equal("v2", await cache.GetAsync<string?>("k", default));

        await cache.RemoveByTagAsync("new", default);
        Assert.Null(await cache.GetAsync<string?>("k", default));
    }

    [Fact]
    public async Task GetOrCreate_ConcurrentSameKey_FactoryRunsOnce()
    {
        var cache = NewService();
        var calls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<int> Factory(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            return gate.Task.ContinueWith(_ => 99, ct);
        }

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => cache.GetOrCreateAsync("single", Factory, null, default)))
            .ToArray();

        await Task.Delay(80);
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.Equal(99, r));
    }

    [Fact]
    public async Task GetOrCreate_DifferentKeys_RunInParallel()
    {
        var cache = NewService();
        var barrier = new CountdownEvent(2);

        Task<int> Factory(CancellationToken ct)
        {
            barrier.Signal();
            Assert.True(barrier.Wait(TimeSpan.FromSeconds(3)), "不同 key 不应互相阻塞");
            return Task.FromResult(1);
        }

        var t1 = Task.Run(() => cache.GetOrCreateAsync("a", Factory, null, default));
        var t2 = Task.Run(() => cache.GetOrCreateAsync("b", Factory, null, default));
        await Task.WhenAll(t1, t2);
    }

    [Fact]
    public async Task GetOrCreate_WithOptions_AppliesTag()
    {
        var cache = NewService();
        var options = new CacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
            Tag = "cfg"
        };

        await cache.GetOrCreateAsync("s:title", _ => Task.FromResult("Hello"), options, default);
        await cache.RemoveByTagAsync("cfg", default);

        Assert.Null(await cache.GetAsync<string?>("s:title", default));
    }

    [Fact]
    public async Task RemoveAsync_EvictsSingleKey()
    {
        var cache = NewService();
        await cache.SetAsync("k", "v", null, "t", default);
        await cache.RemoveAsync("k", default);
        Assert.Null(await cache.GetAsync<string?>("k", default));
    }

    [Fact]
    public async Task ExpiredEntry_IsEvictedByMemoryCache()
    {
        var cache = NewService();
        await cache.SetAsync("short", "v", TimeSpan.FromMilliseconds(30), null, default);
        Assert.Equal("v", await cache.GetAsync<string?>("short", default));
        await Task.Delay(120);
        Assert.Null(await cache.GetAsync<string?>("short", default));
    }
}

public class HybridCacheServiceTests
{
    private static (HybridCacheService Cache, FakeRedisStore Store) NewService()
    {
        var l1 = new MemoryCacheService(new MemoryCache(new MemoryCacheOptions()));
        var l2 = new FakeRedisStore();
        return (new HybridCacheService(l1, l2), l2);
    }

    [Fact]
    public async Task Get_L2Hit_FillsL1()
    {
        var (cache, store) = NewService();
        await store.SetAsync("k", "remote", new CacheEntryOptions(), default);

        Assert.Equal("remote", await cache.GetAsync<string>("k", default));

        store.Clear();
        Assert.Equal("remote", await cache.GetAsync<string>("k", default));
    }

    [Fact]
    public async Task GetOrCreate_UsesL2ValueWithoutRunningFactory()
    {
        var (cache, store) = NewService();
        await store.SetAsync("k", 7, new CacheEntryOptions(), default);
        var calls = 0;

        var value = await cache.GetOrCreateAsync("k", _ =>
        {
            calls++;
            return Task.FromResult(9);
        }, null, default);

        Assert.Equal(7, value);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Set_WritesBothLayers()
    {
        var (cache, store) = NewService();
        var options = new CacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
            Tag = "menus"
        };

        await cache.SetAsync("k", "v", options.AbsoluteExpirationRelativeToNow, options.Tag, default);

        var hit = await store.TryGetAsync<string>("k", default);
        Assert.True(hit.Hit);
        Assert.Equal("v", hit.Value);
        Assert.Equal("menus", hit.Options?.Tag);
    }

    [Fact]
    public async Task Remove_ClearsBothLayers()
    {
        var (cache, store) = NewService();
        await cache.SetAsync("k", "v", null, null, default);

        await cache.RemoveAsync("k", default);

        Assert.Null(await cache.GetAsync<string?>("k", default));
        Assert.False((await store.TryGetAsync<string>("k", default)).Hit);
    }

    [Fact]
    public async Task RemoveByTag_ClearsBothLayersOnlyForTaggedKeys()
    {
        var (cache, store) = NewService();
        await cache.SetAsync("a", "1", null, "g", default);
        await cache.SetAsync("b", "2", null, "g", default);
        await cache.SetAsync("c", "3", null, "other", default);

        await cache.RemoveByTagAsync("g", default);

        Assert.Null(await cache.GetAsync<string?>("a", default));
        Assert.Null(await cache.GetAsync<string?>("b", default));
        Assert.Equal("3", await cache.GetAsync<string?>("c", default));
        Assert.False((await store.TryGetAsync<string>("a", default)).Hit);
        Assert.False((await store.TryGetAsync<string>("b", default)).Hit);
        Assert.True((await store.TryGetAsync<string>("c", default)).Hit);
    }

    private sealed class FakeRedisStore : IRedisCacheStore
    {
        private readonly Dictionary<string, (object? Value, CacheEntryOptions? Options)> _values =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _tags =
            new(StringComparer.Ordinal);

        public Task<RedisCacheHit<T>> TryGetAsync<T>(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_values.TryGetValue(key, out var entry) || entry.Value is not T typed)
                return Task.FromResult(default(RedisCacheHit<T>));

            return Task.FromResult(new RedisCacheHit<T>(true, typed, entry.Options));
        }

        public Task SetAsync<T>(string key, T value, CacheEntryOptions? options, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_values.TryGetValue(key, out var old) && old.Options?.Tag is { } oldTag
                && !string.Equals(oldTag, options?.Tag, StringComparison.Ordinal))
            {
                if (_tags.TryGetValue(oldTag, out var oldSet))
                    oldSet.Remove(key);
            }

            _values[key] = (value, options);
            if (options?.Tag is { } tag)
            {
                if (!_tags.TryGetValue(tag, out var set))
                    _tags[tag] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(key);
            }

            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_values.Remove(key, out var removed) && removed.Options?.Tag is { } tag
                && _tags.TryGetValue(tag, out var set))
            {
                set.Remove(key);
            }
            return Task.CompletedTask;
        }

        public Task RemoveByTagAsync(string tag, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_tags.Remove(tag, out var keys))
                return Task.CompletedTask;

            foreach (var key in keys)
                _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task CleanupStaleTagIndexAsync(string tagKey, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Clear()
        {
            _values.Clear();
            _tags.Clear();
        }
    }
}

public class CacheKeyBuilderTests
{
    private readonly ICacheKeyBuilder _builder = new CacheKeyBuilder();

    [Fact]
    public void Global_UsesGlobalScope()
    {
        Assert.Equal("g:settings", _builder.Global("settings"));
    }

    [Fact]
    public void Tenant_IsolatesDifferentTenants()
    {
        var tenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var tenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var a = _builder.Tenant(tenantA, "menus:visible");
        var b = _builder.Tenant(tenantB, "menus:visible");
        var anon = _builder.Tenant(null, "menus:visible");

        Assert.NotEqual(a, b);
        Assert.NotEqual(a, anon);
        Assert.Equal("t:11111111111111111111111111111111:menus:visible", a);
    }

    [Fact]
    public void User_IncludesTenantAndUserScope()
    {
        var tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var user = Guid.Parse("33333333-3333-3333-3333-333333333333");

        Assert.Equal(
            "t:11111111111111111111111111111111:u:33333333333333333333333333333333:profile",
            _builder.User(tenant, user, "profile"));
    }
}

public class CacheNullValueTests
{
    private static MemoryCacheService NewService() =>
        new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task GetOrCreate_NullIsNotCachedByDefault()
    {
        var cache = NewService();
        var calls = 0;

        Task<string?> Factory(CancellationToken _)
        {
            calls++;
            return Task.FromResult<string?>(null);
        }

        Assert.Null(await cache.GetOrCreateAsync("k", Factory, null, default));
        Assert.Null(await cache.GetOrCreateAsync("k", Factory, null, default));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetOrCreate_NullIsCachedWhenEnabled()
    {
        var cache = NewService();
        var calls = 0;

        Task<string?> Factory(CancellationToken _)
        {
            calls++;
            return Task.FromResult<string?>(null);
        }

        var options = new CacheEntryOptions { CacheNullValues = true };
        Assert.Null(await cache.GetOrCreateAsync("k", Factory, options, default));
        Assert.Null(await cache.GetOrCreateAsync("k", Factory, null, default));
        Assert.Equal(1, calls);
    }
}

public class KeyedLockPoolTests
{
    [Fact]
    public async Task Pool_RemovesEntryAfterRelease()
    {
        var pool = new KeyedLockPool();
        Assert.Equal(0, pool.Count);

        var lease = await pool.LockAsync("k", default);
        Assert.Equal(1, pool.Count);

        lease.Dispose();
        Assert.Equal(0, pool.Count);
    }

    [Fact]
    public async Task Pool_SameKeyIsMutuallyExclusive()
    {
        var pool = new KeyedLockPool();
        var enteredSecond = false;

        var first = await pool.LockAsync("k", default);
        var secondTask = Task.Run(async () =>
        {
            using var lease = await pool.LockAsync("k", default);
            enteredSecond = true;
        });

        await Task.Delay(80);
        Assert.False(enteredSecond);

        first.Dispose();
        await secondTask;
        Assert.True(enteredSecond);
        Assert.Equal(0, pool.Count);
    }
}

/// <summary>
/// 真 Redis 集成测试：设置环境变量 BB_TEST_REDIS=localhost:6379 后才会实际执行。
/// 未设置时直接返回，便于 CI/本机无 Redis 时仍跑全量单测。
/// </summary>
public class RedisIntegrationTests
{
    public sealed record CacheItem(string Name, int Value);

    [Fact]
    public async Task LiveRedis_SetGetTagRemove_RoundTrips()
    {
        var connectionString = Environment.GetEnvironmentVariable("BB_TEST_REDIS");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        RedisCacheState.Configure(connectionString);
        try
        {
            using var provider = new RedisConnectionProvider(NullLogger<RedisConnectionProvider>.Instance);
            var store = new RedisCacheStore(provider, NullLogger<RedisCacheStore>.Instance);
            var key = $"it:{Guid.NewGuid():N}";
            var options = new CacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                Tag = "integration-test"
            };

            await store.SetAsync(key, new CacheItem("redis", 42), options, default);

            var hit = await store.TryGetAsync<CacheItem>(key, default);
            Assert.True(hit.Hit);
            Assert.Equal("redis", hit.Value?.Name);
            Assert.Equal(42, hit.Value?.Value);
            Assert.Equal("integration-test", hit.Options?.Tag);

            await store.RemoveByTagAsync("integration-test", default);
            Assert.False((await store.TryGetAsync<CacheItem>(key, default)).Hit);
        }
        finally
        {
            RedisCacheState.Configure(null);
        }
    }
}
