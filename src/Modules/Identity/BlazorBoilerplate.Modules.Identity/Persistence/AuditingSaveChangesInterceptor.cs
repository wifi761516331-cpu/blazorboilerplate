using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Identity.Persistence;

/// <summary>
/// EF Core 审计与软删除拦截器：
/// 1) Added 自动写入 CreatedAtUtc / CreatedBy；
/// 2) Modified 自动写入 ModifiedAtUtc / ModifiedBy（且保护创建审计字段不被覆盖）；
/// 3) Deleted（实现 ISoftDeletable）自动转换为逻辑删除（IsDeleted = true、DeletedAtUtc、DeletedBy）。
/// </summary>
public sealed class AuditingSaveChangesInterceptor(IServiceProvider? services = null) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyAuditAndSoftDelete(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditAndSoftDelete(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyAuditAndSoftDelete(DbContext? context)
    {
        if (context is null)
            return;

        var utcNow = DateTimeOffset.UtcNow;
        var userId = services?.GetService<ICurrentUserContext>()?.UserId;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is IAuditableEntity auditable)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        auditable.MarkCreated(utcNow, userId);
                        break;

                    case EntityState.Modified:
                        auditable.MarkModified(utcNow, userId);
                        entry.Property(nameof(IAuditableEntity.CreatedAtUtc)).IsModified = false;
                        entry.Property(nameof(IAuditableEntity.CreatedBy)).IsModified = false;
                        entry.Property(nameof(IAuditableEntity.ModifiedAtUtc)).IsModified = true;
                        if (userId.HasValue)
                            entry.Property(nameof(IAuditableEntity.ModifiedBy)).IsModified = true;
                        break;
                }
            }

            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable softDeletable)
            {
                entry.State = EntityState.Modified;
                softDeletable.MarkDeleted(utcNow, userId);
                if (entry.Entity is IAuditableEntity)
                {
                    entry.Property(nameof(IAuditableEntity.CreatedAtUtc)).IsModified = false;
                    entry.Property(nameof(IAuditableEntity.CreatedBy)).IsModified = false;
                    entry.Property(nameof(IAuditableEntity.ModifiedAtUtc)).IsModified = true;
                    if (userId.HasValue)
                        entry.Property(nameof(IAuditableEntity.ModifiedBy)).IsModified = true;
                }
                entry.Property(nameof(ISoftDeletable.IsDeleted)).IsModified = true;
                entry.Property(nameof(ISoftDeletable.DeletedAtUtc)).IsModified = true;
                if (userId.HasValue)
                    entry.Property(nameof(ISoftDeletable.DeletedBy)).IsModified = true;
            }
        }
    }
}

