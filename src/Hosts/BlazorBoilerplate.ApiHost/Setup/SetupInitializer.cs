using System.Text.Json;
using BlazorBoilerplate.Infrastructure.Caching;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Identity.Domain;
using BlazorBoilerplate.Modules.Identity.Persistence;
using BlazorBoilerplate.Modules.Identity.Services;
using BlazorBoilerplate.ModuleRuntime.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace BlazorBoilerplate.ApiHost.Setup;

/// <summary>
/// 首次运行（安装向导）提交的配置。批次 2：接入 EF Core Migrations。
/// PostgreSQL 为主库（MigrateAsync 应用迁移，迁移产物无数据库级外键，
/// 参考 Nova 的 CustomNpgsqlMigrationsSqlGenerator 方案）；
/// SQLite 为开发/测试降级路径（EnsureCreated，模型无关系 → 同样无外键）。
/// </summary>
public sealed record SetupConfiguration(
    string AdminEmail,
    string AdminPassword,
    string? SystemName,
    string DatabaseProvider,
    string? PostgreSQL,
    string? SQLite,
    string? Redis,
    string? MySQL = null,
    string? MSSQL = null);

/// <summary>首次运行时的基础设施初始化：建库（迁移）+ 种子数据（管理员/角色/菜单）。</summary>
public interface ISetupInitializer
{
    Task InitializeAsync(SetupConfiguration configuration, CancellationToken cancellationToken);
}

