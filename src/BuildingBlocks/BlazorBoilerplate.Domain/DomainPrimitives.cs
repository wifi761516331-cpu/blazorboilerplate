namespace BlazorBoilerplate.Domain;

/// <summary>聚合根标记。只有聚合根可以被 Repository 直接持久化。</summary>
public interface IAggregateRoot;

public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredOnUtc { get; }
}

/// <summary>
/// 领域事件生产者。模块 DbContext 在 SaveChanges 后按此接口收集并发布事件。
/// </summary>
public interface IDomainEventProducer
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}

public abstract class DomainEventBase : IDomainEvent
{
    protected DomainEventBase()
    {
        EventId = Guid.NewGuid();
        OccurredOnUtc = DateTimeOffset.UtcNow;
    }

    public Guid EventId { get; }
    public DateTimeOffset OccurredOnUtc { get; }
}

public abstract class Entity<TId> : IDomainEventProducer where TId : notnull
{
    public TId Id { get; protected init; } = default!;

    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();

    public override bool Equals(object? obj) =>
        obj is Entity<TId> other && EqualityComparer<TId>.Default.Equals(Id, other.Id);

    public override int GetHashCode() => EqualityComparer<TId>.Default.GetHashCode(Id);
}

/// <summary>实体审计契约（UTC 时间 + 操作人 Id）。</summary>
public interface IAuditableEntity
{
    DateTimeOffset CreatedAtUtc { get; }
    Guid? CreatedBy { get; }
    DateTimeOffset? ModifiedAtUtc { get; }
    Guid? ModifiedBy { get; }

    void MarkCreated(DateTimeOffset utcNow, Guid? userId);
    void MarkModified(DateTimeOffset utcNow, Guid? userId);
}

/// <summary>软删除契约（逻辑删除标记 + 删除时间/操作人）。</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }
    DateTimeOffset? DeletedAtUtc { get; }
    Guid? DeletedBy { get; }

    void MarkDeleted(DateTimeOffset utcNow, Guid? userId);
    void Restore(DateTimeOffset utcNow, Guid? userId);
}

/// <summary>带审计与软删除基础属性的实体基类。</summary>
public abstract class AuditableEntity<TId> : Entity<TId>, IAuditableEntity, ISoftDeletable where TId : notnull
{
    public DateTimeOffset CreatedAtUtc { get; protected set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedBy { get; protected set; }
    public DateTimeOffset? ModifiedAtUtc { get; protected set; }
    public Guid? ModifiedBy { get; protected set; }

    public bool IsDeleted { get; protected set; }
    public DateTimeOffset? DeletedAtUtc { get; protected set; }
    public Guid? DeletedBy { get; protected set; }

    public virtual void MarkCreated(DateTimeOffset utcNow, Guid? userId)
    {
        if (CreatedAtUtc == default)
            CreatedAtUtc = utcNow;
        CreatedBy ??= userId;
    }

    public virtual void MarkModified(DateTimeOffset utcNow, Guid? userId)
    {
        ModifiedAtUtc = utcNow;
        if (userId.HasValue)
            ModifiedBy = userId;
    }

    public virtual void MarkDeleted(DateTimeOffset utcNow, Guid? userId)
    {
        IsDeleted = true;
        DeletedAtUtc = utcNow;
        if (userId.HasValue)
            DeletedBy = userId;
        ModifiedAtUtc = utcNow;
        if (userId.HasValue)
            ModifiedBy = userId;
    }

    public virtual void Restore(DateTimeOffset utcNow, Guid? userId)
    {
        IsDeleted = false;
        DeletedAtUtc = null;
        DeletedBy = null;
        ModifiedAtUtc = utcNow;
        if (userId.HasValue)
            ModifiedBy = userId;
    }
}

public abstract class ValueObject
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj) =>
        obj is ValueObject other && GetType() == other.GetType() &&
        GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());

    public override int GetHashCode() =>
        GetEqualityComponents().Aggregate(0, (hash, part) =>
            HashCode.Combine(hash, part?.GetHashCode() ?? 0));
}

public static class Guard
{
    public static string AgainstNullOrWhiteSpace(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
        return value;
    }

    public static int AgainstOutOfRange(int value, int min, int max, string parameterName)
    {
        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(parameterName, value, $"Must be between {min} and {max}.");
        return value;
    }
}
