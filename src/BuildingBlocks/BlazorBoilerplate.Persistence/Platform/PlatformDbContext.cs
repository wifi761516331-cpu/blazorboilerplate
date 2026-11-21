using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Persistence.Platform;

/// <summary>
/// 平台公共数据 DbContext：承载模块强类型配置、业务活动日志、消息模板与邮件队列，
/// 自动适配 PostgreSQL / MySQL / SQL Server / SQLite，并启用全局软删除过滤。
/// </summary>
public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : ModuleDbContextBase(options)
{
    public DbSet<PlatformSetting> Settings => Set<PlatformSetting>();
    public DbSet<PlatformActivityLog> ActivityLogs => Set<PlatformActivityLog>();
    public DbSet<PlatformMessageTemplate> MessageTemplates => Set<PlatformMessageTemplate>();
    public DbSet<PlatformQueuedEmail> QueuedEmails => Set<PlatformQueuedEmail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlatformSetting>(b =>
        {
            b.ToTable("PlatformSettings");
            b.HasKey(x => x.Id);
            b.Property(x => x.Key).HasMaxLength(256).IsRequired();
            b.Property(x => x.Value).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
        });

        modelBuilder.Entity<PlatformActivityLog>(b =>
        {
            b.ToTable("PlatformActivityLogs");
            b.HasKey(x => x.Id);
            b.Property(x => x.SystemKeyword).HasMaxLength(128).IsRequired();
            b.Property(x => x.Comment).HasMaxLength(2000).IsRequired();
            b.Property(x => x.UserName).HasMaxLength(128);
            b.Property(x => x.EntityName).HasMaxLength(128);
            b.Property(x => x.EntityId).HasMaxLength(128);
            b.Property(x => x.IpAddress).HasMaxLength(64);
            b.HasIndex(x => x.CreatedAtUtc);
            b.HasIndex(x => x.SystemKeyword);
        });

        modelBuilder.Entity<PlatformMessageTemplate>(b =>
        {
            b.ToTable("PlatformMessageTemplates");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(128).IsRequired();
            b.Property(x => x.Subject).HasMaxLength(500).IsRequired();
            b.Property(x => x.Body).IsRequired();
            b.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<PlatformQueuedEmail>(b =>
        {
            b.ToTable("PlatformQueuedEmails");
            b.HasKey(x => x.Id);
            b.Property(x => x.From).HasMaxLength(256);
            b.Property(x => x.To).HasMaxLength(256).IsRequired();
            b.Property(x => x.Subject).HasMaxLength(500).IsRequired();
            b.Property(x => x.Body).IsRequired();
            b.Property(x => x.LastError).HasMaxLength(2000);
            b.HasIndex(x => new { x.SentOnUtc, x.SentTries, x.Priority, x.CreatedOnUtc });
        });

        base.OnModelCreating(modelBuilder);
    }
}
