using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Web.Shared.Components;

// 参数与静态图标清单在 .razor.cs；@using Bit.BlazorUI 与 SVG 标记留在 .razor。
public partial class AppIcon
{
    [Parameter] public string Name { get; set; } = string.Empty;
    [Parameter] public string? Class { get; set; }

    private static readonly HashSet<string> CustomSvgIcons = new(StringComparer.Ordinal)
    {
        "Home", "Shop", "Product", "People", "User", "SecurityGroup", "Shield",
        "EditList", "List", "Puzzle", "Module", "Grid", "Dashboard",
        "Chart", "BarChartVerticalFill", "Pie", "Donut", "Settings",
        "Bell", "Notification", "Sunny", "ClearNight", "CollapseMenu", "Menu",
        "Back", "Refresh", "Add", "Org", "Tenant", "Info",
        "ChevronDown", "ChevronRight", "ChevronLeft", "Contact", "Profile",
        "Globe", "Language", "Clock", "WorldClock", "Upload", "CloudUpload",
        "RingerOff", "SignOut", "AddFriend", "Mail", "Edit", "Check",
        "Warning", "Error", "Close", "Attach"
    };

    private static bool IsCustomSvg(string name) => CustomSvgIcons.Contains(name);
}
