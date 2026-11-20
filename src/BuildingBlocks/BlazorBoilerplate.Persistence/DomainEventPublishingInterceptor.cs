using System.Runtime.CompilerServices;
using BlazorBoilerplate.Domain;
using BlazorBoilerplate.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.Persistence;

/// <summary>
/// 写后领域事件与实体 CRUD 事件发布拦截器：
/// 在 SaveChanges 成功提交后收集显式 <see cref="IDomainEventProducer.DomainEvents"/> 及 EF 追踪的实体增删改状态，
/// 逐个经 <see cref="IEventPublisher"/> 发布，随后清空。
/// </summary>
public sealed class DomainEventPublishingInterceptor(
    IServiceProvider? services = null,
    ILogger<DomainEventPublishingInterceptor>? logger = null) : SaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, List<(EntityState State, object Entity)>> _pendingChanges = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        CaptureEntityChanges(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CaptureEntityChanges(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

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

    private void CaptureEntityChanges(DbContext? context)
    {
        if (context is null || services is null)
            return;

        var captured = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => (e.State, e.Entity))
            .ToList();

        if (captured.Count > 0)
        {
            _pendingChanges.Remove(context);
            _pendingChanges.Add(context, captured);
        }
    }

    private async Task PublishAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null || services is null)
            return;

        var publisher = services.GetService<IEventPublisher>();
        var events = new List<IDomainEvent>();

        var explicitProducers = context.ChangeTracker.Entries()
            .Select(e => e.Entity)
            .OfType<IDomainEventProducer>()
            .ToArray();

        foreach (var producer in explicitProducers)
        {
            events.AddRange(producer.DomainEvents);
            producer.ClearDomainEvents();
        }

        if (_pendingChanges.TryGetValue(context, out var entityChanges))
        {
            _pendingChanges.Remove(context);
            foreach (var (state, entity) in entityChanges)
            {
                var genericDef = state switch
                {
                    EntityState.Added => typeof(EntityInsertedEvent<>),
                    EntityState.Modified => typeof(EntityUpdatedEvent<>),
                    EntityState.Deleted => typeof(EntityDeletedEvent<>),
                    _ => null
                };
                if (genericDef is null) continue;
                var eventType = genericDef.MakeGenericType(entity.GetType());
                if (Activator.CreateInstance(eventType, entity) is IDomainEvent evt)
                    events.Add(evt);
            }
        }

        if (events.Count == 0 || publisher is null)
            return;

        foreach (var domainEvent in events)
        {
            try
            {
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
