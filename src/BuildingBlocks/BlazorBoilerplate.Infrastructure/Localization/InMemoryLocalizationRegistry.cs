using System.Collections.Concurrent;
using System.Linq;
using BlazorBoilerplate.Application.Localization;

namespace BlazorBoilerplate.Infrastructure.Localization;

/// <summary>
/// 线程安全的进程内多语言资源注册中心：
/// 按 moduleId -> culture -> key 隔离存储，按需生成合并视图；模块卸载时按 moduleId 清理。
/// </summary>
public sealed class InMemoryLocalizationRegistry : ILocalizationRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Dictionary<string, string>>> _modules =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _mergedCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 模块注册序号：首次注册时分配，决定同 key 的覆盖优先级。序号越大越晚注册、优先级越高。
    /// 平台资源在 DI 初始化时最先注册（序号最小，优先级最低），模块按 ResolveLoadOrder 串行注册，
    /// 因此「后加载模块覆盖先加载模块」「模块覆盖平台内置」是确定行为，而非 ConcurrentDictionary 的哈希顺序。
    /// </summary>
    private readonly ConcurrentDictionary<string, long> _registrationOrder =
        new(StringComparer.OrdinalIgnoreCase);
    private long _nextRegistration;

    public event Action? Changed;

    public void RegisterModuleResources(
        string moduleId,
        string culture,
        IReadOnlyDictionary<string, string> resources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        ArgumentNullException.ThrowIfNull(resources);

        var id = moduleId.Trim();
        // 序号仅在该模块首次注册时分配；同一模块后续按 culture 多次注册沿用同一序号。
        _registrationOrder.TryAdd(id, Interlocked.Increment(ref _nextRegistration));

        var byCulture = _modules.GetOrAdd(
            id,
            _ => new ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase));

        var copy = new Dictionary<string, string>(resources.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in resources)
        {
            if (!string.IsNullOrWhiteSpace(key))
                copy[key.Trim()] = value;
        }

        byCulture[culture.Trim()] = copy;
        _mergedCache.Clear();
        Changed?.Invoke();
    }

    public void RegisterModuleResources(
        string moduleId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> resourcesByCulture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(resourcesByCulture);

        foreach (var (culture, resources) in resourcesByCulture)
        {
            if (!string.IsNullOrWhiteSpace(culture) && resources.Count > 0)
                RegisterModuleResources(moduleId, culture, resources);
        }
    }

    public void UnregisterModule(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
            return;

        var id = moduleId.Trim();
        if (_modules.TryRemove(id, out _))
        {
            // 清序号：模块重新加载时视为新注册、获得更大序号（更高优先级），序号表也不无限增长。
            _registrationOrder.TryRemove(id, out _);
            _mergedCache.Clear();
            Changed?.Invoke();
        }
    }

    public IReadOnlyDictionary<string, string> GetMergedResources(string culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return _mergedCache.GetOrAdd(culture.Trim(), BuildMerged);
    }

    public IReadOnlyList<LocaleResourceEntry> GetAllEntries()
    {
        var list = new List<LocaleResourceEntry>();
        foreach (var (moduleId, byCulture) in _modules)
        {
            foreach (var (culture, entries) in byCulture)
            {
                foreach (var (key, value) in entries)
                    list.Add(new LocaleResourceEntry(moduleId, culture, key, value));
            }
        }
        return list;
    }

    private IReadOnlyDictionary<string, string> BuildMerged(string culture)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // 按注册序号升序铺入：先注册的先写、后注册的后写覆盖同名 key。
        // 于是优先级确定为 平台 < 先加载模块 < 后加载模块，与构建/加载顺序一致、可预测。
        foreach (var (moduleId, _) in _registrationOrder.OrderBy(pair => pair.Value))
        {
            if (_modules.TryGetValue(moduleId, out var byCulture) &&
                byCulture.TryGetValue(culture, out var dict))
            {
                foreach (var (key, value) in dict)
                    merged[key] = value;
            }
        }
        return merged;
    }
}

