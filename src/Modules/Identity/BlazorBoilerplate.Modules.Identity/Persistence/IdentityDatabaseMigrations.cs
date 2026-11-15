using BlazorBoilerplate.ModuleAbstractions;

namespace BlazorBoilerplate.Modules.Identity.Persistence;

/// <summary>
/// Identity 模块增量 DDL：只按 Sequence 递增创建新表 / 追加列 / 幂等种子，
/// 严禁 DROP / DELETE / 回滚（版本降级时不重放、不倒退）。
/// 每个步骤同时提供 SQLite（Sql）与 PostgreSQL（PostgreSql）两版。
///
/// 标识符统一用双引号包裹的 PascalCase，与 EF Core 生成的建表/查询 SQL 完全一致：
/// PG 下未加引号会被折叠成小写，导致 EF 的 "Tenants"."Id" 查询找不到对象。
/// 列名与 Domain 实体属性名 1:1 对应，表名与 DbSet / 现有初始迁移保持一致。
///
/// 类型映射：SQLite（Guid/时间/字符串 = TEXT、bool = INTEGER、int = INTEGER），
/// 与运行时 EF EnsureCreated 产物一致；PG（字符串 = TEXT、bool = BOOLEAN、
/// 时间 = TIMESTAMPTZ、int = INTEGER，Guid = uuid）。
/// PG 的 Guid 列采用 uuid 而非 TEXT：初始 EF 迁移与 Npgsql 的 Guid 参数都是 uuid，
/// 若写成 TEXT，EF 的 WHERE "UserId" = @p 会触发 "operator does not exist: text = uuid"。
/// </summary>
public sealed class IdentityDatabaseMigrations : IModuleDatabaseMigrations
{
    public IReadOnlyList<ModuleMigrationStep> Steps { get; } =
    [
        new ModuleMigrationStep(1, "CreateIdentityExtensionTables", BuildStep1(Sqlite), BuildStep1(Postgres)),
        new ModuleMigrationStep(2, "AddMenuPermissionColumns", BuildStep2(Sqlite), BuildStep2(Postgres)),
        new ModuleMigrationStep(3, "SeedButtonPermissions", BuildSeedPermissions(Sqlite, ButtonPermissions), BuildSeedPermissions(Postgres, ButtonPermissions)),
        new ModuleMigrationStep(4, "SeedPlatformMenusAndPermissions", BuildStep4(Sqlite), BuildStep4(Postgres))
    ];

    private const string Sqlite = "sqlite";
    private const string Postgres = "pg";

    private enum ColKind { Guid, Text, Bool, Time, Int }

    private sealed record Col(string Name, ColKind Kind, bool Nullable, bool Pk);

    private sealed record TableDef(string Name, Col[] Cols, (string Name, string Column)[] Indexes);

    private static Col C(string name, ColKind kind, bool nullable = false, bool pk = false) =>
        new(name, kind, nullable, pk);

    private static string Q(string identifier) => $"\"{identifier}\"";

    private static string SqlType(ColKind kind, string provider) => provider == Postgres
        ? kind switch
        {
            ColKind.Guid => "uuid",
            ColKind.Bool => "BOOLEAN",
            ColKind.Time => "TIMESTAMPTZ",
            ColKind.Int => "INTEGER",
            _ => "TEXT"
        }
        : kind switch
        {
            ColKind.Bool => "INTEGER",
            ColKind.Int => "INTEGER",
            _ => "TEXT"
        };

