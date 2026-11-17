namespace BlazorBoilerplate.Application.Localization;

/// <summary>
/// 多语言默认值与约定常量：统一以英语（en-US）作为中间回退语言。
/// </summary>
public static class LocalizationDefaults
{
    public const string DefaultFallbackCulture = "en-US";
    public const string DefaultChineseCulture = "zh-CN";
    public const string PlatformModuleId = "platform";

    public static readonly IReadOnlyList<LanguageInfo> BuiltInLanguages =
    [
        new LanguageInfo(DefaultFallbackCulture, "English", "English (US)", IsRtl: false, IsDefault: false, IsEnabled: true, DisplayOrder: 10),
        new LanguageInfo(DefaultChineseCulture, "简体中文", "简体中文 (中国)", IsRtl: false, IsDefault: true, IsEnabled: true, DisplayOrder: 20)
    ];
}

public sealed record LanguageInfo(
    string Culture,
    string Name,
    string DisplayName,
    bool IsRtl = false,
    bool IsDefault = false,
    bool IsEnabled = true,
    int DisplayOrder = 0);

public sealed record LocaleResourceEntry(
    string ModuleId,
    string Culture,
    string Key,
    string Value);

/// <summary>
/// 模块多语言资源注册中心（进程内单例，由宿主与动态模块共享）：
/// 模块加载时注册各自的中英文内置资源，卸载时按 moduleId 清理，避免 ALC 内存泄漏。
/// </summary>
public interface ILocalizationRegistry
{
    event Action? Changed;

    void RegisterModuleResources(
        string moduleId,
        string culture,
        IReadOnlyDictionary<string, string> resources);

    void RegisterModuleResources(
        string moduleId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> resourcesByCulture);

    void UnregisterModule(string moduleId);

    IReadOnlyDictionary<string, string> GetMergedResources(string culture);

    IReadOnlyList<LocaleResourceEntry> GetAllEntries();
}

/// <summary>
/// 业务模块可选实现的本地化资源提供器（也可直接在模块目录放置 localization/{culture}.json）。
/// </summary>
public interface IModuleLocalizationProvider
{
    string ModuleId { get; }
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> GetResources();
}

/// <summary>
/// 可选持久化多语言存储（由后续独立安装的 Localization 管理模块提供实现，未安装时回退内置注册表）。
/// </summary>
public interface ILocalizationStore
{
    Task<IReadOnlyList<LanguageInfo>> GetLanguagesAsync(CancellationToken cancellationToken = default);
    Task<string?> TryGetResourceAsync(string culture, string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetResourcesAsync(string culture, CancellationToken cancellationToken = default);
    Task SyncModuleResourcesAsync(string moduleId, IReadOnlyList<LocaleResourceEntry> entries, CancellationToken cancellationToken = default);
}

/// <summary>
/// 前后端统一多语言读取服务：
/// 解析顺序为 当前文化 -> 父文化（如 zh-CN -> zh） -> 中间回退语言 en-US -> key 本身。
/// </summary>
public interface ILocalizationService
{
    string CurrentCulture { get; }
    string FallbackCulture { get; }

    string this[string key] { get; }

    string GetString(string key, params object[] args);
    string GetStringForCulture(string key, string culture, params object[] args);

    Task<string> GetStringAsync(string key, CancellationToken cancellationToken = default, params object[] args);
    Task<IReadOnlyDictionary<string, string>> GetAllStringsAsync(string? culture = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LanguageInfo>> GetSupportedLanguagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 词条变更后的缓存失效入口（由 Localization 管理模块保存成功后调用）。
    /// 前端实现清进程级词典、递增版本并广播；后端实现按需清 Store 快照。
    /// culture 为空表示失效全部文化。
    /// </summary>
    Task InvalidateAsync(string? culture = null, CancellationToken cancellationToken = default);
}

