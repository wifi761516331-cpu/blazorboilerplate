namespace BlazorBoilerplate.ModuleAbstractions;

/// <summary>模块前端菜单条目。模块在宿主加载时（静态引用或动态 ALC 初始化）注册。</summary>
public sealed record ModuleFrontendMenu(string Text, string Route, string? Icon = null);

/// <summary>
/// 模块前端注册表。批次 1：由静态引用的模块程序集在加载时通过模块初始化器登记；
/// 动态前端（运行时拖入 RCL 即出现页面）见 docs/ROADMAP.md，属于技能标注需单独 POC 的能力。
/// </summary>
public static class ModuleFrontendRegistry
{
    private static readonly List<ModuleFrontendMenu> Menus = [];

    public static IReadOnlyList<ModuleFrontendMenu> All => Menus;

    public static void Register(ModuleFrontendMenu menu)
    {
        lock (Menus)
        {
            if (Menus.Any(m => m.Route.Equals(menu.Route, StringComparison.OrdinalIgnoreCase)))
                return;
            Menus.Add(menu);
        }
    }
}
