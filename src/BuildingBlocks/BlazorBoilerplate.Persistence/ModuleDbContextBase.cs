using System.Linq.Expressions;
using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BlazorBoilerplate.Persistence;

/// <summary>
/// 模块 DbContext 基类：
/// 1) 为实现 <see cref="ISoftDeletable"/> 的实体注册全局软删除过滤（!e.IsDeleted）；
/// 2) 为实现 <see cref="ITenantEntity"/> 的实体注册全局多租户过滤（e.TenantId == CurrentTenantId）；
///    当 CurrentTenantId 为 null 时仅允许访问 TenantId 为 null 的宿主级记录，不再放行其他租户数据；
///    跨租户查询需显式调用 IgnoreQueryFilters()；
/// 3) 在 SQLite 下全局转换 DateTimeOffset 以支持 ORDER BY 排序。
/// 子类在 <see cref="OnModelCreating"/> 中完成实体配置后调用 <c>base.OnModelCreating(modelBuilder)</c>。
/// </summary>
public abstract class ModuleDbContextBase : DbContext
{
    private static readonly DateTimeOffsetToStringConverter SqliteDateTimeOffsetConverter = new();

    protected ModuleDbContextBase(DbContextOptions options) : base(options) { }

    /// <summary>当前请求上下文的租户 Id（未绑定租户时为 null）。EF Core 查询过滤器会在每次查询时动态求值。</summary>
    protected Guid? CurrentTenantId =>
        this.GetInfrastructure().GetService(typeof(ICurrentUserContext)) is ICurrentUserContext ctx
            ? ctx.TenantId
            : null;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyGlobalEntityQueryFilters(modelBuilder);

        if (string.Equals(Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.OrdinalIgnoreCase))
            ApplySqliteDateTimeOffsetConverters(modelBuilder);
    }

    protected static void ApplySqliteDateTimeOffsetConverters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                    property.SetValueConverter(SqliteDateTimeOffsetConverter);
            }
        }
    }

    protected void ApplyGlobalEntityQueryFilters(ModelBuilder modelBuilder)
    {
        var currentTenantExpr = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var isSoftDeletable = typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType);
            var isTenantEntity = typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType);
            if (!isSoftDeletable && !isTenantEntity)
                continue;

            var param = Expression.Parameter(entityType.ClrType, "e");
            Expression? combined = null;

            if (isSoftDeletable)
            {
                var isDeletedProp = Expression.Property(param, nameof(ISoftDeletable.IsDeleted));
                combined = Expression.Not(isDeletedProp);
            }

            if (isTenantEntity)
            {
                var tenantProp = Expression.Property(param, nameof(ITenantEntity.TenantId));
                var tenantFilter = Expression.Equal(tenantProp, currentTenantExpr);
                combined = combined is null ? tenantFilter : Expression.AndAlso(combined, tenantFilter);
            }

            if (combined is not null)
                entityType.SetQueryFilter(Expression.Lambda(combined, param));
        }
    }
}
