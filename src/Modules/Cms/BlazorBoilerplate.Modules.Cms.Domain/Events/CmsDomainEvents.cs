using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Modules.Cms.Domain.Events;

public sealed class CmsSitePublishedEvent(Guid siteId, string siteKey, DateTimeOffset publishedAtUtc) : DomainEventBase
{
    public Guid SiteId { get; } = siteId;
    public string SiteKey { get; } = siteKey;
    public DateTimeOffset PublishedAtUtc { get; } = publishedAtUtc;
}

public sealed class CmsSiteUnpublishedEvent(Guid siteId, string siteKey) : DomainEventBase
{
    public Guid SiteId { get; } = siteId;
    public string SiteKey { get; } = siteKey;
}

public sealed class CmsSiteDeletedEvent(Guid siteId, string siteKey) : DomainEventBase
{
    public Guid SiteId { get; } = siteId;
    public string SiteKey { get; } = siteKey;
}

public sealed class CmsLayoutChangedEvent(Guid layoutId) : DomainEventBase
{
    public Guid LayoutId { get; } = layoutId;
}

public sealed class CmsLeadSubmittedEvent(Guid leadId, Guid siteId, string name, string? email, string? phone) : DomainEventBase
{
    public Guid LeadId { get; } = leadId;
    public Guid SiteId { get; } = siteId;
    public string Name { get; } = name;
    public string? Email { get; } = email;
    public string? Phone { get; } = phone;
}
