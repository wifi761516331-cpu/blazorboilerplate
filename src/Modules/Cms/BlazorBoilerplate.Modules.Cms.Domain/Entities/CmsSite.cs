using BlazorBoilerplate.Domain;
using BlazorBoilerplate.Modules.Cms.Domain.Events;

namespace BlazorBoilerplate.Modules.Cms.Domain.Entities;

/// <summary>CMS 官网站点聚合根。</summary>
public sealed class CmsSite : AuditableEntity<Guid>, IAggregateRoot
{
    public CmsSite()
    {
        Id = Guid.CreateVersion7();
    }

    public string Name { get; set; } = string.Empty;
    public string SiteKey { get; set; } = "default";
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string DefaultCulture { get; set; } = "zh-CN";
    public string SupportedCulturesJson { get; set; } = "[\"zh-CN\",\"en-US\"]";
    public string ThemeJson { get; set; } = "{}";
    public string? PublishedSnapshotJson { get; set; }
    public bool IsPublished { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public Guid? TenantId { get; set; }

    public ICollection<CmsSection> Sections { get; set; } = [];
    public ICollection<CmsWidgetInstance> Widgets { get; set; } = [];
    public ICollection<CmsLeadSubmission> Leads { get; set; } = [];

    public void UpdateInfo(string name, string siteKey, string title, string? description, string? logoUrl, string? faviconUrl)
    {
        Name = name.Trim();
        SiteKey = siteKey.Trim().ToLowerInvariant();
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
        FaviconUrl = string.IsNullOrWhiteSpace(faviconUrl) ? null : faviconUrl.Trim();
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }

    public void UpdateTheme(string themeJson)
    {
        ThemeJson = string.IsNullOrWhiteSpace(themeJson) ? "{}" : themeJson;
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }

    public void UpdateLanguages(string defaultCulture, string supportedCulturesJson)
    {
        DefaultCulture = string.IsNullOrWhiteSpace(defaultCulture) ? "zh-CN" : defaultCulture.Trim();
        SupportedCulturesJson = string.IsNullOrWhiteSpace(supportedCulturesJson) ? $"[\"{DefaultCulture}\"]" : supportedCulturesJson;
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Publish(string snapshotJson)
    {
        var now = DateTimeOffset.UtcNow;
        PublishedSnapshotJson = snapshotJson;
        IsPublished = true;
        PublishedAtUtc = now;
        ModifiedAtUtc = now;
        Raise(new CmsSitePublishedEvent(Id, SiteKey, now));
    }

    public void Unpublish()
    {
        IsPublished = false;
        ModifiedAtUtc = DateTimeOffset.UtcNow;
        Raise(new CmsSiteUnpublishedEvent(Id, SiteKey));
    }

    public void MarkDeletedEvent() => Raise(new CmsSiteDeletedEvent(Id, SiteKey));
}