    /// <summary>Sequence 1：8 张新表 + 索引。</summary>
    private static readonly TableDef[] Tables =
    [
        new("Tenants",
            [C("Id", ColKind.Guid, pk: true), C("Name", ColKind.Text),
             C("Title", ColKind.Text, nullable: true), C("Domain", ColKind.Text, nullable: true),
             C("IsActive", ColKind.Bool), C("CreatedAtUtc", ColKind.Time)],
            [("IX_Tenants_Name", "Name")]),
        new("TenantUsers",
            [C("Id", ColKind.Guid, pk: true), C("TenantId", ColKind.Guid), C("UserId", ColKind.Guid),
             C("Accepted", ColKind.Bool), C("IsOwner", ColKind.Bool),
             C("InvitedAtUtc", ColKind.Time), C("AcceptedAtUtc", ColKind.Time, nullable: true)],
            [("IX_TenantUsers_TenantId", "TenantId"), ("IX_TenantUsers_UserId", "UserId")]),
        new("UserSessions",
            [C("Id", ColKind.Guid, pk: true), C("UserId", ColKind.Guid),
             C("TenantId", ColKind.Guid, nullable: true), C("IP", ColKind.Text, nullable: true),
             C("DeviceInfo", ColKind.Text, nullable: true), C("CultureName", ColKind.Text, nullable: true),
             C("TimeZone", ColKind.Text, nullable: true), C("NotificationsEnabled", ColKind.Bool),
             C("RefreshToken", ColKind.Text, nullable: true), C("StartedOnUtc", ColKind.Time),
             C("RenewedOnUtc", ColKind.Time), C("RevokedAtUtc", ColKind.Time, nullable: true)],
            [("IX_UserSessions_UserId", "UserId"), ("IX_UserSessions_RefreshToken", "RefreshToken")]),
        new("UserPreferences",
            [C("Id", ColKind.Guid, pk: true), C("UserId", ColKind.Guid), C("Culture", ColKind.Text),
             C("TimeZone", ColKind.Text), C("Theme", ColKind.Text),
             C("AccentColor", ColKind.Text, nullable: true), C("NotificationsEnabled", ColKind.Bool),
             C("ModifiedAtUtc", ColKind.Time)],
            [("IX_UserPreferences_UserId", "UserId")]),
        new("Notifications",
            [C("Id", ColKind.Guid, pk: true), C("UserId", ColKind.Guid), C("Title", ColKind.Text),
             C("Message", ColKind.Text), C("IsRead", ColKind.Bool),
             C("PageUrl", ColKind.Text, nullable: true), C("CreatedAtUtc", ColKind.Time),
             C("ReadAtUtc", ColKind.Time, nullable: true)],
            [("IX_Notifications_UserId", "UserId")]),
        new("PushNotificationSubscriptions",
            [C("Id", ColKind.Guid, pk: true), C("DeviceId", ColKind.Text), C("Platform", ColKind.Text),
             C("PushChannel", ColKind.Text), C("Endpoint", ColKind.Text, nullable: true),
             C("P256dh", ColKind.Text, nullable: true), C("Auth", ColKind.Text, nullable: true),
             C("UserId", ColKind.Guid, nullable: true), C("CreatedAtUtc", ColKind.Time)],
            [("IX_PushNotificationSubscriptions_DeviceId", "DeviceId"),
             ("IX_PushNotificationSubscriptions_UserId", "UserId")]),
        new("PermissionDefinitions",
            [C("Id", ColKind.Guid, pk: true), C("Code", ColKind.Text), C("Name", ColKind.Text),
             C("ModuleId", ColKind.Text, nullable: true), C("MenuPath", ColKind.Text, nullable: true),
             C("Kind", ColKind.Text)],
            [("IX_PermissionDefinitions_Code", "Code")]),
        new("RolePermissions",
            [C("Id", ColKind.Guid, pk: true), C("RoleId", ColKind.Guid), C("Code", ColKind.Text)],
            [("IX_RolePermissions_RoleId", "RoleId"), ("IX_RolePermissions_Code", "Code")])
    ];

    /// <summary>
    /// Sequence 3 的按钮权限种子（Code / 中文名 / ModuleId），Kind 一律 "button"。
    /// 与 manifest.json 的 permissions 数组、/api/identity/permissions 目录保持一致。
    /// </summary>
    private static readonly (string Code, string Name, string ModuleId)[] ButtonPermissions =
    [
        ("identity.user.create", "创建用户", "identity"),
        ("identity.user.read", "查看用户", "identity"),
        ("identity.user.update", "编辑用户", "identity"),
        ("identity.user.delete", "删除用户", "identity"),
        ("identity.role.create", "创建角色", "identity"),
        ("identity.role.update", "编辑角色", "identity"),
        ("identity.role.delete", "删除角色", "identity"),
        ("identity.menu.create", "创建菜单", "identity"),
        ("identity.menu.update", "编辑菜单", "identity"),
        ("identity.menu.delete", "删除菜单", "identity"),
        ("module.upload", "上传模块", "module"),
        ("module.pack", "打包模块", "module"),
        ("module.download", "下载模块", "module"),
        ("module.load", "加载模块", "module"),
        ("module.unload", "卸载模块", "module")
    ];

