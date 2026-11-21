using System.Collections.Concurrent;

namespace BlazorBoilerplate.ModuleAbstractions;

/// <summary>页面微件插槽描述。</summary>
public sealed record WidgetDescriptor(
    string Zone,
    Type ComponentType,
    string ModuleId,
    int Order = ModuleStartupDefaults.DefaultOrder);

/// <summary>声明当前微件依赖的前置微件类型，渲染时自动连带加载。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DependentWidgetsAttribute(params Type[] widgetTypes) : Attribute
{
    public Type[] WidgetTypes { get; } = widgetTypes;
}

/// <summary>前端微件插槽注册表。</summary>
public static class WidgetZoneRegistry
{
    private static readonly ConcurrentDictionary<string, WidgetDescriptor> Entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>全局插槽预览开关（开启后页面高亮展示插槽名称）。</summary>
    public static bool PreviewEnabled { get; set; }

    /// <summary>微件注册表变更通知（用于驱动已渲染的 WidgetZone 即时刷新）。</summary>
    public static event Action? Changed;

    /// <summary>注册组件到指定插槽。</summary>
    public static void Register(string zone, Type componentType, string moduleId, int order = ModuleStartupDefaults.DefaultOrder)
    {
        if (string.IsNullOrWhiteSpace(zone) || componentType is null)
            return;
        var key = $"{moduleId}:{zone}:{componentType.FullName}";
        Entries[key] = new WidgetDescriptor(zone.Trim(), componentType, moduleId, order);
        Changed?.Invoke();
    }

    /// <summary>按插槽名获取按 Order 升序排列的微件（自动展开 DependentWidgetsAttribute 依赖且去重）。</summary>
    public static IReadOnlyList<WidgetDescriptor> GetWidgets(string zone)
    {
        var primary = Entries.Values
            .Where(w => string.Equals(w.Zone, zone, StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => w.Order)
            .ThenBy(w => w.ComponentType.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (primary.Length == 0)
            return primary;

        var result = new List<WidgetDescriptor>(primary.Length);
        var seen = new HashSet<Type>();

        void AddWithDependencies(WidgetDescriptor desc)
        {
            foreach (var attr in desc.ComponentType.GetCustomAttributes(typeof(DependentWidgetsAttribute), inherit: false).OfType<DependentWidgetsAttribute>())
            {
                foreach (var depType in attr.WidgetTypes)
                {
                    if (seen.Add(depType))
                        result.Add(new WidgetDescriptor(desc.Zone, depType, desc.ModuleId, desc.Order - 1));
                }
            }
            if (seen.Add(desc.ComponentType))
                result.Add(desc);
        }

        foreach (var item in primary)
            AddWithDependencies(item);

        return result;
    }

    /// <summary>注销指定模块注册的全部微件。</summary>
    public static void UnregisterModule(string moduleId)
    {
        var removed = false;
        foreach (var key in Entries.Keys.Where(k => k.StartsWith(moduleId + ":", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            if (Entries.TryRemove(key, out _))
                removed = true;
        }
        if (removed)
            Changed?.Invoke();
    }
}
