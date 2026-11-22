using BlazorBoilerplate.Modules.Cms.Domain.Entities;

namespace BlazorBoilerplate.Modules.Cms.Domain.Repositories;

public interface ICmsSiteRepository
{
    Task<List<CmsSite>> ListAsync(CancellationToken ct = default);
    Task<CmsSite?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CmsSite?> GetByKeyAsync(string siteKey, CancellationToken ct = default);
    Task<bool> ExistsKeyAsync(string siteKey, Guid? excludeId = null, CancellationToken ct = default);
    Task AddAsync(CmsSite site, CancellationToken ct = default);
    void Remove(CmsSite site);
}

public interface ICmsLayoutRepository
{
    Task<List<CmsLayout>> ListAsync(CancellationToken ct = default);
    Task<CmsLayout?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsNameAsync(string name, Guid? excludeId = null, CancellationToken ct = default);
    Task AddAsync(CmsLayout layout, CancellationToken ct = default);
    void Remove(CmsLayout layout);
}

public interface ICmsSectionRepository
{
    Task<List<CmsSection>> ListBySiteAsync(Guid siteId, string? culture = null, CancellationToken ct = default);
    Task<CmsSection?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsSlugAsync(Guid siteId, string culture, string anchorSlug, Guid? excludeId = null, CancellationToken ct = default);
    Task AddAsync(CmsSection section, CancellationToken ct = default);
    void Remove(CmsSection section);
    Task RemoveBySiteAsync(Guid siteId, string? culture = null, CancellationToken ct = default);
}

public interface ICmsWidgetRepository
{
    Task<List<CmsWidgetInstance>> ListBySiteAsync(Guid siteId, string? culture = null, CancellationToken ct = default);
    Task<List<CmsWidgetInstance>> ListTemplatesAsync(CancellationToken ct = default);
    Task<CmsWidgetInstance?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(CmsWidgetInstance widget, CancellationToken ct = default);
    void Remove(CmsWidgetInstance widget);
    Task RemoveBySectionAsync(Guid sectionId, CancellationToken ct = default);
    Task RemoveBySiteAsync(Guid siteId, string? culture = null, CancellationToken ct = default);
}

public interface ICmsLeadRepository
{
    Task<List<CmsLeadSubmission>> ListBySiteAsync(Guid siteId, CancellationToken ct = default);
    Task<CmsLeadSubmission?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(CmsLeadSubmission lead, CancellationToken ct = default);
    void Remove(CmsLeadSubmission lead);
}

public interface ICmsUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