    /// <summary>
    /// Sequence 4 补充的平台内置权限（系统设置、租户管理、模块管理），用于已有数据库增量幂等补齐。
    /// </summary>
    private static readonly (string Code, string Name, string ModuleId)[] PlatformPermissions =
    [
        ("system.settings.read", "查看系统设置", "system"),
        ("system.settings.update", "修改系统设置", "system"),
        ("tenant.create", "创建租户", "tenant"),
        ("tenant.update", "编辑租户", "tenant"),
        ("tenant.invite", "邀请租户成员", "tenant"),
        ("module.upload", "上传模块", "module"),
        ("module.pack", "打包模块", "module"),
        ("module.download", "下载模块", "module"),
        ("module.load", "加载模块", "module"),
        ("module.unload", "卸载模块", "module")
    ];

    // SQLite 无内置 UUID 函数：用 randomblob 生成 32 位十六进制并拼成标准 8-4-4-4-12 形式，
    // 保证 EF Core（Guid.Parse / GetGuid）可读且每行不同；volatile 函数在 SELECT 列表里逐行求值。
    private const string SqliteNewId =
        "printf('%s-%s-%s-%s-%s'," +
        "substr(lower(hex(randomblob(16))),1,8)," +
        "substr(lower(hex(randomblob(16))),9,4)," +
        "substr(lower(hex(randomblob(16))),13,4)," +
        "substr(lower(hex(randomblob(16))),17,4)," +
        "substr(lower(hex(randomblob(16))),21,12))";

    // PG 不依赖 pgcrypto 扩展：md5(random() || clock_timestamp()) 生成的 32 位 hex 可直接 cast 成 uuid。
    private const string PgNewId = "md5(random()::text || clock_timestamp()::text)::uuid";

    private static string BuildStep1(string provider)
    {
        var statements = new List<string>();
        foreach (var table in Tables)
        {
            statements.Add(BuildCreateTable(table, provider));
            foreach (var index in table.Indexes)
                statements.Add($"CREATE INDEX IF NOT EXISTS {Q(index.Name)} ON {Q(table.Name)} ({Q(index.Column)})");
        }
        return Join(statements);
    }

    private static string BuildCreateTable(TableDef table, string provider)
    {
        var columns = table.Cols.Select(c =>
            $"    {Q(c.Name)} {SqlType(c.Kind, provider)}{(c.Pk || !c.Nullable ? " NOT NULL" : " NULL")}");
        var body = string.Join(",\n", columns);
        return $"CREATE TABLE IF NOT EXISTS {Q(table.Name)} (\n{body},\n" +
               $"    CONSTRAINT {Q("PK_" + table.Name)} PRIMARY KEY ({Q("Id")})\n)";
    }

    /// <summary>
    /// Sequence 2：Menus 表（已由 EF 初始迁移创建）追加 3 列。
    /// SQLite 不支持 ADD COLUMN IF NOT EXISTS，每条 ALTER 写成独立语句，
    /// 由 runner 捕获 duplicate column 异常后继续；PG 用 ADD COLUMN IF NOT EXISTS。
    /// Kind 带 DEFAULT 'menu'，使已有行自动回填（非空约束得以满足）。
    /// </summary>
    private static string BuildStep2(string provider)
    {
        var ifNotExists = provider == Postgres ? "IF NOT EXISTS " : string.Empty;
        return Join(
        [
            $"ALTER TABLE {Q("Menus")} ADD COLUMN {ifNotExists}{Q("ModuleId")} TEXT NULL",
            $"ALTER TABLE {Q("Menus")} ADD COLUMN {ifNotExists}{Q("PermissionCode")} TEXT NULL",
            $"ALTER TABLE {Q("Menus")} ADD COLUMN {ifNotExists}{Q("Kind")} TEXT NOT NULL DEFAULT 'menu'"
        ]);
    }

