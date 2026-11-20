namespace BlazorBoilerplate.Application.Platform;

/// <summary>分布式锁租约句柄（Dispose 时自动释放锁）。</summary>
public interface IDistributedLockHandle : IAsyncDisposable
{
    /// <summary>锁定的资源键。</summary>
    string ResourceKey { get; }
}

/// <summary>分布式互斥锁服务（有 Redis 时使用跨节点锁，单机自动退化为进程内锁）。</summary>
public interface IDistributedLockService
{
    /// <summary>尝试获取资源互斥锁，获取失败返回 null。</summary>
    Task<IDistributedLockHandle?> TryAcquireAsync(
        string resourceKey,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);
}

