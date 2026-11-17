using BlazorBoilerplate.Domain;
using BlazorBoilerplate.Domain.Events;
using BlazorBoilerplate.Infrastructure.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BlazorBoilerplate.ModuleRuntime.Tests;

/// <summary>
/// 事件总线测试：强类型分发、异常隔离、弱引用回收、模块卸载摘除、SubscribeAll 发现。
/// </summary>
public class EventBusTests
{
    private sealed class TestEvent(string value) : DomainEventBase
    {
        public string Value { get; } = value;
    }

    private sealed class CollectingHandler<T>(List<T> sink) : IEventHandler<T> where T : IDomainEvent
    {
        public Task HandleAsync(T @event, CancellationToken cancellationToken)
        {
            sink.Add(@event);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Publish_ReachesAllSubscribers()
    {
        var bus = new EventBus(NullLogger<EventBus>.Instance);
        var a = new List<TestEvent>();
        var b = new List<TestEvent>();
        bus.Subscribe(new CollectingHandler<TestEvent>(a));
        bus.Subscribe(new CollectingHandler<TestEvent>(b));

        var evt = new TestEvent("hello");
        await bus.PublishAsync(evt, default);

        Assert.Single(a);
        Assert.Single(b);
        Assert.Equal("hello", a[0].Value);
    }

    [Fact]
    public async Task Publish_OneHandlerThrows_OthersStillReceive()
    {
        var bus = new EventBus(NullLogger<EventBus>.Instance);
        var ok = new List<TestEvent>();
        bus.Subscribe<TestEvent>(new ThrowingHandler());
        bus.Subscribe(new CollectingHandler<TestEvent>(ok));

        await bus.PublishAsync(new TestEvent("x"), default);

        Assert.Single(ok); // 异常被记日志、未中断其余订阅者
    }

    private sealed class ThrowingHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task UnsubscribeModule_RemovesOnlyThatModulesHandlers()
    {
        var bus = new EventBus(NullLogger<EventBus>.Instance);
        var moduleA = new List<TestEvent>();
        var moduleB = new List<TestEvent>();
        // 保持强引用，避免被 GC 误判为已回收
        var handlerA = new CollectingHandler<TestEvent>(moduleA);
        var handlerB = new CollectingHandler<TestEvent>(moduleB);
        bus.Subscribe(handlerA, "mod-a");
        bus.Subscribe(handlerB, "mod-b");

        bus.UnsubscribeModule("mod-a");
        await bus.PublishAsync(new TestEvent("y"), default);

        Assert.Empty(moduleA);
        Assert.Single(moduleB);
        GC.KeepAlive(handlerA);
        GC.KeepAlive(handlerB);
    }

    [Fact]
    public async Task SubscribeAll_RegistersEveryImplementedEventHandler()
    {
        var bus = new EventBus(NullLogger<EventBus>.Instance);
        var a = new List<TestEvent>();
        var other = new List<AnotherEvent>();
        var multi = new MultiHandler(a, other);

        var count = bus.SubscribeAll(multi, "mod");

        Assert.Equal(2, count);
        await bus.PublishAsync(new TestEvent("1"), default);
        await bus.PublishAsync(new AnotherEvent("2"), default);
        Assert.Single(a);
        Assert.Single(other);
        GC.KeepAlive(multi);
    }

    private sealed class AnotherEvent(string value) : DomainEventBase
    {
        public string Value { get; } = value;
    }

    [Subscribe]
    private sealed class MultiHandler(List<TestEvent> a, List<AnotherEvent> b)
        : IEventHandler<TestEvent>, IEventHandler<AnotherEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken)
        {
            a.Add(@event);
            return Task.CompletedTask;
        }

        public Task HandleAsync(AnotherEvent @event, CancellationToken cancellationToken)
        {
            b.Add(@event);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Publish_DeadWeakHandler_IsSkippedWithoutResurrection()
    {
        var bus = new EventBus(NullLogger<EventBus>.Instance);
        RegisterTransientHandler(bus);

        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        // 处理器已无强引用，应被回收；发布不复活它、也不抛异常。
        await bus.PublishAsync(new TestEvent("z"), default);
    }

    private void RegisterTransientHandler(EventBus bus)
    {
        var sink = new List<TestEvent>();
        bus.Subscribe(new CollectingHandler<TestEvent>(sink), "gone");
    }
}
