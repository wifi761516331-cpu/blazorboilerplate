using System.Collections.Concurrent;
using System.Globalization;
using BlazorBoilerplate.Application.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Infrastructure.Localization;

/// <summary>
/// 平台默认多语言服务：
/// 1) 优先查询可选的持久化 <see cref="ILocalizationStore"/>（如已安装 Localization 管理模块）；
///    其读取结果写入进程内快照，供同步索引器 L[key] 复用，保证同步/异步语义一致；
/// 2) 回退到 <see cref="ILocalizationRegistry"/> 内置模块资源；
/// 3) 按 当前文化 -> 父文化 -> 中间回退语言（en-US） -> Key 本身 逐级回退。
/// </summary>
public sealed class PlatformLocalizationService(
    ILocalizationRegistry registry,
    IServiceProvider serviceProvider) : ILocalizationService
{
    // ILocalizationStore 是可选异步接口，同步索引器无法 await；
    // 由异步路径（GetStringAsync / GetAllStringsAsync）读取后回填，同步路径按同构回退链读取。
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _storeSnapshot =
        new(StringComparer.OrdinalIgnoreCase);

    public string CurrentCulture
    {
        get
        {
            var name = CultureInfo.CurrentUICulture.Name;
            return string.IsNullOrWhiteSpace(name) ? LocalizationDefaults.DefaultChineseCulture : name;
        }
    }

    public string FallbackCulture => LocalizationDefaults.DefaultFallbackCulture;

    public string this[string key] => GetString(key);

    public string GetString(string key, params object[] args) =>
        GetStringForCulture(key, CurrentCulture, args);

    public string GetStringForCulture(string key, string culture, params object[] args)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var raw = ResolveFromStoreSnapshot(key, culture)
                  ?? ResolveFromRegistry(key, culture)
                  ?? key;
        return FormatValue(raw, args);
    }

    public async Task<string> GetStringAsync(string key, CancellationToken cancellationToken = default, params object[] args)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var culture = CurrentCulture;
        var store = serviceProvider.GetService<ILocalizationStore>();
        if (store is not null)
        {
            var value = await TryResolveFromStoreAsync(store, key, culture, cancellationToken);
            if (value is not null)
            {
                CacheStoreValue(culture, key, value);
                return FormatValue(value, args);
            }
        }

        return GetStringForCulture(key, culture, args);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllStringsAsync(
        string? culture = null,
        CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(culture) ? CurrentCulture : culture.Trim();
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 先铺入中间回退语言 en-US，再铺父文化，最后铺目标文化，保证缺失项自动回退 en-US
        MergeInto(merged, registry.GetMergedResources(FallbackCulture));

        var dash = target.IndexOf('-');
        if (dash > 0)
            MergeInto(merged, registry.GetMergedResources(target[..dash]));

        if (!string.Equals(target, FallbackCulture, StringComparison.OrdinalIgnoreCase))
            MergeInto(merged, registry.GetMergedResources(target));

        var store = serviceProvider.GetService<ILocalizationStore>();
        if (store is not null)
        {
            var fallbackFromStore = await store.GetResourcesAsync(FallbackCulture, cancellationToken);
            MergeInto(merged, fallbackFromStore);
            CacheStoreResources(FallbackCulture, fallbackFromStore);

            if (!string.Equals(target, FallbackCulture, StringComparison.OrdinalIgnoreCase))
            {
                var targetFromStore = await store.GetResourcesAsync(target, cancellationToken);
                MergeInto(merged, targetFromStore);
                CacheStoreResources(target, targetFromStore);
            }
        }

        return merged;
    }

    public async Task<IReadOnlyList<LanguageInfo>> GetSupportedLanguagesAsync(CancellationToken cancellationToken = default)
    {
        var store = serviceProvider.GetService<ILocalizationStore>();
        if (store is not null)
        {
            var fromStore = await store.GetLanguagesAsync(cancellationToken);
            if (fromStore.Count > 0)
                return fromStore;
        }

        return LocalizationDefaults.BuiltInLanguages;
    }

    /// <summary>
    /// 词条变更后的失效入口：清本 scoped 实例的 Store 快照，使后续同步索引器 L[key] 重新读库。
    /// registry 内置资源不变，无需处理；进程级前端缓存由 FrontendLocalizationService 自行失效。
    /// </summary>
    public Task InvalidateAsync(string? culture = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(culture))
            _storeSnapshot.Clear();
        else
            _storeSnapshot.TryRemove(culture.Trim(), out _);

        return Task.CompletedTask;
    }

    private string? ResolveFromStoreSnapshot(string key, string culture)
    {
        var normalized = string.IsNullOrWhiteSpace(culture) ? CurrentCulture : culture.Trim();
        if (_storeSnapshot.TryGetValue(normalized, out var exact) && exact.TryGetValue(key, out var value))
            return value;

        var dash = normalized.IndexOf('-');
        if (dash > 0 && _storeSnapshot.TryGetValue(normalized[..dash], out var parent) && parent.TryGetValue(key, out var parentValue))
            return parentValue;

        if (_storeSnapshot.TryGetValue(FallbackCulture, out var fallback) && fallback.TryGetValue(key, out var fallbackValue))
            return fallbackValue;

        return null;
    }

    private void CacheStoreValue(string culture, string key, string value)
    {
        var bucket = _storeSnapshot.GetOrAdd(culture.Trim(), _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        lock (bucket) bucket[key] = value;
    }

    private void CacheStoreResources(string culture, IReadOnlyDictionary<string, string> resources)
    {
        var bucket = _storeSnapshot.GetOrAdd(culture.Trim(), _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        lock (bucket)
            foreach (var (key, value) in resources)
                bucket[key] = value;
    }

    private string? ResolveFromRegistry(string key, string culture)
    {
        var normalized = string.IsNullOrWhiteSpace(culture) ? CurrentCulture : culture.Trim();
        if (registry.GetMergedResources(normalized).TryGetValue(key, out var exact))
            return exact;

        var dash = normalized.IndexOf('-');
        if (dash > 0 && registry.GetMergedResources(normalized[..dash]).TryGetValue(key, out var parent))
            return parent;

        if (!string.Equals(normalized, FallbackCulture, StringComparison.OrdinalIgnoreCase) &&
            registry.GetMergedResources(FallbackCulture).TryGetValue(key, out var fallback))
            return fallback;

        return null;
    }

    private async Task<string?> TryResolveFromStoreAsync(
        ILocalizationStore store,
        string key,
        string culture,
        CancellationToken cancellationToken)
    {
        var exact = await store.TryGetResourceAsync(culture, key, cancellationToken);
        if (exact is not null) return exact;

        var dash = culture.IndexOf('-');
        if (dash > 0)
        {
            var parent = await store.TryGetResourceAsync(culture[..dash], key, cancellationToken);
            if (parent is not null) return parent;
        }

        if (!string.Equals(culture, FallbackCulture, StringComparison.OrdinalIgnoreCase))
            return await store.TryGetResourceAsync(FallbackCulture, key, cancellationToken);

        return null;
    }

    private static void MergeInto(Dictionary<string, string> target, IReadOnlyDictionary<string, string> source)
    {
        foreach (var (key, value) in source)
            target[key] = value;
    }

    private static string FormatValue(string template, object[] args)
    {
        if (args.Length == 0)
            return template;

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}

