namespace BlazorBoilerplate.Infrastructure.Caching;

/// <summary>
/// Redis 缓存在当前进程内的运行状态。安装向导在应用启动后才写入 Redis 连接串，
/// 所以这里用静态状态让 HybridCacheService / RedisConnectionProvider 延迟感知配置变化。
/// </summary>
public static class RedisCacheState
{
    /// <summary>开箱即用默认值；本机或 compose 未改配置时直接使用。</summary>
    public const string DefaultConnectionString = "localhost:6379";

    private static string? _connectionString;

    /// <summary>配置变化时通知宿主侧连接/订阅对象自行重建。</summary>
    public static event EventHandler? ConnectionChanged;

    /// <summary>用于 pub/sub 消息剔除自身，避免刚写入的 L1 被本机订阅立即清掉。</summary>
    public static Guid NodeId { get; } = Guid.NewGuid();

    public static string? ConnectionString => Volatile.Read(ref _connectionString);

    public static bool IsEnabled => ConnectionString is not null;

    public static void Configure(string? connectionString)
    {
        var value = string.IsNullOrWhiteSpace(connectionString)
            || string.Equals(connectionString.Trim(), "not-configured", StringComparison.OrdinalIgnoreCase)
            ? null
            : connectionString.Trim();

        var old = Volatile.Read(ref _connectionString);
        if (string.Equals(old, value, StringComparison.Ordinal))
            return;

        Volatile.Write(ref _connectionString, value);
        ConnectionChanged?.Invoke(null, EventArgs.Empty);
    }
}
