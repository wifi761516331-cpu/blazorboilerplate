using BlazorBoilerplate.Domain;
using BlazorBoilerplate.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Modules.Demo;

/// <summary>
/// Demo 模块私有数据库实体（继承 <see cref="AuditableEntity{TId}"/> 并实现 <see cref="ITenantEntity"/>，
/// 自动具备 Id、审计字段、软删除、多租户行级隔离与领域事件能力）。
/// </summary>
public sealed class DemoNote : AuditableEntity<Guid>, ITenantEntity
{
    public DemoNote()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid? TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// Demo 模块私有 DbContext（继承 <see cref="ModuleDbContextBase"/>，自动启用软删除过滤；
/// 配合 <see cref="ModuleDbContextOptionsExtensions.UsePlatformDbProvider"/> 自动适配宿主数据库并发布 CRUD 领域事件）。
/// </summary>
public class DemoDbContext(DbContextOptions options) : ModuleDbContextBase(options)
{
    public DbSet<DemoNote> Notes => Set<DemoNote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DemoNote>(b =>
        {
            b.ToTable("DemoNotes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(200).IsRequired();
            b.HasIndex(x => x.TenantId);
        });

        base.OnModelCreating(modelBuilder);
    }
}
