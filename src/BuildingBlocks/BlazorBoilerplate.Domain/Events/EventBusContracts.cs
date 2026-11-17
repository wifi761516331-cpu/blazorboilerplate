namespace BlazorBoilerplate.Domain.Events;

/// <summary>
/// 进程内强类型事件发布契约。事件类必须定义在共享程序集（Domain），
/// 保证宿主与模块 ALC 共用同一类型身份；事件对象仅在分发调用栈中短暂存在，
/// 发布器不得长期持有事件实例或订阅者实例。
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : IDomainEvent;
}

/// <summary>
/// 强类型事件处理器。实现类型由模块加载时扫描 <see cref="SubscribeAttribute"/> 发现并注册。
/// </summary>
public interface IEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}

/// <summary>
/// 标记一个处理器类订阅领域事件。事件类型从 <see cref="IEventHandler{TEvent}"/> 接口推导，
/// 同一个类可实现多个 <see cref="IEventHandler{TEvent}"/> 以订阅多种事件。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SubscribeAttribute : Attribute;

/// <summary>跨节点字符串频道。仅承载字符串（通常为 JSON），不传递任何 CLR 对象。</summary>
public interface IChannelBus
{
    Task PublishAsync(string channel, string message, CancellationToken cancellationToken);

    /// <summary>订阅频道。返回的句柄在 Dispose 时退订。</summary>
    Task<IChannelSubscription> SubscribeAsync(string channel,
        Func<string, string, Task> handler, CancellationToken cancellationToken);
}

public interface IChannelSubscription : IAsyncDisposable;
