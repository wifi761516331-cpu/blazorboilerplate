using BlazorBoilerplate.Modules.Cms.Domain.Entities;
using BlazorBoilerplate.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Modules.Cms.Infrastructure.Persistence;

/// <summary>CMS 模块私有 DbContext（遵循 ADR-0003 无数据库外键约定；导航属性仅用于逻辑查询，除主键外全部非必填）。</summary>
public class CmsDbContext(DbContextOptions options) : ModuleDbContextBase(options)
{
    public DbSet<CmsSite> Sites => Set<CmsSite>();
    public DbSet<CmsLayout> Layouts => Set<CmsLayout>();
    public DbSet<CmsSection> Sections => Set<CmsSection>();
    public DbSet<CmsWidgetInstance> Widgets => Set<CmsWidgetInstance>();
    public DbSet<CmsLeadSubmission> Leads => Set<CmsLeadSubmission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CmsSite>(b =>
        {
            b.ToTable("CmsSites");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(120).IsRequired(false);
            b.Property(x => x.SiteKey).HasMaxLength(100).IsRequired(false);
            b.Property(x => x.Title).HasMaxLength(200).IsRequired(false);
            b.Property(x => x.Description).HasMaxLength(500).IsRequired(false);
            b.Property(x => x.LogoUrl).HasMaxLength(500).IsRequired(false);
            b.Property(x => x.FaviconUrl).HasMaxLength(500).IsRequired(false);
            b.Property(x => x.DefaultCulture).HasMaxLength(20).IsRequired(false);
            b.Property(x => x.SupportedCulturesJson).IsRequired(false);
            b.Property(x => x.ThemeJson).IsRequired(false);
            b.Property(x => x.PublishedSnapshotJson).IsRequired(false);
            b.HasIndex(x => x.SiteKey);
        });

        modelBuilder.Entity<CmsLayout>(b =>
        {
            b.ToTable("CmsLayouts");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(120).IsRequired(false);
            b.Property(x => x.Description).HasMaxLength(500).IsRequired(false);
            b.Property(x => x.ThumbnailUrl).HasMaxLength(500).IsRequired(false);
            b.Property(x => x.RowsJson).IsRequired(false);
            b.Property(x => x.CustomCss).IsRequired(false);
        });

        modelBuilder.Entity<CmsSection>(b =>
        {
            b.ToTable("CmsSections");
            b.HasKey(x => x.Id);
            b.Property(x => x.Culture).HasMaxLength(20).IsRequired(false);
            b.Property(x => x.Title).HasMaxLength(120).IsRequired(false);
            b.Property(x => x.AnchorSlug).HasMaxLength(100).IsRequired(false);
            b.Property(x => x.BackgroundStyle).HasMaxLength(200).IsRequired(false);
            b.HasIndex(x => new { x.SiteId, x.Culture, x.SortOrder });

            b.HasOne(x => x.Site)
                .WithMany(s => s.Sections)
                .HasForeignKey(x => x.SiteId)
                .OnDelete(DeleteBehavior.NoAction);

            b.HasOne(x => x.Layout)
                .WithMany(l => l.Sections)
                .HasForeignKey(x => x.LayoutId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<CmsWidgetInstance>(b =>
        {
            b.ToTable("CmsWidgets");
            b.HasKey(x => x.Id);
            b.Property(x => x.Culture).HasMaxLength(20).IsRequired(false);
            b.Property(x => x.ZoneId).HasMaxLength(80).IsRequired(false);
            b.Property(x => x.WidgetType).HasMaxLength(100).IsRequired(false);
            b.Property(x => x.Title).HasMaxLength(160).IsRequired(false);
            b.Property(x => x.PropsJson).IsRequired(false);
            b.Property(x => x.StyleJson).IsRequired(false);
            b.Property(x => x.TemplateName).HasMaxLength(120).IsRequired(false);
            b.HasIndex(x => new { x.SiteId, x.Culture, x.SectionId, x.SortOrder });

            b.HasOne(x => x.Site)
                .WithMany(s => s.Widgets)
                .HasForeignKey(x => x.SiteId)
                .OnDelete(DeleteBehavior.NoAction);

            b.HasOne(x => x.Section)
                .WithMany(s => s.Widgets)
                .HasForeignKey(x => x.SectionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<CmsLeadSubmission>(b =>
        {
            b.ToTable("CmsLeads");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(120).IsRequired(false);
            b.Property(x => x.Email).HasMaxLength(200).IsRequired(false);
            b.Property(x => x.Phone).HasMaxLength(60).IsRequired(false);
            b.Property(x => x.Company).HasMaxLength(200).IsRequired(false);
            b.Property(x => x.Message).HasMaxLength(4000).IsRequired(false);
            b.Property(x => x.ExtraJson).IsRequired(false);
            b.Property(x => x.SourceIp).HasMaxLength(64).IsRequired(false);
            b.Property(x => x.Status).HasMaxLength(32).IsRequired(false);
            b.HasIndex(x => new { x.SiteId, x.CreatedAtUtc });

            b.HasOne(x => x.Site)
                .WithMany(s => s.Leads)
                .HasForeignKey(x => x.SiteId)
                .OnDelete(DeleteBehavior.NoAction);

            b.HasOne(x => x.Section)
                .WithMany(s => s.Leads)
                .HasForeignKey(x => x.SectionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        base.OnModelCreating(modelBuilder);
    }
}
