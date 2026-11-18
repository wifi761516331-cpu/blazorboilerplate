using BlazorBoilerplate.Modules.Localization.Domain;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Modules.Localization.Persistence;

public class LocalizationDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Language> Languages => Set<Language>();
    public DbSet<LocaleResource> Resources => Set<LocaleResource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Language>(e =>
        {
            e.ToTable("LocLanguages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Culture).HasMaxLength(32).IsRequired();
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
            e.Property(x => x.FlagCode).HasMaxLength(8);
            e.HasIndex(x => x.Culture).IsUnique();
        });

        modelBuilder.Entity<LocaleResource>(e =>
        {
            e.ToTable("LocResources");
            e.HasKey(x => x.Id);
            e.Property(x => x.ModuleId).HasMaxLength(64).IsRequired();
            e.Property(x => x.Culture).HasMaxLength(32).IsRequired();
            e.Property(x => x.Key).HasMaxLength(256).IsRequired();
            e.Property(x => x.Value).IsRequired();
            e.HasIndex(x => new { x.ModuleId, x.Culture, x.Key }).IsUnique();
            e.HasIndex(x => new { x.Culture, x.Key });
        });
    }
}

