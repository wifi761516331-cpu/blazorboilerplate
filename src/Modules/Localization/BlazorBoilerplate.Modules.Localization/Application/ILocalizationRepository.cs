using BlazorBoilerplate.Modules.Localization.Domain;

namespace BlazorBoilerplate.Modules.Localization.Application;

public interface ILocalizationCacheInvalidator
{
    void InvalidateAll();
    void InvalidateCulture(string? culture);
}

public interface ILocalizationRepository
{
    Task<IReadOnlyList<Language>> GetAllLanguagesAsync(CancellationToken ct = default);
    Task<Language?> GetLanguageByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> CultureExistsAsync(string culture, CancellationToken ct = default);
    void AddLanguage(Language entity);
    Task ClearDefaultLanguageAsync(Guid? excludeId, CancellationToken ct = default);
    Task DeleteLanguageAndResourcesAsync(Language entity, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetDistinctModulesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LocaleResource>> GetResourcesForComparisonAsync(
        string fallbackCulture,
        string targetCulture,
        string? moduleId,
        CancellationToken ct = default);
    Task<IReadOnlyList<LocaleResource>> GetResourcesByCultureAsync(
        string culture,
        string? moduleId,
        CancellationToken ct = default);
    Task<LocaleResource?> GetResourceAsync(
        string moduleId,
        string culture,
        string key,
        CancellationToken ct = default);
    void AddResource(LocaleResource entity);
    void RemoveResource(LocaleResource entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}

