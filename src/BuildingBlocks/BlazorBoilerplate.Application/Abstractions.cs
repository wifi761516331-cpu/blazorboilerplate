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

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken);
    Task SetAsync<T>(string key, T value, TimeSpan? ttl, string? tag, CancellationToken cancellationToken);
    Task RemoveByTagAsync(string tag, CancellationToken cancellationToken);
}

public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOnUtc { get; init; } = DateTimeOffset.UtcNow;
    public required string ContractVersion { get; init; }
}

public interface IIntegrationEventBus
{
    Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken);
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
