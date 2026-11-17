using System.Text.Json;
using BlazorBoilerplate.Application.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BlazorBoilerplate.Infrastructure.Caching;

public readonly record struct RedisCacheHit<T>(bool Hit, T? Value, CacheEntryOptions? Options);

/// <summary>
/// Redis L2 存储。所有 value 以 JSON 保存，因此天然只能承载共享契约/DTO/基础类型，
/// 不会把模块 ALC 内部对象写进跨进程缓存。
/// </summary>
public interface IRedisCacheStore
{
    Task<RedisCacheHit<T>> TryGetAsync<T>(string key, CancellationToken cancellationToken);
    Task SetAsync<T>(string key, T value, CacheEntryOptions? options, CancellationToken cancellationToken);
    Task RemoveAsync(string key, CancellationToken cancellationToken);
    Task RemoveByTagAsync(string tag, CancellationToken cancellationToken);
    Task CleanupStaleTagIndexAsync(string tagKey, CancellationToken cancellationToken);
}

public sealed class RedisCacheStore(
    RedisConnectionProvider connection,
    ILogger<RedisCacheStore> logger) : IRedisCacheStore
{
    private const string ValuePrefix = "bb:cache:v:";
    private const string TagPrefix = "bb:cache:t:";
    private const string InvalidationChannel = "bb:cache:invalidate";
    private const string NoTagPlaceholder = "__none__";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // value + tag 索引一次原子写入；旧 tag 从旧 envelope 中解析，避免先 GET 再 SET 的竞态。
    private const string SetScript = """
        local valueKey = KEYS[1]
        local tagKey = KEYS[2]
        local json = ARGV[1]
        local tag = ARGV[2]
        local ttlMs = tonumber(ARGV[3])
        local prefix = ARGV[4]

        local old = redis.call('GET', valueKey)
        if old then
            local ok, obj = pcall(cjson.decode, old)
            if ok and type(obj) == 'table' and obj.tag and obj.tag ~= '' and obj.tag ~= tag then
                redis.call('SREM', prefix .. obj.tag, valueKey)
            end
        end

        if ttlMs > 0 then
            redis.call('SET', valueKey, json, 'PX', ttlMs)
        else
            redis.call('SET', valueKey, json)
        end

        if tag ~= '' then
            redis.call('SADD', tagKey, valueKey)
        end
        return 1
        """;

    private const string RemoveScript = """
        local valueKey = KEYS[1]
        local prefix = ARGV[1]
        local old = redis.call('GET', valueKey)
        if old then
            local ok, obj = pcall(cjson.decode, old)
            if ok and type(obj) == 'table' and obj.tag and obj.tag ~= '' then
                redis.call('SREM', prefix .. obj.tag, valueKey)
            end
        end
        return redis.call('DEL', valueKey)
        """;

    private const string RemoveByTagScript = """
        local tagKey = KEYS[1]
        local members = redis.call('SMEMBERS', tagKey)
        for i = 1, #members do
            redis.call('DEL', members[i])
        end
        redis.call('DEL', tagKey)
        return #members
        """;

    private const string CleanupTagScript = """
        local tagKey = KEYS[1]
        local members = redis.call('SMEMBERS', tagKey)
        local removed = 0
        for i = 1, #members do
            if redis.call('EXISTS', members[i]) == 0 then
                redis.call('SREM', tagKey, members[i])
                removed = removed + 1
            end
        end
        if redis.call('SCARD', tagKey) == 0 then
            redis.call('DEL', tagKey)
        end
        return removed
        """;

    public async Task<RedisCacheHit<T>> TryGetAsync<T>(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        if (!RedisCacheState.IsEnabled)
            return default;

        try
        {
            var database = await connection.GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
            var valueKey = ValueKey(key);
            var json = (string?)await database.StringGetAsync(valueKey).ConfigureAwait(false);
            if (json is null)
                return default;

            var envelope = JsonSerializer.Deserialize<RedisCacheEnvelope<T>>(json, JsonOptions);
            if (envelope is null)
                return default;

            var now = DateTimeOffset.UtcNow;
            if (envelope.AbsoluteExpiresAtUtc is { } absolute && absolute <= now)
            {
                await RemoveCoreAsync(database, valueKey).ConfigureAwait(false);
                await PublishAsync(InvalidationKind.Key, key, cancellationToken).ConfigureAwait(false);
                return default;
            }

            if (ComputeTtl(envelope, now) is { } ttl)
                await database.KeyExpireAsync(valueKey, ttl).ConfigureAwait(false);

            return new RedisCacheHit<T>(true, envelope.Value, ToEntryOptions(envelope));
        }
        catch (Exception ex) when (ShouldDegrade(ex, cancellationToken))
        {
            logger.LogWarning(ex, "Redis cache read failed for key {CacheKey}; falling back to miss.", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, CacheEntryOptions? options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        if (!RedisCacheState.IsEnabled)
            return;

        var now = DateTimeOffset.UtcNow;
        var absolute = options?.AbsoluteExpirationRelativeToNow is { } absoluteTtl
            ? now.Add(absoluteTtl)
            : (DateTimeOffset?)null;
        var sliding = options?.SlidingExpiration;
        var ttl = sliding ?? (absolute is { } deadline ? deadline - now : null);
        var ttlMs = ttl is { } relative ? Math.Max(1, (long)relative.TotalMilliseconds) : 0;

        var envelope = new RedisCacheEnvelope<T>
        {
            Value = value,
            Tag = options?.Tag,
            AbsoluteExpiresAtUtc = absolute,
            SlidingExpirationTicks = sliding?.Ticks
        };

        var valueKey = ValueKey(key);
        var tagKey = envelope.Tag is null ? TagKey(NoTagPlaceholder) : TagKey(envelope.Tag);
        var json = JsonSerializer.Serialize(envelope, JsonOptions);

        try
        {
            var database = await connection.GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
            await database.ScriptEvaluateAsync(
                SetScript,
                [valueKey, tagKey],
                [json, envelope.Tag ?? string.Empty, ttlMs, TagPrefix]).ConfigureAwait(false);

            await PublishAsync(InvalidationKind.Key, key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldDegrade(ex, cancellationToken))
        {
            logger.LogWarning(ex, "Redis cache write failed for key {CacheKey}; L1 result is kept.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        if (!RedisCacheState.IsEnabled)
            return;

        try
        {
            var database = await connection.GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
            await RemoveCoreAsync(database, ValueKey(key)).ConfigureAwait(false);
            await PublishAsync(InvalidationKind.Key, key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldDegrade(ex, cancellationToken))
        {
            logger.LogWarning(ex, "Redis cache remove failed for key {CacheKey}.", key);
        }
    }

    public async Task RemoveByTagAsync(string tag, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        cancellationToken.ThrowIfCancellationRequested();
        if (!RedisCacheState.IsEnabled)
            return;

        try
        {
            var database = await connection.GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
            await database.ScriptEvaluateAsync(RemoveByTagScript, [TagKey(tag)], []).ConfigureAwait(false);
            await PublishAsync(InvalidationKind.Tag, tag, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldDegrade(ex, cancellationToken))
        {
            logger.LogWarning(ex, "Redis cache tag invalidation failed for tag {CacheTag}.", tag);
        }
    }

    public async Task CleanupStaleTagIndexAsync(string tagKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagKey);
        cancellationToken.ThrowIfCancellationRequested();
        if (!RedisCacheState.IsEnabled)
            return;

        try
        {
            var database = await connection.GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
            await database.ScriptEvaluateAsync(CleanupTagScript, [tagKey], []).ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldDegrade(ex, cancellationToken))
        {
            logger.LogDebug(ex, "Redis cache tag index cleanup failed for {TagKey}.", tagKey);
        }
    }

    private static async Task RemoveCoreAsync(IDatabase database, string valueKey) =>
        await database.ScriptEvaluateAsync(RemoveScript, [valueKey], [TagPrefix]).ConfigureAwait(false);

    private async Task PublishAsync(string kind, string value, CancellationToken cancellationToken)
    {
        if (!RedisCacheState.IsEnabled)
            return;

        try
        {
            var subscriber = await connection.GetSubscriberAsync(cancellationToken).ConfigureAwait(false);
            var message = JsonSerializer.Serialize(
                new CacheInvalidationMessage(RedisCacheState.NodeId, kind, value),
                JsonOptions);
            await subscriber.PublishAsync(RedisChannel.Literal(InvalidationChannel), message).ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldDegrade(ex, cancellationToken))
        {
            logger.LogDebug(ex, "Redis cache invalidation publish failed for {Kind}:{Value}.", kind, value);
        }
    }

    private static TimeSpan? ComputeTtl(RedisCacheEnvelope envelope, DateTimeOffset now)
    {
        var absolute = envelope.AbsoluteExpiresAtUtc is { } deadline ? deadline - now : (TimeSpan?)null;
        var sliding = envelope.SlidingExpirationTicks is { } ticks ? TimeSpan.FromTicks(ticks) : (TimeSpan?)null;

        if (sliding is null)
            return absolute;

        var ttl = sliding.Value;
        if (absolute is { } remaining)
        {
            if (remaining <= TimeSpan.Zero)
                return TimeSpan.Zero;
            if (ttl > remaining)
                ttl = remaining;
        }
        return ttl;
    }

    private static CacheEntryOptions ToEntryOptions(RedisCacheEnvelope envelope)
    {
        var options = new CacheEntryOptions
        {
            Tag = envelope.Tag,
            SlidingExpiration = envelope.SlidingExpirationTicks is { } ticks
                ? TimeSpan.FromTicks(ticks)
                : null
        };

        if (envelope.AbsoluteExpiresAtUtc is { } deadline)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            options.AbsoluteExpirationRelativeToNow = remaining > TimeSpan.Zero
                ? remaining
                : TimeSpan.FromSeconds(1);
        }

        return options;
    }

    private static bool ShouldDegrade(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && exception is not OperationCanceledException;

    private static string ValueKey(string key) => ValuePrefix + key;

    private static string TagKey(string tag) => TagPrefix + tag;
}

internal static class InvalidationKind
{
    public const string Key = "key";
    public const string Tag = "tag";
}

internal sealed record CacheInvalidationMessage(Guid NodeId, string Type, string Value);

internal class RedisCacheEnvelope
{
    public RedisCacheEnvelope()
    {
    }

    public string? Tag { get; set; }
    public DateTimeOffset? AbsoluteExpiresAtUtc { get; set; }
    public long? SlidingExpirationTicks { get; set; }
}

internal sealed class RedisCacheEnvelope<T> : RedisCacheEnvelope
{
    public T? Value { get; set; }
}