    /// <summary>
    /// Sequence 4：幂等补齐平台菜单（/system/settings）与平台内置权限，并授予系统管理员角色。
    /// </summary>
    private static string BuildStep4(string provider)
    {
        var newId = provider == Postgres ? PgNewId : SqliteNewId;
        var boolTrue = provider == Postgres ? "TRUE" : "1";
        var statements = new List<string>
        {
            $"INSERT INTO {Q("Menus")} " +
            $"({Q("Id")},{Q("Name")},{Q("Path")},{Q("Icon")},{Q("ParentId")},{Q("Sort")},{Q("IsVisible")},{Q("ModuleId")},{Q("PermissionCode")},{Q("Kind")}) " +
            $"SELECT {newId}, '系统设置', '/system/settings', 'Settings', NULL, 950, {boolTrue}, NULL, NULL, 'menu' " +
            $"WHERE NOT EXISTS (SELECT 1 FROM {Q("Menus")} WHERE {Q("Path")} = '/system/settings')",

            $"INSERT INTO {Q("RoleMenus")} ({Q("Id")},{Q("RoleId")},{Q("MenuId")}) " +
            $"SELECT {newId}, r.{Q("Id")}, m.{Q("Id")} " +
            $"FROM {Q("Roles")} r CROSS JOIN {Q("Menus")} m " +
            $"WHERE r.{Q("IsSystem")} = {boolTrue} AND r.{Q("Name")} = '管理员' AND m.{Q("Path")} = '/system/settings' " +
            $"AND NOT EXISTS (SELECT 1 FROM {Q("RoleMenus")} rm " +
            $"WHERE rm.{Q("RoleId")} = r.{Q("Id")} AND rm.{Q("MenuId")} = m.{Q("Id")})"
        };

        AppendSeedPermissions(statements, provider, PlatformPermissions);
        return Join(statements);
    }

    /// <summary>
    /// 幂等种子权限定义 + 把系统管理员角色授予全部权限码。
    /// 不写任何具体 Guid：主键用数据库函数生成，RoleId 用子查询按 Name='管理员' 查找。
    /// </summary>
    private static string BuildSeedPermissions(string provider, (string Code, string Name, string ModuleId)[] items)
    {
        var statements = new List<string>();
        AppendSeedPermissions(statements, provider, items);
        return Join(statements);
    }

    private static void AppendSeedPermissions(List<string> statements, string provider, (string Code, string Name, string ModuleId)[] items)
    {
        var newId = provider == Postgres ? PgNewId : SqliteNewId;
        var systemTrue = provider == Postgres ? "TRUE" : "1";

        foreach (var (code, name, moduleId) in items)
        {
            statements.Add(
                $"INSERT INTO {Q("PermissionDefinitions")} " +
                $"({Q("Id")},{Q("Code")},{Q("Name")},{Q("ModuleId")},{Q("MenuPath")},{Q("Kind")}) " +
                $"SELECT {newId}, '{code}', '{name}', '{moduleId}', NULL, 'button' " +
                $"WHERE NOT EXISTS (SELECT 1 FROM {Q("PermissionDefinitions")} WHERE {Q("Code")} = '{code}')");
        }

        statements.Add(
            $"INSERT INTO {Q("RolePermissions")} ({Q("Id")},{Q("RoleId")},{Q("Code")}) " +
            $"SELECT {newId}, r.{Q("Id")}, p.{Q("Code")} " +
            $"FROM {Q("Roles")} r CROSS JOIN {Q("PermissionDefinitions")} p " +
            $"WHERE r.{Q("IsSystem")} = {systemTrue} AND r.{Q("Name")} = '管理员' " +
            $"AND NOT EXISTS (SELECT 1 FROM {Q("RolePermissions")} rp " +
            $"WHERE rp.{Q("RoleId")} = r.{Q("Id")} AND rp.{Q("Code")} = p.{Q("Code")})");
    }

    /// <summary>runner 按 ";" 逐条执行，故每条语句都自带分号并用换行分隔。</summary>
    private static string Join(IEnumerable<string> statements) =>
        string.Join("\n", statements.Select(s => s + ";"));
}
