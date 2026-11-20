using System.Reflection;
using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Application.Caching;
using BlazorBoilerplate.Domain.Events;

namespace BlazorBoilerplate.Infrastructure.Caching;

/// <summary>
/// 实体增删改自动缓存失效处理器：订阅 <see cref="EntityInsertedEvent{TEntity}"/> /
/// <see cref="EntityUpdatedEvent{TEntity}"/> / <see cref="EntityDeletedEvent{TEntity}"/>，
/// 自动清理对应 <see cref="EntityCacheDefaults{TEntity}.AllTag"/> 及主键缓存。
/// </summary>
public sealed class EntityCacheInvalidator<TEntity>(ICacheService cache) :
    IConsumer<EntityInsertedEvent<TEntity>>,
    IConsumer<EntityUpdatedEvent<TEntity>>,
    IConsumer<EntityDeletedEvent<TEntity>>
{
    private static readonly PropertyInfo? IdProp = typeof(TEntity).GetProperty("Id");

    public Task HandleAsync(EntityInsertedEvent<TEntity> @event, CancellationToken cancellationToken) =>
        InvalidateAsync(@event.Entity, cancellationToken);

    public Task HandleAsync(EntityUpdatedEvent<TEntity> @event, CancellationToken cancellationToken) =>
        InvalidateAsync(@event.Entity, cancellationToken);

    public Task HandleAsync(EntityDeletedEvent<TEntity> @event, CancellationToken cancellationToken) =>
        InvalidateAsync(@event.Entity, cancellationToken);

    private async Task InvalidateAsync(TEntity entity, CancellationToken ct)
    {
        await cache.RemoveByTagAsync(EntityCacheDefaults<TEntity>.AllTag, ct).ConfigureAwait(false);
        if (entity is not null && IdProp?.GetValue(entity) is { } id)
            await cache.RemoveAsync(EntityCacheDefaults<TEntity>.ByIdKey(id), ct).ConfigureAwait(false);
    }
}

