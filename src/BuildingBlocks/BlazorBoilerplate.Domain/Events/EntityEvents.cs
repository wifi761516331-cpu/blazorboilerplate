namespace BlazorBoilerplate.Domain.Events;

/// <summary>实体新增事件。</summary>
public sealed record EntityInsertedEvent<TEntity>(TEntity Entity) : IDomainEvent
{
    public Guid EventId { get; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredOnUtc { get; } = DateTimeOffset.UtcNow;
}

/// <summary>实体更新事件。</summary>
public sealed record EntityUpdatedEvent<TEntity>(TEntity Entity) : IDomainEvent
{
    public Guid EventId { get; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredOnUtc { get; } = DateTimeOffset.UtcNow;
}

/// <summary>实体删除事件。</summary>
public sealed record EntityDeletedEvent<TEntity>(TEntity Entity) : IDomainEvent
{
    public Guid EventId { get; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredOnUtc { get; } = DateTimeOffset.UtcNow;
}

/// <summary>事件消费者契约（无需额外特性即可被自动发现）。</summary>
public interface IConsumer<in TEvent> : IEventHandler<TEvent> where TEvent : IDomainEvent;
