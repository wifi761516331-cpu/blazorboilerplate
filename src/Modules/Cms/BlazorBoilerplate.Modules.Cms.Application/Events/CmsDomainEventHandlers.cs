using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Application.Platform;
using BlazorBoilerplate.Domain.Events;
using BlazorBoilerplate.Modules.Cms.Application.Caching;
using BlazorBoilerplate.Modules.Cms.Domain.Events;

namespace BlazorBoilerplate.Modules.Cms.Application.Events;

/// <summary>监听站点发布/下线/删除及布局变更事件，自动失效公开快照与布局缓存。</summary>
[Subscribe]
public sealed class CmsCacheInvalidationEventHandler(ICacheService cache) :
    IEventHandler<CmsSitePublishedEvent>,
    IEventHandler<CmsSiteUnpublishedEvent>,
    IEventHandler<CmsSiteDeletedEvent>,
    IEventHandler<CmsLayoutChangedEvent>
{
    public async Task HandleAsync(CmsSitePublishedEvent @event, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(CmsCacheDefaults.PublicSiteKey(@event.SiteKey), cancellationToken);
        await cache.RemoveByTagAsync(CmsCacheDefaults.PublicSiteTag, cancellationToken);
    }

    public async Task HandleAsync(CmsSiteUnpublishedEvent @event, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(CmsCacheDefaults.PublicSiteKey(@event.SiteKey), cancellationToken);
        await cache.RemoveByTagAsync(CmsCacheDefaults.PublicSiteTag, cancellationToken);
    }

    public async Task HandleAsync(CmsSiteDeletedEvent @event, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(CmsCacheDefaults.PublicSiteKey(@event.SiteKey), cancellationToken);
        await cache.RemoveByTagAsync(CmsCacheDefaults.PublicSiteTag, cancellationToken);
    }

    public Task HandleAsync(CmsLayoutChangedEvent @event, CancellationToken cancellationToken) =>
        cache.RemoveByTagAsync(CmsCacheDefaults.LayoutsTag, cancellationToken);
}

/// <summary>监听官网留资事件，自动写入平台操作审计日志（解耦留资提交与通知/审计）。</summary>
[Subscribe]
public sealed class CmsLeadSubmittedEventHandler(IActivityLogService? audit = null)
    : IEventHandler<CmsLeadSubmittedEvent>
{
    public async Task HandleAsync(CmsLeadSubmittedEvent @event, CancellationToken cancellationToken)
    {
        if (audit is null)
            return;

        await audit.LogAsync(
            "Cms.LeadSubmitted",
            $"官网收到新留资：{@event.Name}（{@event.Email ?? @event.Phone ?? "-"}）",
            entityName: "CmsLeadSubmission",
            entityId: @event.LeadId.ToString(),
            cancellationToken: cancellationToken);
    }
}
