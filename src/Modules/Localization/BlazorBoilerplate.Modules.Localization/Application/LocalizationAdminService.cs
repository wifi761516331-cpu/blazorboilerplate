using System.Globalization;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Modules.Localization.Domain;

namespace BlazorBoilerplate.Modules.Localization.Application;

public sealed class LocalizationAdminService(
    ILocalizationRepository repo,
    ILocalizationCacheInvalidator cache,
    ILocalizationStore store,
    ILocalizationRegistry? registry = null)
{
    public async Task<IReadOnlyList<LanguageDto>> GetAllLanguagesAsync(CancellationToken ct = default)
    {
        var list = await repo.GetAllLanguagesAsync(ct);
        return list.Select(ToDto).ToList();
    }

    public async Task<LanguageDto> CreateLanguageAsync(CreateLanguageRequest req, CancellationToken ct = default)
    {
        var culture = ValidateCulture(req.Culture);
        if (await repo.CultureExistsAsync(culture, ct))
            throw new InvalidOperationException($"语言 '{culture}' 已存在。");

        var now = DateTimeOffset.UtcNow;
        var isDefault = req.IsDefault;
        var isEnabled = isDefault || req.IsEnabled;

        if (isDefault)
            await repo.ClearDefaultLanguageAsync(excludeId: null, ct);

        var entity = new Language
        {
            Culture = culture,
            Name = string.IsNullOrWhiteSpace(req.Name) ? culture : req.Name.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? culture : req.DisplayName.Trim(),
            FlagCode = NormalizeFlagCode(req.FlagCode),
            IsRtl = req.IsRtl,
            IsDefault = isDefault,
            IsEnabled = isEnabled,
            DisplayOrder = req.DisplayOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        repo.AddLanguage(entity);
        await repo.SaveChangesAsync(ct);
        cache.InvalidateAll();
        return ToDto(entity);
    }

    public async Task<LanguageDto> UpdateLanguageAsync(Guid id, UpdateLanguageRequest req, CancellationToken ct = default)
    {
        var entity = await repo.GetLanguageByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("未找到指定的语言配置。");

        var isFallback = string.Equals(entity.Culture, LocalizationDefaults.DefaultFallbackCulture, StringComparison.OrdinalIgnoreCase);
        if (isFallback && !req.IsEnabled)
            throw new InvalidOperationException($"中间回退语言 {LocalizationDefaults.DefaultFallbackCulture} 不可禁用。");

        if (req.IsDefault)
            await repo.ClearDefaultLanguageAsync(excludeId: id, ct);

        entity.Name = string.IsNullOrWhiteSpace(req.Name) ? entity.Culture : req.Name.Trim();
        entity.DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? entity.Name : req.DisplayName.Trim();
        entity.FlagCode = NormalizeFlagCode(req.FlagCode);
        entity.IsRtl = req.IsRtl;
        entity.IsDefault = req.IsDefault;
        entity.IsEnabled = isFallback || req.IsDefault || req.IsEnabled;
        entity.DisplayOrder = req.DisplayOrder;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await repo.SaveChangesAsync(ct);
        cache.InvalidateAll();
        return ToDto(entity);
    }

    public async Task DeleteLanguageAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await repo.GetLanguageByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("未找到指定的语言配置。");

        if (string.Equals(entity.Culture, LocalizationDefaults.DefaultFallbackCulture, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"中间回退语言 {LocalizationDefaults.DefaultFallbackCulture} 不可删除。");

        if (entity.IsDefault)
            throw new InvalidOperationException("当前默认语言不可删除，请先将其他语言设为默认。");

        await repo.DeleteLanguageAndResourcesAsync(entity, ct);
        await repo.SaveChangesAsync(ct);
        cache.InvalidateAll();
    }

    public async Task<IReadOnlyList<string>> GetModulesAsync(CancellationToken ct = default)
    {
        var dbModules = await repo.GetDistinctModulesAsync(ct);
        var set = new HashSet<string>(dbModules, StringComparer.OrdinalIgnoreCase);
        if (registry is not null)
        {
            foreach (var entry in registry.GetAllEntries())
                set.Add(entry.ModuleId);
        }

        return set
            .OrderBy(m => string.Equals(m, LocalizationDefaults.PlatformModuleId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(m => m, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<PagedResourcesResult> QueryResourcesAsync(
        string culture,
        string? moduleId,
        string? keyword,
        bool onlyMissing,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var targetCulture = string.IsNullOrWhiteSpace(culture) ? LocalizationDefaults.DefaultChineseCulture : culture.Trim();
        var fallbackCulture = LocalizationDefaults.DefaultFallbackCulture;

        var rows = await repo.GetResourcesForComparisonAsync(fallbackCulture, targetCulture, moduleId, ct);

        var fallbackMap = new Dictionary<(string ModuleId, string Key), LocaleResource>();
        var targetMap = new Dictionary<(string ModuleId, string Key), LocaleResource>();
        var allKeys = new HashSet<(string ModuleId, string Key)>();

        foreach (var row in rows)
        {
            var composite = (row.ModuleId, row.Key);
            allKeys.Add(composite);
            if (string.Equals(row.Culture, fallbackCulture, StringComparison.OrdinalIgnoreCase))
                fallbackMap[composite] = row;
            if (string.Equals(row.Culture, targetCulture, StringComparison.OrdinalIgnoreCase))
                targetMap[composite] = row;
        }

        var kw = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
        var items = new List<ResourceItemDto>(allKeys.Count);

        foreach (var composite in allKeys)
        {
            fallbackMap.TryGetValue(composite, out var fbRow);
            targetMap.TryGetValue(composite, out var tgRow);

            var isMissing = tgRow is null || string.IsNullOrWhiteSpace(tgRow.Value);
            if (onlyMissing && !isMissing)
                continue;

            if (kw is not null)
            {
                var matched =
                    composite.Key.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                    (fbRow?.Value.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (tgRow?.Value.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false);
                if (!matched)
                    continue;
            }

            items.Add(new ResourceItemDto(
                composite.ModuleId,
                composite.Key,
                fbRow?.Value,
                tgRow?.Value,
                tgRow?.IsOverridden ?? false,
                isMissing,
                tgRow?.UpdatedAt));
        }

        var ordered = items
            .OrderBy(i => string.Equals(i.ModuleId, LocalizationDefaults.PlatformModuleId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(i => i.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var safePage = Math.Max(1, page);
        var safeSize = Math.Clamp(pageSize, 10, 200);
        var paged = ordered.Skip((safePage - 1) * safeSize).Take(safeSize).ToList();

        return new PagedResourcesResult(ordered.Count, paged);
    }

    public async Task UpsertResourcesAsync(UpsertResourcesRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Culture))
            throw new InvalidOperationException("目标文化不能为空。");

        var culture = req.Culture.Trim();
        var now = DateTimeOffset.UtcNow;

        var existing = await repo.GetResourcesByCultureAsync(culture, moduleId: null, ct);
        var map = existing.ToDictionary(
            r => (r.ModuleId.ToLowerInvariant(), r.Key.ToLowerInvariant()),
            r => r);

        foreach (var item in req.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Key))
                continue;

            var mod = string.IsNullOrWhiteSpace(item.ModuleId) ? LocalizationDefaults.PlatformModuleId : item.ModuleId.Trim();
            var key = item.Key.Trim();
            var lookup = (mod.ToLowerInvariant(), key.ToLowerInvariant());

            if (map.TryGetValue(lookup, out var row))
            {
                row.Value = item.Value;
                row.IsOverridden = true;
                row.UpdatedAt = now;
            }
            else
            {
                var added = new LocaleResource
                {
                    ModuleId = mod,
                    Culture = culture,
                    Key = key,
                    Value = item.Value,
                    IsOverridden = true,
                    UpdatedAt = now
                };
                repo.AddResource(added);
                map[lookup] = added;
            }
        }

        await repo.SaveChangesAsync(ct);
        cache.InvalidateCulture(culture);
    }

    public async Task ResetResourceAsync(ResetResourceRequest req, CancellationToken ct = default)
    {
        var mod = string.IsNullOrWhiteSpace(req.ModuleId) ? LocalizationDefaults.PlatformModuleId : req.ModuleId.Trim();
        var culture = req.Culture.Trim();
        var key = req.Key.Trim();

        var row = await repo.GetResourceAsync(mod, culture, key, ct);
        if (row is null)
            return;

        var builtIn = registry?.GetAllEntries().FirstOrDefault(e =>
            string.Equals(e.ModuleId, mod, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Culture, culture, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

        if (builtIn is not null)
        {
            row.Value = builtIn.Value;
            row.IsOverridden = false;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            repo.RemoveResource(row);
        }

        await repo.SaveChangesAsync(ct);
        cache.InvalidateCulture(culture);
    }

    public async Task<int> SyncFromRegistryAsync(CancellationToken ct = default)
    {
        if (registry is null)
            return 0;

        var entries = registry.GetAllEntries();
        foreach (var group in entries.GroupBy(e => e.ModuleId, StringComparer.OrdinalIgnoreCase))
            await store.SyncModuleResourcesAsync(group.Key, [.. group], ct);

        cache.InvalidateAll();
        return entries.Count;
    }

    public async Task<Dictionary<string, string>> ExportAsync(string culture, string? moduleId, CancellationToken ct = default)
    {
        var rows = await repo.GetResourcesByCultureAsync(culture.Trim(), moduleId, ct);
        var result = new Dictionary<string, string>(rows.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.OrderBy(r => r.Key))
            result[row.Key] = row.Value;
        return result;
    }

    public async Task<int> ImportAsync(ImportResourcesRequest req, CancellationToken ct = default)
    {
        if (req.Resources.Count == 0)
            return 0;

        var mod = string.IsNullOrWhiteSpace(req.ModuleId) ? LocalizationDefaults.PlatformModuleId : req.ModuleId.Trim();
        var items = req.Resources
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .Select(kv => new UpsertResourceItem(mod, kv.Key.Trim(), kv.Value))
            .ToList();

        await UpsertResourcesAsync(new UpsertResourcesRequest(req.Culture, items), ct);
        return items.Count;
    }

    private static string ValidateCulture(string culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
            throw new InvalidOperationException("区域文化代码不能为空。");

        try
        {
            var info = CultureInfo.GetCultureInfo(culture.Trim());
            if (string.IsNullOrWhiteSpace(info.Name))
                throw new InvalidOperationException("无效的区域文化代码。");
            return info.Name;
        }
        catch (CultureNotFoundException)
        {
            throw new InvalidOperationException($"无效的区域文化代码：'{culture}'。");
        }
    }

    private static string? NormalizeFlagCode(string? flagCode)
    {
        if (string.IsNullOrWhiteSpace(flagCode))
            return null;

        var trimmed = flagCode.Trim().ToUpperInvariant();
        return trimmed.Length > 8 ? trimmed[..8] : trimmed;
    }

    private static LanguageDto ToDto(Language e) =>
        new(
            e.Id,
            e.Culture,
            e.Name,
            e.DisplayName,
            e.FlagCode,
            e.IsRtl,
            e.IsDefault,
            e.IsEnabled,
            e.DisplayOrder,
            string.Equals(e.Culture, LocalizationDefaults.DefaultFallbackCulture, StringComparison.OrdinalIgnoreCase));
}

