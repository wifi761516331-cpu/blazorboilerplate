using System.Collections.Concurrent;
using System.Reflection;
using BlazorBoilerplate.Domain;
using BlazorBoilerplate.Domain.Events;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.Infrastructure.Events;

/// <summary>
/// 进程内强类型事件总线。
/// <para>
/// ALC 安全：注册表只持有处理器的 <see cref="WeakReference"/> 与一个静态桥接委托，
/// 不持有模块实例、模块 <see cref="Type"/> 或任何捕获模块对象的闭包。
/// 即使模块卸载时忘记显式摘除，处理器被回收后弱引用自然失效，不会钉住 collectible ALC。
/// </para>
/// 分发：按事件精确类型查找订阅者，串行 await；单个处理器异常记日志、不重抛。
/// </summary>
public sealed class EventBus(ILogger<EventBus> logger) : IEventPublisher
{
    private readonly ConcurrentDictionary<Type, List<Subscription>> _subscriptions = new();
    private readonly object _gate = new();

    /// <summary>注册强类型处理器。moduleId 用于模块卸载时按模块批量摘除。</summary>
    public void Subscribe<TEvent>(IEventHandler<TEvent> handler, string? moduleId = null)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        Add(typeof(TEvent), new Subscription(
            moduleId,
            new WeakReference<object>(handler, trackResurrection: false),
            HandlerBridge<TEvent>.InvokeAsync));
    }

    /// <summary>
    /// 按对象实际实现的 <see cref="IEventHandler{TEvent}"/> 接口注册（模块加载时扫描发现用）。
    /// 一个处理器可实现多个接口，每种事件各注册一条。
    /// </summary>
    public int SubscribeAll(object handler, string? moduleId = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var count = 0;
        foreach (var iface in handler.GetType().GetInterfaces())
        {
            if (!iface.IsGenericType || iface.GetGenericTypeDefinition() != typeof(IEventHandler<>))
                continue;
            var eventType = iface.GetGenericArguments()[0];
            if (!typeof(IDomainEvent).IsAssignableFrom(eventType))
                continue;

            var bridge = (Func<WeakReference<object>, IDomainEvent, CancellationToken, Task>)BridgeFactory
                .MakeGenericMethod(eventType)
                .Invoke(null, [])!;

            Add(eventType, new Subscription(
                moduleId,
                new WeakReference<object>(handler, trackResurrection: false),
                bridge));
            count++;
        }
        return count;
    }

    /// <summary>摘除某模块的全部订阅。卸载路径必须调用，避免弱引用条目残留。</summary>
    public void UnsubscribeModule(string moduleId)
    {
        lock (_gate)
        {
            foreach (var pair in _subscriptions)
            {
                pair.Value.RemoveAll(s => string.Equals(s.ModuleId, moduleId, StringComparison.Ordinal));
            }
            foreach (var empty in _subscriptions.Where(p => p.Value.Count == 0).Select(p => p.Key).ToArray())
                _subscriptions.TryRemove(empty, out _);
        }
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (!_subscriptions.TryGetValue(typeof(TEvent), out var subscriptions))
            return;

        Subscription[] snapshot;
        lock (_gate)
            snapshot = subscriptions.ToArray();

        foreach (var subscription in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!subscription.Handler.TryGetTarget(out _))
            {
                // 处理器已被回收（模块已卸载）：惰性清理，绝不复活模块对象。
                lock (_gate)
                    subscriptions.Remove(subscription);
                continue;
            }

            try
            {
                await subscription.Invoke(subscription.Handler, @event, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 记日志、不重抛：单个订阅者失败不影响写入方与其他订阅者。
                logger.LogError(ex, "Event handler failed. Event={Event}, EventId={EventId}",
                    typeof(TEvent).Name, @event.EventId);
            }
        }
    }

    private void Add(Type eventType, Subscription subscription)
    {
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(eventType, out var list))
                _subscriptions[eventType] = list = [];
            list.Add(subscription);
        }
    }

    private sealed record Subscription(
        string? ModuleId,
        WeakReference<object> Handler,
        Func<WeakReference<object>, IDomainEvent, CancellationToken, Task> Invoke);

    private static class HandlerBridge<TEvent> where TEvent : IDomainEvent
    {
        public static Task InvokeAsync(WeakReference<object> handler, IDomainEvent @event, CancellationToken cancellationToken)
        {
            if (!handler.TryGetTarget(out var target) || target is not IEventHandler<TEvent> typed)
                return Task.CompletedTask;
            return typed.HandleAsync((TEvent)@event, cancellationToken);
        }
    }

    private static readonly MethodInfo BridgeFactory =
        typeof(EventBus).GetMethod(nameof(GetBridge), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static Func<WeakReference<object>, IDomainEvent, CancellationToken, Task> GetBridge<TEvent>()
        where TEvent : IDomainEvent
        => HandlerBridge<TEvent>.InvokeAsync;
}
