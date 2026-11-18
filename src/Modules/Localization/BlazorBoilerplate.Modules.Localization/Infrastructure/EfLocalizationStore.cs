using System.Collections.ObjectModel;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Modules.Localization.Domain;
using BlazorBoilerplate.Modules.Localization.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Modules.Localization.Infrastructure;

/// <summary>
/// 基于 EF Core 的持久化多语言存储实现：
/// 通过宿主 <see cref="ILocalizationStoreBridge"/> 接入平台，支持多数据库与动态模块词条同步。
/// </summary>
public sealed class EfLocalizationStore(
    LocalizationDbContext db,
    LocalizationStoreCache cache) : ILocalizationStore
{
    public async Task<IReadOnlyList<LanguageInfo>> GetLanguagesAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetLanguages(out var cached))
            return cached;

        var list = await db.Languages
            .AsNoTracking()
            .Where(l => l.IsEnabled)
            .OrderBy(l => l.DisplayOrder)
            .ThenBy(l => l.Culture)
            .Select(l => new LanguageInfo(
                l.Culture,
                l.Name,
                l.DisplayName,
                l.IsRtl,
                l.IsDefault,
                l.IsEnabled,
                l.DisplayOrder,
                l.FlagCode))
            .ToListAsync(cancellationToken);

        cache.SetLanguages(list);
        return list;
    }

    public async Task<string?> TryGetResourceAsync(string culture, string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(culture) || string.IsNullOrWhiteSpace(key))
            return null;

        var dict = await GetResourcesAsync(culture, cancellationToken);
        return dict.TryGetValue(key.Trim(), out var value) ? value : null;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetResourcesAsync(string culture, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(culture))
            return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var normalized = culture.Trim();
        if (cache.TryGetResources(normalized, out var cached))
            return cached;

        var rows = await db.Resources
            .AsNoTracking()
            .Where(r => r.Culture == normalized)
            .OrderBy(r => r.ModuleId == LocalizationDefaults.PlatformModuleId ? 0 : 1)
            .ThenBy(r => r.ModuleId)
            .ToListAsync(cancellationToken);

        var dict = new Dictionary<string, string>(rows.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
            dict[row.Key] = row.Value;

        IReadOnlyDictionary<string, string> readonlyDict = new ReadOnlyDictionary<string, string>(dict);
        cache.SetResources(normalized, readonlyDict);
        return readonlyDict;
    }

    public async Task SyncModuleResourcesAsync(
        string moduleId,
        IReadOnlyList<LocaleResourceEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(moduleId) || entries.Count == 0)
            return;

        var normalizedModule = moduleId.Trim();
        var existing = await db.Resources
            .Where(r => r.ModuleId == normalizedModule)
            .ToListAsync(cancellationToken);

        var existingMap = existing.ToDictionary(
            r => (r.Culture.ToLowerInvariant(), r.Key.ToLowerInvariant()),
            r => r);

        var changed = false;
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Culture) || string.IsNullOrWhiteSpace(entry.Key))
                continue;

            var culture = entry.Culture.Trim();
            var key = entry.Key.Trim();
            var lookup = (culture.ToLowerInvariant(), key.ToLowerInvariant());

            if (existingMap.TryGetValue(lookup, out var row))
            {
                if (!row.IsOverridden && !string.Equals(row.Value, entry.Value, StringComparison.Ordinal))
                {
                    row.Value = entry.Value;
                    row.UpdatedAt = now;
                    changed = true;
                }
            }
            else
            {
                var added = new LocaleResource
                {
                    ModuleId = normalizedModule,
                    Culture = culture,
                    Key = key,
                    Value = entry.Value,
                    IsOverridden = false,
                    UpdatedAt = now
                };
                db.Resources.Add(added);
                existingMap[lookup] = added;
                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
            cache.InvalidateAll();
        }
    }
}

