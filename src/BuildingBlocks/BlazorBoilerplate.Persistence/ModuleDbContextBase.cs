using System.Linq.Expressions;
using BlazorBoilerplate.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BlazorBoilerplate.Persistence;

/// <summary>
/// 模块 DbContext 基类：统一为所有实现 <see cref="ISoftDeletable"/> 的实体注册全局软删除过滤（!e.IsDeleted），
/// 并在 SQLite 下全局转换 DateTimeOffset 以支持 ORDER BY 排序。
/// 子类在 <see cref="OnModelCreating"/> 中完成实体配置后调用 <c>base.OnModelCreating(modelBuilder)</c>。
/// </summary>
public abstract class ModuleDbContextBase : DbContext
{
    private static readonly DateTimeOffsetToStringConverter SqliteDateTimeOffsetConverter = new();

    protected ModuleDbContextBase(DbContextOptions options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplySoftDeleteQueryFilters(modelBuilder);

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

    protected static void ApplySoftDeleteQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
                continue;

            var param = Expression.Parameter(entityType.ClrType, "e");
            var prop = Expression.Property(param, nameof(ISoftDeletable.IsDeleted));
            var filter = Expression.Lambda(Expression.Not(prop), param);
            entityType.SetQueryFilter(filter);
        }
    }
}