public sealed class DefaultSetupInitializer(
    IHostEnvironment environment,
    ModuleCatalog catalog,
    ILogger<DefaultSetupInitializer> logger) : ISetupInitializer
{
    public async Task InitializeAsync(SetupConfiguration configuration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.AdminPassword))
            throw new InvalidOperationException("管理员密码不能为空。");

        var provider = PlatformConnectionState.Normalize(configuration.DatabaseProvider);
        var sqliteConn = ResolveSqliteConnection(configuration.SQLite);
        var conn = PlatformConnectionState.ResolveSystemType(provider) switch
        {
            DbSystemType.Sqlite => sqliteConn,
            DbSystemType.MySql => configuration.MySQL,
            DbSystemType.SqlServer => configuration.MSSQL,
            _ => configuration.PostgreSQL
        };

        // 1) 持久化配置（appsettings 之外的运行时配置文件），并同步平台连接状态供模块读取。
        RedisCacheState.Configure(configuration.Redis);
        PersistSettings(configuration, provider, conn, sqliteConn);
        PlatformConnectionState.Set(provider, configuration.PostgreSQL, sqliteConn, configuration.MySQL, configuration.MSSQL);

        // 2) 建库建表：统一按所选数据库（PostgreSQL / MySQL / MSSQL / SQLite）执行对应 EF Core 迁移。
        await IdentityMigrationHelper.MigrateAsync(provider, conn, cancellationToken);
        await using (var db = CreateDbContext(provider, conn))
        {
            // 3) 种子数据（仅空库时执行；安装幂等由 setup.json 标记保证）。
            await SeedAsync(db, configuration, cancellationToken);
        }

        logger.LogInformation(
            "Setup initialization completed. Provider={Provider}, Admin={Admin}, System={System}",
            provider, configuration.AdminEmail, configuration.SystemName);
    }

    private IdentityDbContext CreateDbContext(string provider, string? conn)
    {
        var builder = new DbContextOptionsBuilder<IdentityDbContext>();
        builder.UseIdentityProvider(
            provider: provider,
            connectionString: conn,
            enableRetryOnFailure: true);
        return new IdentityDbContext(builder.Options);
    }

    private string ResolveSqliteConnection(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;
        var dataDir = Path.Combine(environment.ContentRootPath, "data");
        Directory.CreateDirectory(dataDir);
        return $"Data Source={Path.Combine(dataDir, "blazorboilerplate.db")}";
    }

    private void PersistSettings(SetupConfiguration configuration, string provider, string? conn, string sqliteConn)
    {
        var dataDir = Path.Combine(environment.ContentRootPath, "data");
        Directory.CreateDirectory(dataDir);

        var settings = new
        {
            persistedAtUtc = DateTimeOffset.UtcNow,
            databaseProvider = provider,
            infrastructure = new
            {
                postgreSQL = configuration.PostgreSQL,
                mySQL = configuration.MySQL,
                mssql = configuration.MSSQL,
                sqlite = provider == PlatformConnectionState.SqliteProvider ? sqliteConn : configuration.SQLite,
                configuration.Redis
            }
        };
        var path = Path.Combine(dataDir, "blazorboilerplate.settings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }

    /// <summary>
    /// 种子：系统角色（管理员/普通用户）+ 核心菜单（含静态注册的模块菜单）+ 管理员账户 + 全量角色菜单授权。
    /// </summary>
    private async Task SeedAsync(IdentityDbContext db, SetupConfiguration configuration, CancellationToken ct)
    {
        // 幂等：数据库可能是上一轮安装遗留的（例如删掉 setup.json 重装、或 PG 库复用）。
        if (await db.Users.AnyAsync(ct))
        {
            logger.LogInformation("Seed skipped: identity data already exists.");
            return;
        }

        // ---- 角色 ----
        var adminRole = Role.Create("管理员", "平台内置管理员角色，拥有全部菜单访问权。", isSystem: true);
        var userRole = Role.Create("普通用户", "平台内置普通用户角色。", isSystem: true);
        db.Roles.AddRange(adminRole, userRole);

        // ---- 菜单：核心 + 模块静态注册（ModuleFrontendRegistry，含 catalog 等）----
        var coreMenus = new List<(string Name, string Path, string? Icon, int Sort)>
        {
            ("概览", "/", null, 0),
            ("用户管理", "/identity/users", "People", 10),
            ("角色管理", "/identity/roles", "SecurityGroup", 20),
            ("菜单管理", "/identity/menus", "EditList", 30),
            ("模块管理", "/modules", "Puzzle", 90)
        };
        var sort = 100;
        // 模块声明的菜单：优先读 manifest 的结构化 MenuItems（安装时发现阶段已载入 catalog），
        // 再并入代码里静态注册的菜单（ModuleFrontendRegistry），按 Route 去重。
        var declared = catalog.Manifests()
            .SelectMany(m => m.Frontend.MenuItems)
            .Where(m => !string.IsNullOrWhiteSpace(m.Route))
            .Select(m => (Name: m.Text, Path: m.Route, Icon: m.Icon, Sort: m.Sort == 0 ? sort += 10 : m.Sort));
        var registered = ModuleFrontendRegistry.All
            .Select(m => (Name: m.Text, Path: m.Route, Icon: m.Icon, Sort: sort += 10));
        var moduleMenus = declared.Concat(registered)
            .Where(m => !coreMenus.Any(c => c.Path == m.Path))
            .GroupBy(m => m.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var menus = new List<Menu>();
        menus.AddRange(coreMenus.Concat(moduleMenus)
            .Select(c => Menu.Create(c.Name, c.Path, c.Icon, parentId: null, sort: c.Sort)));
        db.Menus.AddRange(menus);

        // ---- 管理员账户 ----
        var email = configuration.AdminEmail.Trim();
        var admin = User.Create("admin", email, "系统管理员", PasswordHasher.Hash(configuration.AdminPassword));
        db.Users.Add(admin);
        db.UserRoles.Add(new UserRole(admin.Id, adminRole.Id));

        // ---- 授权：管理员角色绑定全部菜单 ----
        foreach (var menu in menus)
            db.RoleMenus.Add(new RoleMenu(adminRole.Id, menu.Id));

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Seed completed. Roles=2, Menus={Menus}, AdminUser={Admin}",
            menus.Count, email);
    }
}
