using BlazorBoilerplate.Domain;
using BlazorBoilerplate.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.Persistence;

/// <summary>
/// 写后领域事件发布拦截器：在 SaveChanges 成功提交后收集 <see cref="IDomainEventProducer.DomainEvents"/>，
/// 逐个经 <see cref="IEventPublisher"/> 发布，随后清空。
/// <para>
/// 发布放在提交之后：数据已落库，订阅侧失败不影响写侧语义（记日志、不重抛）。
/// 发布器从构造时传入的宿主/模块 ServiceProvider 解析，缺失时静默跳过（Setup 阶段无事件总线也可启动）。
/// </para>
/// </summary>
public sealed class DomainEventPublishingInterceptor(
    IServiceProvider? services = null,
    ILogger<DomainEventPublishingInterceptor>? logger = null) : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await PublishAsync(eventData.Context, cancellationToken).ConfigureAwait(false);
        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        PublishAsync(eventData.Context, default).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    private async Task PublishAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null || services is null)
            return;

        var events = context.ChangeTracker.Entries()
            .Select(e => e.Entity)
            .OfType<IDomainEventProducer>()
            .SelectMany(p => p.DomainEvents.ToArray())
            .ToArray();

        if (events.Length == 0)
            return;

        var publisher = services.GetService<IEventPublisher>();
        if (publisher is null)
            return;

        foreach (var producer in context.ChangeTracker.Entries()
                     .Select(e => e.Entity)
                     .OfType<IDomainEventProducer>())
            producer.ClearDomainEvents();

        foreach (var domainEvent in events)
        {
            try
            {
                // 反射调用泛型 PublishAsync（运行时事件类型）。
                var method = typeof(IEventPublisher).GetMethod(nameof(IEventPublisher.PublishAsync))!
                    .MakeGenericMethod(domainEvent.GetType());
                var task = (Task)method.Invoke(publisher, [domainEvent, cancellationToken])!;
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to publish domain event after save. Event={Event}",
                    domainEvent.GetType().Name);
            }
        }
    }
}
