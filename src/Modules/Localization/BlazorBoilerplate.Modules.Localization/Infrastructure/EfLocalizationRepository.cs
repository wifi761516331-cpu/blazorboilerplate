using BlazorBoilerplate.Modules.Localization.Application;
using BlazorBoilerplate.Modules.Localization.Domain;
using BlazorBoilerplate.Modules.Localization.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Modules.Localization.Infrastructure;

public sealed class EfLocalizationRepository(LocalizationDbContext db) : ILocalizationRepository
{
    public async Task<IReadOnlyList<Language>> GetAllLanguagesAsync(CancellationToken ct = default) =>
        await db.Languages
            .AsNoTracking()
            .OrderBy(l => l.DisplayOrder)
            .ThenBy(l => l.Culture)
            .ToListAsync(ct);

    public Task<Language?> GetLanguageByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Languages.FirstOrDefaultAsync(l => l.Id == id, ct);

    public Task<bool> CultureExistsAsync(string culture, CancellationToken ct = default) =>
        db.Languages.AnyAsync(l => l.Culture == culture, ct);

    public void AddLanguage(Language entity) => db.Languages.Add(entity);

    public async Task ClearDefaultLanguageAsync(Guid? excludeId, CancellationToken ct = default)
    {
        var currentDefaults = await db.Languages
            .Where(l => l.IsDefault && (excludeId == null || l.Id != excludeId.Value))
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var lang in currentDefaults)
        {
            lang.IsDefault = false;
            lang.UpdatedAt = now;
        }
    }

    public async Task DeleteLanguageAndResourcesAsync(Language entity, CancellationToken ct = default)
    {
        var related = await db.Resources
            .Where(r => r.Culture == entity.Culture)
            .ToListAsync(ct);

        if (related.Count > 0)
            db.Resources.RemoveRange(related);

        db.Languages.Remove(entity);
    }

    public async Task<IReadOnlyList<string>> GetDistinctModulesAsync(CancellationToken ct = default) =>
        await db.Resources
            .AsNoTracking()
            .Select(r => r.ModuleId)
            .Distinct()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LocaleResource>> GetResourcesForComparisonAsync(
        string fallbackCulture,
        string targetCulture,
        string? moduleId,
        CancellationToken ct = default)
    {
        var query = db.Resources.AsNoTracking()
            .Where(r => r.Culture == fallbackCulture || r.Culture == targetCulture);

        if (!string.IsNullOrWhiteSpace(moduleId))
        {
            var mod = moduleId.Trim();
            query = query.Where(r => r.ModuleId == mod);
        }

        return await query.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LocaleResource>> GetResourcesByCultureAsync(
        string culture,
        string? moduleId,
        CancellationToken ct = default)
    {
        var query = db.Resources.Where(r => r.Culture == culture);
        if (!string.IsNullOrWhiteSpace(moduleId))
        {
            var mod = moduleId.Trim();
            query = query.Where(r => r.ModuleId == mod);
        }

        return await query.ToListAsync(ct);
    }

    public Task<LocaleResource?> GetResourceAsync(
        string moduleId,
        string culture,
        string key,
        CancellationToken ct = default) =>
        db.Resources.FirstOrDefaultAsync(
            r => r.ModuleId == moduleId && r.Culture == culture && r.Key == key,
            ct);

    public void AddResource(LocaleResource entity) => db.Resources.Add(entity);

    public void RemoveResource(LocaleResource entity) => db.Resources.Remove(entity);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

