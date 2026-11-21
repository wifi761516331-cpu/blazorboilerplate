using BlazorBoilerplate.Domain;
using BlazorBoilerplate.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Modules.Domains;

/// <summary>域名路由绑定实体：将访问域名映射到指定模块、官网站点标识与入口路径。</summary>
public sealed class DomainRouteBinding : AuditableEntity<Guid>
{
    public DomainRouteBinding()
    {
        Id = Guid.CreateVersion7();
    }

    public string Host { get; set; } = string.Empty;
    public string? ModuleId { get; set; }
    public string? SiteKey { get; set; }
    public string EntryPath { get; set; } = "/";
    public bool AllowAdminRoutes { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Description { get; set; }
    public Guid? TenantId { get; set; }
}

/// <summary>Domains 模块私有 DbContext。</summary>
public class DomainsDbContext(DbContextOptions options) : ModuleDbContextBase(options)
{
    public DbSet<DomainRouteBinding> Bindings => Set<DomainRouteBinding>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DomainRouteBinding>(b =>
        {
            b.ToTable("DomainRouteBindings");
            b.HasKey(x => x.Id);
            b.Property(x => x.Host).HasMaxLength(255).IsRequired();
            b.Property(x => x.ModuleId).HasMaxLength(100);
            b.Property(x => x.SiteKey).HasMaxLength(100);
            b.Property(x => x.EntryPath).HasMaxLength(255).IsRequired();
            b.Property(x => x.Description).HasMaxLength(500);
            b.HasIndex(x => x.Host);
        });

        base.OnModelCreating(modelBuilder);
    }
}

