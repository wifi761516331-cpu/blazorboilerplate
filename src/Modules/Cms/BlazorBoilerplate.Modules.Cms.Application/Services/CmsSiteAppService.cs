using System.Text.Json;
using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Modules.Cms.Application.AI;
using BlazorBoilerplate.Modules.Cms.Application.Caching;
using BlazorBoilerplate.Modules.Cms.Application.DTOs;
using BlazorBoilerplate.Modules.Cms.Domain.Entities;
using BlazorBoilerplate.Modules.Cms.Domain.Repositories;
using BlazorBoilerplate.Modules.Cms.Domain.ValueObjects;

namespace BlazorBoilerplate.Modules.Cms.Application.Services;

public sealed class CmsSiteAppService(
    ICmsSiteRepository sites,
    ICmsLayoutRepository layouts,
    ICmsSectionRepository sections,
    ICmsWidgetRepository widgets,
    ICmsUnitOfWork uow,
    ICacheService cache)
{
    public async Task<List<CmsSiteDto>> ListAsync(CancellationToken ct = default) =>
        (await sites.ListAsync(ct)).Select(s => s.ToDto()).ToList();

    public async Task<CmsSiteDto> CreateAsync(UpsertCmsSiteRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new InvalidOperationException("站点名称不能为空。");
        if (string.IsNullOrWhiteSpace(req.Title))
            throw new InvalidOperationException("官网标题不能为空。");
        var key = req.SiteKey?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("站点标识 SiteKey 不能为空。");
        if (await sites.ExistsKeyAsync(key, null, ct))
            throw new InvalidOperationException($"站点标识 '{key}' 已存在。");

        var defaultCulture = string.IsNullOrWhiteSpace(req.DefaultCulture) ? "zh-CN" : req.DefaultCulture.Trim();
        var supported = req.SupportedCultures is { Count: > 0 }
            ? req.SupportedCultures.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [defaultCulture, "en-US"];
        if (!supported.Contains(defaultCulture, StringComparer.OrdinalIgnoreCase))
            supported.Insert(0, defaultCulture);

        var site = new CmsSite
        {
            Name = req.Name.Trim(),
            SiteKey = key,
            Title = req.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            LogoUrl = req.LogoUrl,
            FaviconUrl = req.FaviconUrl,
            DefaultCulture = defaultCulture,
            SupportedCulturesJson = JsonSerializer.Serialize(supported, CmsDtoMapper.JsonOptions),
            ThemeJson = JsonSerializer.Serialize(new CmsThemeConfig(), CmsDtoMapper.JsonOptions)
        };
        await sites.AddAsync(site, ct);
        await uow.SaveChangesAsync(ct);
        return site.ToDto();
    }

    private static void EnsureEditableSite(CmsSite site)
    {
        if (CmsSiteTemplateLibrary.IsBuiltInTemplateKey(site.SiteKey))
            throw new InvalidOperationException("内置官网模板为只读保护模板，不可直接编辑或删除，请先复制为新站点。");
    }

    public async Task<CmsSiteDto> UpdateAsync(Guid id, UpsertCmsSiteRequest req, CancellationToken ct = default)
    {
        var site = await sites.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("站点不存在。");
        EnsureEditableSite(site);
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new InvalidOperationException("站点名称不能为空。");
        if (string.IsNullOrWhiteSpace(req.Title))
            throw new InvalidOperationException("官网标题不能为空。");
        var key = req.SiteKey?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("站点标识 SiteKey 不能为空。");
        if (await sites.ExistsKeyAsync(key, id, ct))
            throw new InvalidOperationException($"站点标识 '{key}' 已被其他站点使用。");

        site.UpdateInfo(req.Name, key, req.Title, req.Description, req.LogoUrl, req.FaviconUrl);
        if (!string.IsNullOrWhiteSpace(req.DefaultCulture) || req.SupportedCultures is { Count: > 0 })
        {
            var def = string.IsNullOrWhiteSpace(req.DefaultCulture) ? site.DefaultCulture : req.DefaultCulture.Trim();
            var list = req.SupportedCultures is { Count: > 0 }
                ? req.SupportedCultures.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : CmsDtoMapper.DeserializeCultures(site.SupportedCulturesJson, def);
            if (!list.Contains(def, StringComparer.OrdinalIgnoreCase))
                list.Insert(0, def);
            site.UpdateLanguages(def, JsonSerializer.Serialize(list, CmsDtoMapper.JsonOptions));
        }

        await uow.SaveChangesAsync(ct);
        return site.ToDto();
    }

    public async Task<CmsSiteDto> UpdateLanguagesAsync(Guid id, UpdateSiteLanguagesRequest req, CancellationToken ct = default)
    {
        var site = await sites.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("站点不存在。");
        EnsureEditableSite(site);
        var def = string.IsNullOrWhiteSpace(req.DefaultCulture) ? "zh-CN" : req.DefaultCulture.Trim();
        var list = req.SupportedCultures.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!list.Contains(def, StringComparer.OrdinalIgnoreCase))
            list.Insert(0, def);
        site.UpdateLanguages(def, JsonSerializer.Serialize(list, CmsDtoMapper.JsonOptions));
        await uow.SaveChangesAsync(ct);
        return site.ToDto();
    }

    public async Task<CmsSiteDto> UpdateThemeAsync(Guid id, CmsThemeConfig theme, CancellationToken ct = default)
    {
        var site = await sites.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("站点不存在。");
        EnsureEditableSite(site);
        site.UpdateTheme(JsonSerializer.Serialize(theme, CmsDtoMapper.JsonOptions));
        await uow.SaveChangesAsync(ct);
        return site.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var site = await sites.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("站点不存在。");
        EnsureEditableSite(site);
        await widgets.RemoveBySiteAsync(id, null, ct);
        await sections.RemoveBySiteAsync(id, null, ct);
        site.MarkDeletedEvent();
        sites.Remove(site);
        await uow.SaveChangesAsync(ct);
        await InvalidatePublicSiteCacheAsync(site.SiteKey, ct);
    }

    public async Task<CmsSiteDesignDto> GetDesignAsync(Guid siteId, string? culture = null, CancellationToken ct = default)
    {
        var site = await sites.GetByIdAsync(siteId, ct) ?? throw new KeyNotFoundException("站点不存在。");
        var activeCulture = string.IsNullOrWhiteSpace(culture) ? site.DefaultCulture : culture.Trim();
        var allLayouts = await layouts.ListAsync(ct);
        var siteSections = await sections.ListBySiteAsync(siteId, activeCulture, ct);
        var siteWidgets = await widgets.ListBySiteAsync(siteId, activeCulture, ct);

        return new CmsSiteDesignDto(
            site.ToDto(),
            activeCulture,
            allLayouts.Select(l => l.ToDto()).ToList(),
            siteSections.OrderBy(s => s.SortOrder).Select(s => s.ToDto()).ToList(),
            siteWidgets.OrderBy(w => w.SortOrder).Select(w => w.ToDto()).ToList());
    }

    /// <summary>将源语言（如 zh-CN）的区块与微件结构复制到目标语言（如 en-US），方便翻译。</summary>
    public async Task<CmsSiteDesignDto> CloneCultureAsync(Guid siteId, string sourceCulture, string targetCulture, CancellationToken ct = default)
    {
        var site = await sites.GetByIdAsync(siteId, ct) ?? throw new KeyNotFoundException("站点不存在。");
        EnsureEditableSite(site);
        var src = string.IsNullOrWhiteSpace(sourceCulture) ? site.DefaultCulture : sourceCulture.Trim();
        var dst = targetCulture.Trim();
        if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
            return await GetDesignAsync(siteId, dst, ct);

        await widgets.RemoveBySiteAsync(siteId, dst, ct);
        await sections.RemoveBySiteAsync(siteId, dst, ct);

        var srcSections = await sections.ListBySiteAsync(siteId, src, ct);
        var srcWidgets = await widgets.ListBySiteAsync(siteId, src, ct);
        var sectionIdMap = new Dictionary<Guid, Guid>();

        foreach (var sec in srcSections)
        {
            var copySec = new CmsSection
            {
                SiteId = siteId,
                LayoutId = sec.LayoutId,
                Culture = dst,
                Title = sec.Title,
                AnchorSlug = sec.AnchorSlug,
                NavVisible = sec.NavVisible,
                SortOrder = sec.SortOrder,
                BackgroundStyle = sec.BackgroundStyle,
                IsEnabled = sec.IsEnabled
            };
            sectionIdMap[sec.Id] = copySec.Id;
            await sections.AddAsync(copySec, ct);
        }

        foreach (var w in srcWidgets.Where(x => !x.IsTemplate))
        {
            if (!sectionIdMap.TryGetValue(w.SectionId, out var newSectionId))
                continue;

            await widgets.AddAsync(new CmsWidgetInstance
            {
                SiteId = siteId,
                SectionId = newSectionId,
                Culture = dst,
                ZoneId = w.ZoneId,
                WidgetType = w.WidgetType,
                Title = w.Title,
                PropsJson = w.PropsJson,
                StyleJson = w.StyleJson,
                SortOrder = w.SortOrder
            }, ct);
        }

        await uow.SaveChangesAsync(ct);
        return await GetDesignAsync(siteId, dst, ct);
    }

    public async Task<CmsSiteDto> PublishAsync(Guid siteId, CancellationToken ct = default)
    {
        var site = await sites.GetByIdAsync(siteId, ct) ?? throw new KeyNotFoundException("站点不存在。");
        var allLayouts = (await layouts.ListAsync(ct)).ToDictionary(l => l.Id);
        var allSections = await sections.ListBySiteAsync(siteId, null, ct);
        var allWidgets = await widgets.ListBySiteAsync(siteId, null, ct);
        var supportedCultures = CmsDtoMapper.DeserializeCultures(site.SupportedCulturesJson, site.DefaultCulture);
        var theme = CmsDtoMapper.DeserializeTheme(site.ThemeJson);
        var now = DateTimeOffset.UtcNow;

        var snapshots = new Dictionary<string, CmsPublishedSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in supportedCultures)
        {
            var cultureSections = allSections
                .Where(s => string.Equals(s.Culture, culture, StringComparison.OrdinalIgnoreCase) && s.IsEnabled)
                .OrderBy(s => s.SortOrder)
                .ToList();

            var sourceCulture = cultureSections.Count > 0 ? culture : site.DefaultCulture;
            if (cultureSections.Count == 0 && !string.Equals(culture, site.DefaultCulture, StringComparison.OrdinalIgnoreCase))
            {
                cultureSections = allSections
                    .Where(s => string.Equals(s.Culture, site.DefaultCulture, StringComparison.OrdinalIgnoreCase) && s.IsEnabled)
                    .OrderBy(s => s.SortOrder)
                    .ToList();
            }

            var publishedSections = new List<CmsPublishedSection>();
            foreach (var sec in cultureSections)
            {
                var layout = allLayouts.GetValueOrDefault(sec.LayoutId);
                var rows = layout is not null
                    ? CmsDtoMapper.DeserializeRows(layout.RowsJson)
                    : [new CmsLayoutRow("row-1", null, [new CmsLayoutColumn("main", "主内容区", 12)])];

                var secWidgets = allWidgets
                    .Where(w => w.SectionId == sec.Id && string.Equals(w.Culture, sourceCulture, StringComparison.OrdinalIgnoreCase) && !w.IsTemplate)
                    .OrderBy(w => w.SortOrder)
                    .Select(w => new CmsPublishedWidget(w.Id, w.ZoneId, w.WidgetType, w.Title, w.PropsJson, w.StyleJson, w.SortOrder))
                    .ToList();

                publishedSections.Add(new CmsPublishedSection(
                    sec.Id, sec.Title, sec.AnchorSlug, sec.NavVisible, sec.SortOrder,
                    sec.BackgroundStyle, rows, layout?.CustomCss, secWidgets));
            }

            snapshots[culture] = new CmsPublishedSnapshot(
                site.Id, site.SiteKey, culture, site.DefaultCulture, supportedCultures,
                site.Title, site.LogoUrl, site.FaviconUrl, theme, now, publishedSections);
        }

        site.Publish(JsonSerializer.Serialize(snapshots, CmsDtoMapper.JsonOptions));
        await uow.SaveChangesAsync(ct);
        await InvalidatePublicSiteCacheAsync(site.SiteKey, ct);
        return site.ToDto();
    }

    public async Task<CmsSiteDto> UnpublishAsync(Guid siteId, CancellationToken ct = default)
    {
        var site = await sites.GetByIdAsync(siteId, ct) ?? throw new KeyNotFoundException("站点不存在。");
        site.Unpublish();
        await uow.SaveChangesAsync(ct);
        await InvalidatePublicSiteCacheAsync(site.SiteKey, ct);
        return site.ToDto();
    }

    private async Task InvalidatePublicSiteCacheAsync(string siteKey, CancellationToken ct)
    {
        await cache.RemoveAsync(CmsCacheDefaults.PublicSiteKey(siteKey), ct);
        await cache.RemoveByTagAsync(CmsCacheDefaults.PublicSiteTag, ct);
    }

    public async Task<CmsPublishedSnapshot?> GetPublicSnapshotAsync(string? siteKey, string? culture = null, CancellationToken ct = default)
    {
        var key = string.IsNullOrWhiteSpace(siteKey) ? "default" : siteKey.Trim().ToLowerInvariant();
        var reqCulture = string.IsNullOrWhiteSpace(culture) ? null : culture.Trim();

        var json = await cache.GetOrCreateAsync<string>(
            CmsCacheDefaults.PublicSiteKey(key, reqCulture),
            async token =>
            {
                var site = await sites.GetByKeyAsync(key, token);
                if (site is null && key != "default")
                    site = await sites.GetByKeyAsync("default", token);
                if (site is null || !site.IsPublished || string.IsNullOrWhiteSpace(site.PublishedSnapshotJson))
                    return string.Empty;

                var map = JsonSerializer.Deserialize<Dictionary<string, CmsPublishedSnapshot>>(site.PublishedSnapshotJson, CmsDtoMapper.JsonOptions);
                if (map is null || map.Count == 0)
                    return string.Empty;

                var targetCulture = reqCulture ?? site.DefaultCulture;
                if (!map.TryGetValue(targetCulture, out var snap) && !map.TryGetValue(site.DefaultCulture, out snap))
                    snap = map.Values.First();

                return JsonSerializer.Serialize(snap, CmsDtoMapper.JsonOptions);
            },
            new CacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30), Tag = CmsCacheDefaults.PublicSiteTag },
            ct);

        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonSerializer.Deserialize<CmsPublishedSnapshot>(json, CmsDtoMapper.JsonOptions);
    }

    /// <summary>复制现有站点（含全部语言下的区块与微件），生成一个可自由编辑的新站点。</summary>
    public async Task<CmsSiteDto> CloneSiteAsync(Guid sourceSiteId, string? newName = null, string? newSiteKey = null, CancellationToken ct = default)
    {
        var source = await sites.GetByIdAsync(sourceSiteId, ct) ?? throw new KeyNotFoundException("源站点不存在。");

        var baseKey = string.IsNullOrWhiteSpace(newSiteKey)
            ? $"{source.SiteKey}-copy"
            : newSiteKey.Trim().ToLowerInvariant();
        var candidateKey = baseKey;
        var suffix = 1;
        while (await sites.ExistsKeyAsync(candidateKey, null, ct))
        {
            candidateKey = $"{baseKey}-{suffix++}";
        }

        var copyName = string.IsNullOrWhiteSpace(newName)
            ? $"{source.Name} (副本)"
            : newName.Trim();

        var cloned = new CmsSite
        {
            Name = copyName,
            SiteKey = candidateKey,
            Title = source.Title,
            Description = source.Description,
            LogoUrl = source.LogoUrl,
            FaviconUrl = source.FaviconUrl,
            DefaultCulture = source.DefaultCulture,
            SupportedCulturesJson = source.SupportedCulturesJson,
            ThemeJson = source.ThemeJson
        };

        await sites.AddAsync(cloned, ct);

        var srcSections = await sections.ListBySiteAsync(sourceSiteId, null, ct);
        var srcWidgets = await widgets.ListBySiteAsync(sourceSiteId, null, ct);
        var sectionIdMap = new Dictionary<Guid, Guid>();

        foreach (var sec in srcSections.OrderBy(s => s.SortOrder))
        {
            var copySec = new CmsSection
            {
                SiteId = cloned.Id,
                LayoutId = sec.LayoutId,
                Culture = sec.Culture,
                Title = sec.Title,
                AnchorSlug = sec.AnchorSlug,
                NavVisible = sec.NavVisible,
                SortOrder = sec.SortOrder,
                BackgroundStyle = sec.BackgroundStyle,
                IsEnabled = sec.IsEnabled
            };
            sectionIdMap[sec.Id] = copySec.Id;
            await sections.AddAsync(copySec, ct);
        }

        foreach (var w in srcWidgets.Where(x => !x.IsTemplate).OrderBy(x => x.SortOrder))
        {
            if (!sectionIdMap.TryGetValue(w.SectionId, out var newSectionId))
                continue;

            await widgets.AddAsync(new CmsWidgetInstance
            {
                SiteId = cloned.Id,
                SectionId = newSectionId,
                Culture = w.Culture,
                ZoneId = w.ZoneId,
                WidgetType = w.WidgetType,
                Title = w.Title,
                PropsJson = w.PropsJson,
                StyleJson = w.StyleJson,
                SortOrder = w.SortOrder
            }, ct);
        }

        await uow.SaveChangesAsync(ct);
        return cloned.ToDto();
    }

    /// <summary>将 AI 生成的草稿应用到指定站点和语言（写入区块与微件，不直接覆盖已发布快照）。</summary>
    public async Task<CmsSiteDesignDto> ApplyAiDraftAsync(Guid siteId, CmsAiSiteDraft draft, string? culture = null, CancellationToken ct = default)
    {
        var site = await sites.GetByIdAsync(siteId, ct) ?? throw new KeyNotFoundException("站点不存在。");
        var targetCulture = string.IsNullOrWhiteSpace(culture) ? site.DefaultCulture : culture.Trim();
        if (CmsSiteTemplateLibrary.IsBuiltInTemplateKey(site.SiteKey)
            && (await sections.ListBySiteAsync(siteId, targetCulture, ct)).Count > 0)
        {
            EnsureEditableSite(site);
        }
        var allLayouts = await layouts.ListAsync(ct);
        var layoutZones = allLayouts.ToDictionary(
            l => l.Id,
            l => CmsDtoMapper.DeserializeRows(l.RowsJson)
                .SelectMany(r => r.Columns)
                .Select(c => c.ZoneId.Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase));

        var singleCol = allLayouts.FirstOrDefault(l => layoutZones[l.Id].SetEquals(["main"]))
            ?? allLayouts.FirstOrDefault(l => l.Name.Contains("单栏") || l.Name.Contains("通栏"))
            ?? allLayouts.FirstOrDefault();
        var twoCol = allLayouts.FirstOrDefault(l => layoutZones[l.Id].Contains("left") && layoutZones[l.Id].Contains("right"))
            ?? allLayouts.FirstOrDefault(l => l.Name.Contains("两栏"))
            ?? singleCol;
        var threeCol = allLayouts.FirstOrDefault(l => layoutZones[l.Id].Contains("col-1") && layoutZones[l.Id].Contains("col-2") && layoutZones[l.Id].Contains("col-3"))
            ?? allLayouts.FirstOrDefault(l => l.Name.Contains("三栏"))
            ?? singleCol;
        var sidebarCol = allLayouts.FirstOrDefault(l => layoutZones[l.Id].Contains("sidebar") && layoutZones[l.Id].Contains("main"))
            ?? allLayouts.FirstOrDefault(l => l.Name.Contains("侧栏"))
            ?? singleCol;
        if (singleCol is null)
            throw new InvalidOperationException("请先至少创建一个布局页模板。");

        await widgets.RemoveBySiteAsync(siteId, targetCulture, ct);
        await sections.RemoveBySiteAsync(siteId, targetCulture, ct);

        site.UpdateTheme(JsonSerializer.Serialize(draft.Theme, CmsDtoMapper.JsonOptions));

        var secOrder = 0;
        foreach (var secDraft in draft.Sections)
        {
            var layout = secDraft.LayoutPreset switch
            {
                "two-column" => twoCol,
                "three-column" => threeCol,
                "sidebar" => sidebarCol,
                _ => singleCol
            };
            var layoutId = (layout ?? singleCol).Id;
            var sec = new CmsSection
            {
                SiteId = siteId,
                LayoutId = layoutId,
                Culture = targetCulture,
                Title = secDraft.Title,
                AnchorSlug = secDraft.AnchorSlug,
                NavVisible = secDraft.NavVisible,
                SortOrder = secOrder++,
                IsEnabled = true
            };
            await sections.AddAsync(sec, ct);

            var wOrder = 0;
            foreach (var wDraft in secDraft.Widgets)
            {
                await widgets.AddAsync(new CmsWidgetInstance
                {
                    SiteId = siteId,
                    SectionId = sec.Id,
                    Culture = targetCulture,
                    ZoneId = wDraft.ZoneId,
                    WidgetType = wDraft.WidgetType,
                    Title = wDraft.Title,
                    PropsJson = wDraft.PropsJson,
                    StyleJson = wDraft.StyleJson,
                    SortOrder = wOrder++
                }, ct);
            }
        }

        await uow.SaveChangesAsync(ct);
        return await GetDesignAsync(siteId, targetCulture, ct);
    }
}
