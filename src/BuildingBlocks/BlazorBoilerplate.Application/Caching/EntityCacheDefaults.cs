using BlazorBoilerplate.Application.Abstractions;

namespace BlazorBoilerplate.Application.Caching;

/// <summary>标准化实体缓存键与失效标签约定。</summary>
public static class EntityCacheDefaults<TEntity>
{
    /// <summary>实体缓存统一前缀。</summary>
    public static string Prefix => $"bb.{typeof(TEntity).Name.ToLowerInvariant()}.";

    /// <summary>按主键缓存前缀。</summary>
    public static string ByIdPrefix => $"{Prefix}id.";

    /// <summary>实体集合/查询统一失效标签。</summary>
    public static string AllTag => $"{Prefix}all";

    /// <summary>生成主键缓存 Key。</summary>
    public static string ByIdKey(object id) => $"{ByIdPrefix}{id}";

    /// <summary>生成带实体失效标签的默认缓存策略。</summary>
    public static CacheEntryOptions CreateOptions(TimeSpan? ttl = null) => new()
    {
        AbsoluteExpirationRelativeToNow = ttl ?? TimeSpan.FromMinutes(10),
        Tag = AllTag
    };
}

