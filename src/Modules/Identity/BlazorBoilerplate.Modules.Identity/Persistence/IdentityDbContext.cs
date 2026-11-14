using BlazorBoilerplate.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BlazorBoilerplate.Modules.Identity.Persistence;

/// <summary>
/// Identity 模块 DbContext。刻意不配置任何 EF 关系（导航/外键）：
/// 所有引用均为标量 Guid 列，数据库级不产生 FOREIGN KEY —— 参考 Nova 项目
/// 「移除外键」的约定；引用完整性（用户/角色/菜单存在性、树形循环）
/// 由 Services 层在代码中校验与级联清理。
/// 注意：新增的 8 张表不通过 EF Migration 创建（避免改动快照/Designer），
/// 由 IModuleDatabaseMigrations 的增量 SQL 建表；EF 仅负责查询/写入映射，
/// 因此这里的列名必须与增量 SQL 完全一致（PascalCase）。
/// </summary>
public class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RoleMenu> RoleMenus => Set<RoleMenu>();

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantUser> TenantUsers => Set<TenantUser>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PushNotificationSubscription> PushNotificationSubscriptions => Set<PushNotificationSubscription>();
    public DbSet<PermissionDefinition> PermissionDefinitions => Set<PermissionDefinition>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(ConfigureUser);
        modelBuilder.Entity<Role>(ConfigureRole);
        modelBuilder.Entity<Menu>(ConfigureMenu);
        modelBuilder.Entity<UserRole>(ConfigureUserRole);
        modelBuilder.Entity<RoleMenu>(ConfigureRoleMenu);

        modelBuilder.Entity<Tenant>(ConfigureTenant);
        modelBuilder.Entity<TenantUser>(ConfigureTenantUser);
        modelBuilder.Entity<UserSession>(ConfigureUserSession);
        modelBuilder.Entity<UserPreference>(ConfigureUserPreference);
        modelBuilder.Entity<Notification>(ConfigureNotification);
        modelBuilder.Entity<PushNotificationSubscription>(ConfigurePushNotificationSubscription);
        modelBuilder.Entity<PermissionDefinition>(ConfigurePermissionDefinition);
        modelBuilder.Entity<RolePermission>(ConfigureRolePermission);
    }

    private static void ConfigureUser(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.HasKey(u => u.Id);
        // 唯一性在代码层保证（Nova 约定），数据库仅建普通索引支撑查询。
        b.Property(u => u.UserName).IsRequired().HasMaxLength(64);
        b.Property(u => u.Email).IsRequired().HasMaxLength(256);
        b.Property(u => u.DisplayName).IsRequired().HasMaxLength(64);
        b.Property(u => u.PasswordHash).IsRequired().HasMaxLength(512);
        b.HasIndex(u => u.UserName).HasDatabaseName("IX_Users_UserName");
        b.HasIndex(u => u.Email).HasDatabaseName("IX_Users_Email");
    }

    private static void ConfigureRole(EntityTypeBuilder<Role> b)
    {
        b.ToTable("Roles");
        b.HasKey(r => r.Id);
        b.Property(r => r.Name).IsRequired().HasMaxLength(64);
        b.Property(r => r.Description).HasMaxLength(256);
        b.HasIndex(r => r.Name).HasDatabaseName("IX_Roles_Name");
    }

    private static void ConfigureMenu(EntityTypeBuilder<Menu> b)
    {
        b.ToTable("Menus");
        b.HasKey(m => m.Id);
        b.Property(m => m.Name).IsRequired().HasMaxLength(100);
        b.Property(m => m.Path).IsRequired().HasMaxLength(200);
        b.Property(m => m.Icon).HasMaxLength(100);
        // 增量迁移步骤 2 追加的 3 列（旧列上的行由 SQL DEFAULT 填 "menu"）。
        b.Property(m => m.ModuleId).HasMaxLength(128);
        b.Property(m => m.PermissionCode).HasMaxLength(128);
        b.Property(m => m.Kind).IsRequired().HasMaxLength(32);
        b.HasIndex(m => m.ParentId).HasDatabaseName("IX_Menus_ParentId");
    }

    private static void ConfigureUserRole(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("UserRoles");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.UserId).HasDatabaseName("IX_UserRoles_UserId");
        b.HasIndex(x => x.RoleId).HasDatabaseName("IX_UserRoles_RoleId");
    }

    private static void ConfigureRoleMenu(EntityTypeBuilder<RoleMenu> b)
    {
        b.ToTable("RoleMenus");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.RoleId).HasDatabaseName("IX_RoleMenus_RoleId");
        b.HasIndex(x => x.MenuId).HasDatabaseName("IX_RoleMenus_MenuId");
    }

    private static void ConfigureTenant(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("Tenants");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(128);
        b.Property(x => x.Title).HasMaxLength(256);
        b.Property(x => x.Domain).HasMaxLength(256);
        b.HasIndex(x => x.Name).HasDatabaseName("IX_Tenants_Name");
    }

    private static void ConfigureTenantUser(EntityTypeBuilder<TenantUser> b)
    {
        b.ToTable("TenantUsers");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.TenantId).HasDatabaseName("IX_TenantUsers_TenantId");
        b.HasIndex(x => x.UserId).HasDatabaseName("IX_TenantUsers_UserId");
    }

    private static void ConfigureUserSession(EntityTypeBuilder<UserSession> b)
    {
        b.ToTable("UserSessions");
        b.HasKey(x => x.Id);
        b.Property(x => x.IP).HasMaxLength(64);
        b.Property(x => x.DeviceInfo).HasMaxLength(512);
        b.Property(x => x.CultureName).HasMaxLength(32);
        b.Property(x => x.TimeZone).HasMaxLength(128);
        b.Property(x => x.RefreshToken).HasMaxLength(512);
        b.HasIndex(x => x.UserId).HasDatabaseName("IX_UserSessions_UserId");
        b.HasIndex(x => x.RefreshToken).HasDatabaseName("IX_UserSessions_RefreshToken");
    }

    private static void ConfigureUserPreference(EntityTypeBuilder<UserPreference> b)
    {
        b.ToTable("UserPreferences");
        b.HasKey(x => x.Id);
        b.Property(x => x.Culture).IsRequired().HasMaxLength(32);
        b.Property(x => x.TimeZone).IsRequired().HasMaxLength(128);
        b.Property(x => x.Theme).IsRequired().HasMaxLength(32);
        b.Property(x => x.AccentColor).HasMaxLength(64);
        b.HasIndex(x => x.UserId).HasDatabaseName("IX_UserPreferences_UserId");
    }

    private static void ConfigureNotification(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("Notifications");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).IsRequired().HasMaxLength(256);
        b.Property(x => x.Message).IsRequired().HasMaxLength(2048);
        b.Property(x => x.PageUrl).HasMaxLength(512);
        b.HasIndex(x => x.UserId).HasDatabaseName("IX_Notifications_UserId");
    }

    private static void ConfigurePushNotificationSubscription(EntityTypeBuilder<PushNotificationSubscription> b)
    {
        b.ToTable("PushNotificationSubscriptions");
        b.HasKey(x => x.Id);
        b.Property(x => x.DeviceId).IsRequired().HasMaxLength(256);
        b.Property(x => x.Platform).IsRequired().HasMaxLength(64);
        b.Property(x => x.PushChannel).IsRequired().HasMaxLength(64);
        b.Property(x => x.Endpoint).HasMaxLength(1024);
        b.Property(x => x.P256dh).HasMaxLength(512);
        b.Property(x => x.Auth).HasMaxLength(256);
        b.HasIndex(x => x.DeviceId).HasDatabaseName("IX_PushNotificationSubscriptions_DeviceId");
        b.HasIndex(x => x.UserId).HasDatabaseName("IX_PushNotificationSubscriptions_UserId");
    }

    private static void ConfigurePermissionDefinition(EntityTypeBuilder<PermissionDefinition> b)
    {
        b.ToTable("PermissionDefinitions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).IsRequired().HasMaxLength(128);
        b.Property(x => x.Name).IsRequired().HasMaxLength(128);
        b.Property(x => x.ModuleId).HasMaxLength(128);
        b.Property(x => x.MenuPath).HasMaxLength(512);
        b.Property(x => x.Kind).IsRequired().HasMaxLength(32);
        b.HasIndex(x => x.Code).HasDatabaseName("IX_PermissionDefinitions_Code");
    }

    private static void ConfigureRolePermission(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("RolePermissions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).IsRequired().HasMaxLength(128);
        b.HasIndex(x => x.RoleId).HasDatabaseName("IX_RolePermissions_RoleId");
        b.HasIndex(x => x.Code).HasDatabaseName("IX_RolePermissions_Code");
    }
}
