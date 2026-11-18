using System.Collections.Concurrent;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Modules.Localization.Application;

namespace BlazorBoilerplate.Modules.Localization.Infrastructure;

/// <summary>
/// 模块内进程级缓存（注册为模块 DI 单例，随模块卸载自动释放）：
/// 缓存已启用语言清单与按文化聚合的词典，写操作后立即失效。
/// </summary>
public sealed class LocalizationStoreCache : ILocalizationCacheInvalidator
{
    private volatile IReadOnlyList<LanguageInfo>? _languages;
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _resourcesByCulture =
        new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetLanguages(out IReadOnlyList<LanguageInfo> languages)
    {
        var snapshot = _languages;
        if (snapshot is not null)
        {
            languages = snapshot;
            return true;
        }

        languages = [];
        return false;
    }

    public void SetLanguages(IReadOnlyList<LanguageInfo> languages) => _languages = languages;

    public bool TryGetResources(string culture, out IReadOnlyDictionary<string, string> resources) =>
        _resourcesByCulture.TryGetValue(culture, out resources!);

    public void SetResources(string culture, IReadOnlyDictionary<string, string> resources) =>
        _resourcesByCulture[culture] = resources;

    public void InvalidateAll()
    {
        _languages = null;
        _resourcesByCulture.Clear();
    }

    public void InvalidateCulture(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            _resourcesByCulture.Clear();
            return;
        }

        _resourcesByCulture.TryRemove(culture.Trim(), out _);
    }
}

