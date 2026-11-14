namespace BlazorBoilerplate.WebHost.Services;

/// <summary>
/// 概览页 Dashboard Widget 的展示数据（mock）。
/// 页面布局与样式移植自 Bluebird / bitplatform Boilerplate；此处只提供展示用的假数据，
/// 后续接入真实接口时替换本静态源即可，页面无需改动。
/// </summary>
public static class DashboardMockData
{
    public sealed record CategoryStat(string Name, int Count, string Color);
    public sealed record Activity(string Text, string Time, string Icon);
    public sealed record QuickLink(string Text, string Href, string Icon);

    /// <summary>分类商品数量（对应 Bluebird DashboardDemoData 的 5 个分类）。</summary>
    public static IReadOnlyList<CategoryStat> Categories { get; } =
    [
        new("外设", 8, "#0f6cbd"),
        new("音频", 8, "#4bc0c0"),
        new("显示器", 19, "#ffcd56"),
        new("家具", 8, "#ff6384"),
        new("配件", 7, "#ff9124")
    ];

    public static IReadOnlyList<Activity> Activities { get; } =
    [
        new("Catalog 模块已按加载顺序 100 激活", "2 分钟前", "Puzzle"),
        new("Identity 模块（必要模块）运行中", "5 分钟前", "SecurityGroup"),
        new("管理员账户与系统角色初始化完成", "12 分钟前", "People"),
        new("菜单树已写入数据库", "18 分钟前", "EditList"),
        new("平台进入 Normal 模式", "刚刚", "Info")
    ];

    public static IReadOnlyList<QuickLink> QuickLinks { get; } =
    [
        new("模块管理", "/modules", "Puzzle"),
        new("商品目录", "/catalog", "Shop"),
        new("用户管理", "/identity/users", "People"),
        new("角色管理", "/identity/roles", "SecurityGroup"),
        new("菜单管理", "/identity/menus", "EditList")
    ];

    public const int MockUserCount = 128;
    public const int MockActiveModuleCount = 2;
    public const int MockCategoryTotal = 50;
}
