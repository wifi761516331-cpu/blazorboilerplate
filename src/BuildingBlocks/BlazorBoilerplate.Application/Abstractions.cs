namespace BlazorBoilerplate.Application.Abstractions;

// 第一批只定义稳定抽象；真实实现（PostgreSQL / Redis / MinIO / SMTP / MassTransit / Hangfire）
// 在后续批次接入，见 docs/ROADMAP.md。所有 I/O 均异步并传递 CancellationToken。

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>当前请求用户上下文（供审计拦截器/应用服务获取操作人，后台任务或匿名上下文可返回 null）。</summary>
public interface ICurrentUserContext
{
    Guid? UserId { get; }
    Guid? TenantId { get; }
}

public interface IFileStorage
{
    Task<Stream?> GetAsync(string key, CancellationToken cancellationToken);
    Task PutAsync(string key, Stream content, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken);
}

public interface ISmsSender
{
    Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken);
}

/// <summary>
/// 缓存条目策略。契约必须留在 Application 程序集（ModuleLoadContext.SharedAssemblies），
/// 保证宿主与模块 ALC 共用同一类型身份；模块端点传入的 options 不会被缓存持有，仅 value 会。
/// </summary>
public sealed class CacheEntryOptions
{
    /// <summary>绝对过期（相对写入时刻）。null 表示永不过期。</summary>
    public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }

    /// <summary>滑动过期：距上次访问超过此时长即失效，不延长绝对过期上限。</summary>
    public TimeSpan? SlidingExpiration { get; set; }

    /// <summary>失效分组标签；RemoveByTagAsync 按此批量清除。null 表示不参与分组。</summary>
    public string? Tag { get; set; }

    /// <summary>
    /// 是否缓存 null 结果（防穿透）。默认 false：factory 返回 null 时不写入缓存。
    /// 仅对 GetOrCreateAsync 生效；SetAsync 显式写入的 null 不受影响。
    /// </summary>
    public bool CacheNullValues { get; set; }
}

/// <summary>
/// 缓存 key 构造器。带租户/用户的缓存必须经此构造，避免调用方手写 key 串租户数据。
/// 契约留在 Application，宿主与模块 ALC 共用同一类型身份。
/// </summary>
public interface ICacheKeyBuilder
{
    string Global(string key);
    string Tenant(Guid? tenantId, string key);
    string User(Guid? tenantId, Guid? userId, string key);
}

public sealed class CacheKeyBuilder : ICacheKeyBuilder
{
    public string Global(string key) => $"g:{Normalize(key)}";

    public string Tenant(Guid? tenantId, string key) => $"{TenantScope(tenantId)}:{Normalize(key)}";

    public string User(Guid? tenantId, Guid? userId, string key) =>
        $"{TenantScope(tenantId)}:u:{(userId is { } id ? id.ToString("N") : "anon")}:{Normalize(key)}";

    private static string TenantScope(Guid? tenantId) =>
        tenantId is { } id ? $"t:{id:N}" : "t:anon";

    private static string Normalize(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.Trim();
    }
}

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken);
    Task SetAsync<T>(string key, T value, TimeSpan? ttl, string? tag, CancellationToken cancellationToken);
    Task RemoveByTagAsync(string tag, CancellationToken cancellationToken);

    /// <summary>删除单个 key（不存在时静默）。</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// 读—未命中—_factory 生成—回填，per-key 同步保证同一 key 的并发请求只执行一次 factory（防击穿）。
    /// factory 结果必须是可安全跨 ALC 共享的类型（DTO / 共享契约），不得是模块 ALC 内部类型——
    /// 否则缓存条目会成为 GC root 阻止 collectible ALC 卸载。
    /// </summary>
    Task<T?> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory,
        CacheEntryOptions? options, CancellationToken cancellationToken);
}

/// <summary>用例标记接口。不使用 MediatR（v13+ 商业授权），采用自研极简分发器。</summary>
public interface IUseCase<out TResult>;

public interface IUseCaseHandler<in TUseCase, TResult> where TUseCase : IUseCase<TResult>
{
    Task<TResult> HandleAsync(TUseCase useCase, CancellationToken cancellationToken);
}

public interface IUseCaseDispatcher
{
    Task<TResult> SendAsync<TResult>(IUseCase<TResult> useCase, CancellationToken cancellationToken);
}

public sealed class UseCaseDispatcher(IServiceProvider services) : IUseCaseDispatcher
{
    public async Task<TResult> SendAsync<TResult>(IUseCase<TResult> useCase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(useCase);
        var handlerType = typeof(IUseCaseHandler<,>).MakeGenericType(useCase.GetType(), typeof(TResult));
        var handler = services.GetService(handlerType)
            ?? throw new InvalidOperationException($"No handler registered for {useCase.GetType().FullName}.");
        var method = handlerType.GetMethod(nameof(IUseCaseHandler<IUseCase<TResult>, TResult>.HandleAsync))
            ?? throw new InvalidOperationException("Handler method not found.");
        var task = (Task<TResult>)method.Invoke(handler, [useCase, cancellationToken])!;
        return await task.ConfigureAwait(false);
    }
}
