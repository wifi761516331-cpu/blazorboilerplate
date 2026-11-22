using BlazorBoilerplate.Modules.Cms.Domain.Entities;
using BlazorBoilerplate.Modules.Cms.Domain.Repositories;
using BlazorBoilerplate.Modules.Cms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Modules.Cms.Infrastructure.Repositories;

public sealed class CmsSiteRepository(CmsDbContext db) : ICmsSiteRepository
{
    public Task<List<CmsSite>> ListAsync(CancellationToken ct = default) =>
        db.Sites.OrderBy(s => s.CreatedAtUtc).ToListAsync(ct);

    public Task<CmsSite?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Sites.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<CmsSite?> GetByKeyAsync(string siteKey, CancellationToken ct = default) =>
        db.Sites.FirstOrDefaultAsync(s => s.SiteKey == siteKey, ct);

    public Task<bool> ExistsKeyAsync(string siteKey, Guid? excludeId = null, CancellationToken ct = default) =>
        db.Sites.AnyAsync(s => s.SiteKey == siteKey && (!excludeId.HasValue || s.Id != excludeId.Value), ct);

    public Task AddAsync(CmsSite site, CancellationToken ct = default) =>
        db.Sites.AddAsync(site, ct).AsTask();

    public void Remove(CmsSite site) => db.Sites.Remove(site);
}

public sealed class CmsLayoutRepository(CmsDbContext db) : ICmsLayoutRepository
{
    public Task<List<CmsLayout>> ListAsync(CancellationToken ct = default) =>
        db.Layouts.OrderBy(l => l.SortOrder).ToListAsync(ct);

    public Task<CmsLayout?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Layouts.FirstOrDefaultAsync(l => l.Id == id, ct);

    public Task<bool> ExistsNameAsync(string name, Guid? excludeId = null, CancellationToken ct = default) =>
        db.Layouts.AnyAsync(l => l.Name == name && (!excludeId.HasValue || l.Id != excludeId.Value), ct);

    public Task AddAsync(CmsLayout layout, CancellationToken ct = default) =>
        db.Layouts.AddAsync(layout, ct).AsTask();

    public void Remove(CmsLayout layout) => db.Layouts.Remove(layout);
}

public sealed class CmsSectionRepository(CmsDbContext db) : ICmsSectionRepository
{
    public Task<List<CmsSection>> ListBySiteAsync(Guid siteId, string? culture = null, CancellationToken ct = default) =>
        db.Sections
            .Where(s => s.SiteId == siteId && (culture == null || s.Culture == culture))
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);

    public Task<CmsSection?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Sections.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<bool> ExistsSlugAsync(Guid siteId, string culture, string anchorSlug, Guid? excludeId = null, CancellationToken ct = default) =>
        db.Sections.AnyAsync(
            s => s.SiteId == siteId && s.Culture == culture && s.AnchorSlug == anchorSlug && (!excludeId.HasValue || s.Id != excludeId.Value),
            ct);

    public Task AddAsync(CmsSection section, CancellationToken ct = default) =>
        db.Sections.AddAsync(section, ct).AsTask();

    public void Remove(CmsSection section) => db.Sections.Remove(section);

    public async Task RemoveBySiteAsync(Guid siteId, string? culture = null, CancellationToken ct = default)
    {
        var items = await db.Sections.Where(s => s.SiteId == siteId && (culture == null || s.Culture == culture)).ToListAsync(ct);
        if (items.Count > 0) db.Sections.RemoveRange(items);
    }
}

public sealed class CmsWidgetRepository(CmsDbContext db) : ICmsWidgetRepository
{
    public Task<List<CmsWidgetInstance>> ListBySiteAsync(Guid siteId, string? culture = null, CancellationToken ct = default) =>
        db.Widgets
            .Where(w => w.SiteId == siteId && (culture == null || w.Culture == culture))
            .OrderBy(w => w.SortOrder)
            .ToListAsync(ct);

    public Task<List<CmsWidgetInstance>> ListTemplatesAsync(CancellationToken ct = default) =>
        db.Widgets.Where(w => w.IsTemplate).OrderBy(w => w.TemplateName).ToListAsync(ct);

    public Task<CmsWidgetInstance?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Widgets.FirstOrDefaultAsync(w => w.Id == id, ct);

    public Task AddAsync(CmsWidgetInstance widget, CancellationToken ct = default) =>
        db.Widgets.AddAsync(widget, ct).AsTask();

    public void Remove(CmsWidgetInstance widget) => db.Widgets.Remove(widget);

    public async Task RemoveBySectionAsync(Guid sectionId, CancellationToken ct = default)
    {
        var items = await db.Widgets.Where(w => w.SectionId == sectionId).ToListAsync(ct);
        if (items.Count > 0) db.Widgets.RemoveRange(items);
    }

    public async Task RemoveBySiteAsync(Guid siteId, string? culture = null, CancellationToken ct = default)
    {
        var items = await db.Widgets.Where(w => w.SiteId == siteId && (culture == null || w.Culture == culture)).ToListAsync(ct);
        if (items.Count > 0) db.Widgets.RemoveRange(items);
    }
}

public sealed class CmsLeadRepository(CmsDbContext db) : ICmsLeadRepository
{
    public Task<List<CmsLeadSubmission>> ListBySiteAsync(Guid siteId, CancellationToken ct = default) =>
        db.Leads.Where(l => l.SiteId == siteId).OrderByDescending(l => l.CreatedAtUtc).ToListAsync(ct);

    public Task<CmsLeadSubmission?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);

    public Task AddAsync(CmsLeadSubmission lead, CancellationToken ct = default) =>
        db.Leads.AddAsync(lead, ct).AsTask();

    public void Remove(CmsLeadSubmission lead) => db.Leads.Remove(lead);
}

public sealed class CmsUnitOfWork(CmsDbContext db) : ICmsUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
