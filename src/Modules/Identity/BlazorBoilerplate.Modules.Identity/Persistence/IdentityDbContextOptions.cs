using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BlazorBoilerplate.Modules.Identity.Persistence;

/// <summary>
/// DbContext 选项的统一构建：按 PlatformConnectionState 选择提供者。
/// PostgreSQL 走 Npgsql 并 ReplaceService 无外键迁移生成器（Nova 方案）；
/// SQLite 为开发/测试降级路径（EnsureCreated 建表，模型无关系 → 同样无外键）。
/// </summary>
public static class IdentityDbContextOptions
{
    public static void UseIdentityProvider(
        this DbContextOptionsBuilder builder,
        IServiceProvider? services = null,
        string? provider = null,
        string? connectionString = null)
    {
        var resolved = PlatformConnectionState.Normalize(provider ?? PlatformConnectionState.DatabaseProvider);
        var conn = connectionString ?? PlatformConnectionState.ResolveConnectionString();

        builder.AddInterceptors(new AuditingSaveChangesInterceptor(services));

        if (resolved == PlatformConnectionState.SqliteProvider)
        {
            builder.UseSqlite(conn);
            return;
        }

        builder
            .UseNpgsql(conn)
            .ReplaceService<IMigrationsSqlGenerator, CustomNpgsqlMigrationsSqlGenerator>();
    }
}

/// <summary>
/// dotnet-ef 设计时工厂（dotnet ef migrations add 使用）。
/// 连接串用占位值（生成迁移不连接数据库），ReplaceService 与运行时一致，
/// 保证迁移文件与运行时 SQL 生成行为完全对齐。
/// </summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("BLAZORBOILERPLATE_DESIGN_CONNECTION")
                   ?? "Host=localhost;Port=5432;Database=blazorboilerplate_design;Username=design;Password=design";

        var builder = new DbContextOptionsBuilder<IdentityDbContext>();
        builder
            .UseNpgsql(conn, npgsql => npgsql.EnableRetryOnFailure())
            .ReplaceService<IMigrationsSqlGenerator, CustomNpgsqlMigrationsSqlGenerator>();

        return new IdentityDbContext(builder.Options);
    }
}
