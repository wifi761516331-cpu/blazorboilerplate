using System.Net;
using System.Text.Json;
using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Application.Platform;
using BlazorBoilerplate.Modules.Cms.Application.AI;
using BlazorBoilerplate.Modules.Cms.Application.Caching;
using BlazorBoilerplate.Modules.Cms.Application.DTOs;
using BlazorBoilerplate.Modules.Cms.Domain.Entities;
using BlazorBoilerplate.Modules.Cms.Domain.Repositories;
using BlazorBoilerplate.Modules.Cms.Domain.ValueObjects;

namespace BlazorBoilerplate.Modules.Cms.Application.Services;

public sealed class CmsLayoutAppService(ICmsLayoutRepository layouts, ICmsUnitOfWork uow, ICacheService cache)
{
    public async Task<List<CmsLayoutDto>> ListAsync(CancellationToken ct = default)
    {
        var json = await cache.GetOrCreateAsync<string>(
            CmsCacheDefaults.LayoutsKey,
            async token =>
            {
                var items = (await layouts.ListAsync(token)).OrderBy(l => l.SortOrder).Select(l => l.ToDto()).ToList();
                return JsonSerializer.Serialize(items, CmsDtoMapper.JsonOptions);
            },
            new CacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30), Tag = CmsCacheDefaults.LayoutsTag },
            ct);

        return string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<CmsLayoutDto>>(json, CmsDtoMapper.JsonOptions) ?? [];
    }

    public async Task<CmsLayoutDto> CreateAsync(UpsertCmsLayoutRequest req, CancellationToken ct = default)
    {
        var name = req.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("布局名称不能为空。");
        if (req.Rows is not { Count: > 0 })
            throw new InvalidOperationException("布局至少需要包含一行。");
        if (!CmsLayoutTree.Validate(req.Rows, out var layoutError))
            throw new InvalidOperationException(layoutError);
        if (await layouts.ExistsNameAsync(name, null, ct))
            throw new InvalidOperationException($"布局名称 '{name}' 已存在。");

        var layout = new CmsLayout
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            RowsJson = JsonSerializer.Serialize(req.Rows, CmsDtoMapper.JsonOptions),
            CustomCss = req.CustomCss,
            ThumbnailUrl = req.ThumbnailUrl,
            SortOrder = req.SortOrder,
            IsBuiltIn = false
        };
        layout.NotifyChanged();
        await layouts.AddAsync(layout, ct);
        await uow.SaveChangesAsync(ct);
        return layout.ToDto();
    }

    public async Task<CmsLayoutDto> UpdateAsync(Guid id, UpsertCmsLayoutRequest req, CancellationToken ct = default)
    {
        var layout = await layouts.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("布局不存在。");
        var name = req.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("布局名称不能为空。");
        if (req.Rows is not { Count: > 0 })
            throw new InvalidOperationException("布局至少需要包含一行。");
        if (!CmsLayoutTree.Validate(req.Rows, out var layoutError))
            throw new InvalidOperationException(layoutError);
        if (await layouts.ExistsNameAsync(name, id, ct))
            throw new InvalidOperationException($"布局名称 '{name}' 已被其他布局使用。");

        layout.Update(name, req.Description, JsonSerializer.Serialize(req.Rows, CmsDtoMapper.JsonOptions), req.CustomCss, req.ThumbnailUrl, req.SortOrder);
        await uow.SaveChangesAsync(ct);
        return layout.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var layout = await layouts.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("布局不存在。");
        if (layout.IsBuiltIn)
            throw new InvalidOperationException("内置布局模板不可删除，可复制修改。");
        layout.NotifyChanged();
        layouts.Remove(layout);
        await uow.SaveChangesAsync(ct);
    }
}

public sealed class CmsSectionAppService(
    ICmsSectionRepository sections,
    ICmsWidgetRepository widgets,
    ICmsUnitOfWork uow,
    ICmsSiteRepository? sites = null)
{
    private async Task EnsureSiteEditableAsync(Guid siteId, CancellationToken ct)
    {
        if (sites is null) return;
        var site = await sites.GetByIdAsync(siteId, ct);
        if (site is not null && CmsSiteTemplateLibrary.IsBuiltInTemplateKey(site.SiteKey))
            throw new InvalidOperationException("内置官网模板为只读保护模板，不可直接编辑或删除，请先复制为新站点。");
    }

    public async Task<CmsSectionDto> CreateAsync(Guid siteId, UpsertCmsSectionRequest req, CancellationToken ct = default)
    {
        await EnsureSiteEditableAsync(siteId, ct);
        if (req.LayoutId == Guid.Empty)
            throw new InvalidOperationException("请选择区块布局。");
        if (string.IsNullOrWhiteSpace(req.Title))
            throw new InvalidOperationException("区块名称不能为空。");
        var slug = req.AnchorSlug?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(slug))
            throw new InvalidOperationException("区块锚点标识不能为空。");
        var culture = string.IsNullOrWhiteSpace(req.Culture) ? "zh-CN" : req.Culture.Trim();
        if (await sections.ExistsSlugAsync(siteId, culture, slug, null, ct))
            throw new InvalidOperationException($"锚点标识 '{slug}' 在当前语言下已存在。");

        var existing = await sections.ListBySiteAsync(siteId, culture, ct);
        var nextOrder = req.SortOrder > 0 ? req.SortOrder : (existing.Count == 0 ? 0 : existing.Max(s => s.SortOrder) + 1);
        var sec = new CmsSection
        {
            SiteId = siteId,
            LayoutId = req.LayoutId,
            Culture = culture,
            Title = req.Title.Trim(),
            AnchorSlug = slug,
            NavVisible = req.NavVisible,
            SortOrder = nextOrder,
            BackgroundStyle = req.BackgroundStyle,
            IsEnabled = req.IsEnabled
        };
        await sections.AddAsync(sec, ct);
        await uow.SaveChangesAsync(ct);
        return sec.ToDto();
    }

    public async Task<CmsSectionDto> UpdateAsync(Guid id, UpsertCmsSectionRequest req, CancellationToken ct = default)
    {
        var sec = await sections.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("区块不存在。");
        await EnsureSiteEditableAsync(sec.SiteId, ct);
        if (req.LayoutId == Guid.Empty)
            throw new InvalidOperationException("请选择区块布局。");
        if (string.IsNullOrWhiteSpace(req.Title))
            throw new InvalidOperationException("区块名称不能为空。");
        var slug = req.AnchorSlug?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(slug))
            throw new InvalidOperationException("区块锚点标识不能为空。");
        if (await sections.ExistsSlugAsync(sec.SiteId, sec.Culture, slug, id, ct))
            throw new InvalidOperationException($"锚点标识 '{slug}' 在当前语言下已被占用。");

        sec.Update(req.LayoutId, req.Title, slug, req.NavVisible, req.SortOrder, req.BackgroundStyle, req.IsEnabled);
        await uow.SaveChangesAsync(ct);
        return sec.ToDto();
    }

    public async Task ReorderAsync(Guid siteId, List<Guid> orderedIds, CancellationToken ct = default)
    {
        await EnsureSiteEditableAsync(siteId, ct);
        var all = (await sections.ListBySiteAsync(siteId, null, ct)).ToDictionary(s => s.Id);
        for (var i = 0; i < orderedIds.Count; i++)
        {
            if (all.TryGetValue(orderedIds[i], out var sec))
                sec.SortOrder = i;
        }
        await uow.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var sec = await sections.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("区块不存在。");
        await EnsureSiteEditableAsync(sec.SiteId, ct);
        await widgets.RemoveBySectionAsync(id, ct);
        sections.Remove(sec);
        await uow.SaveChangesAsync(ct);
    }
}

public sealed class CmsWidgetAppService(
    ICmsWidgetRepository widgets,
    ICmsUnitOfWork uow,
    ICmsSiteRepository? sites = null)
{
    private async Task EnsureSiteEditableAsync(Guid siteId, CancellationToken ct)
    {
        if (sites is null) return;
        var site = await sites.GetByIdAsync(siteId, ct);
        if (site is not null && CmsSiteTemplateLibrary.IsBuiltInTemplateKey(site.SiteKey))
            throw new InvalidOperationException("内置官网模板为只读保护模板，不可直接编辑或删除，请先复制为新站点。");
    }

    public async Task<CmsWidgetDto> CreateAsync(UpsertCmsWidgetRequest req, CancellationToken ct = default)
    {
        await EnsureSiteEditableAsync(req.SiteId, ct);
        if (string.IsNullOrWhiteSpace(req.ZoneId))
            throw new InvalidOperationException("目标插槽不能为空。");
        if (string.IsNullOrWhiteSpace(req.WidgetType))
            throw new InvalidOperationException("微件类型不能为空。");
        if (string.IsNullOrWhiteSpace(req.Title))
            throw new InvalidOperationException("微件标题不能为空。");
        var widget = new CmsWidgetInstance
        {
            SiteId = req.SiteId,
            SectionId = req.SectionId,
            Culture = string.IsNullOrWhiteSpace(req.Culture) ? "zh-CN" : req.Culture.Trim(),
            ZoneId = req.ZoneId.Trim(),
            WidgetType = req.WidgetType.Trim(),
            Title = req.Title.Trim(),
            PropsJson = string.IsNullOrWhiteSpace(req.PropsJson) ? "{}" : req.PropsJson,
            StyleJson = req.StyleJson,
            SortOrder = req.SortOrder
        };
        await widgets.AddAsync(widget, ct);
        await uow.SaveChangesAsync(ct);
        return widget.ToDto();
    }

    public async Task<CmsWidgetDto> UpdateAsync(Guid id, UpsertCmsWidgetRequest req, CancellationToken ct = default)
    {
        var widget = await widgets.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("微件不存在。");
        await EnsureSiteEditableAsync(widget.SiteId, ct);
        if (string.IsNullOrWhiteSpace(req.ZoneId))
            throw new InvalidOperationException("目标插槽不能为空。");
        if (string.IsNullOrWhiteSpace(req.Title))
            throw new InvalidOperationException("微件标题不能为空。");
        widget.UpdateContent(req.Title, req.PropsJson, req.StyleJson);
        widget.MoveTo(req.SectionId, req.ZoneId, req.SortOrder);
        await uow.SaveChangesAsync(ct);
        return widget.ToDto();
    }

    public async Task<CmsWidgetDto> MoveAsync(Guid id, MoveCmsWidgetRequest req, CancellationToken ct = default)
    {
        var widget = await widgets.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("微件不存在。");
        await EnsureSiteEditableAsync(widget.SiteId, ct);
        widget.MoveTo(req.SectionId, req.ZoneId, req.SortOrder);
        await uow.SaveChangesAsync(ct);
        return widget.ToDto();
    }

    public async Task<List<CmsWidgetDto>> ListTemplatesAsync(CancellationToken ct = default) =>
        (await widgets.ListTemplatesAsync(ct)).Select(w => w.ToDto()).ToList();

    public async Task<CmsWidgetDto> SaveAsTemplateAsync(Guid id, string templateName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(templateName))
            throw new InvalidOperationException("模板名称不能为空。");
        var src = await widgets.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("微件不存在。");
        var tpl = new CmsWidgetInstance
        {
            SiteId = src.SiteId,
            SectionId = src.SectionId,
            ZoneId = src.ZoneId,
            WidgetType = src.WidgetType,
            Title = src.Title,
            PropsJson = src.PropsJson,
            StyleJson = src.StyleJson,
            SortOrder = 0
        };
        tpl.MarkAsTemplate(templateName);
        await widgets.AddAsync(tpl, ct);
        await uow.SaveChangesAsync(ct);
        return tpl.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var widget = await widgets.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("微件不存在。");
        await EnsureSiteEditableAsync(widget.SiteId, ct);
        widgets.Remove(widget);
        await uow.SaveChangesAsync(ct);
    }
}

public sealed class CmsLeadAppService(
    ICmsSiteRepository sites,
    ICmsLeadRepository leads,
    ICmsUnitOfWork uow,
    IQueuedEmailService? emailQueue = null,
    ISystemEmailProvider? systemEmail = null)
{
    public async Task<CmsLeadDto> SubmitAsync(SubmitCmsLeadRequest req, string? sourceIp, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new InvalidOperationException("姓名不能为空。");
        if (string.IsNullOrWhiteSpace(req.Message))
            throw new InvalidOperationException("咨询内容不能为空。");
        var key = string.IsNullOrWhiteSpace(req.SiteKey) ? "default" : req.SiteKey.Trim().ToLowerInvariant();
        var site = await sites.GetByKeyAsync(key, ct) ?? throw new KeyNotFoundException("站点不存在。");
        var lead = new CmsLeadSubmission
        {
            SiteId = site.Id,
            SectionId = req.SectionId,
            Name = req.Name.Trim(),
            Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim(),
            Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim(),
            Company = string.IsNullOrWhiteSpace(req.Company) ? null : req.Company.Trim(),
            Message = req.Message.Trim(),
            ExtraJson = req.ExtraJson,
            SourceIp = sourceIp
        };
        lead.MarkSubmitted();
        await leads.AddAsync(lead, ct);
        await uow.SaveChangesAsync(ct);
        await TryNotifyAdminByEmailAsync(site.Name, lead, ct);
        return lead.ToDto();
    }

    private async Task TryNotifyAdminByEmailAsync(string siteName, CmsLeadSubmission lead, CancellationToken ct)
    {
        if (emailQueue is null) return;
        var toEmail = systemEmail?.GetAdminEmail();
        if (string.IsNullOrWhiteSpace(toEmail)) return;

        var subject = $"[{siteName}] 收到新的官网咨询：{lead.Name}";
        var body = $"""
            <div style="font-family:system-ui,-apple-system,sans-serif;line-height:1.7;color:#1f2328;">
                <h2 style="margin:0 0 12px;">官网新咨询通知</h2>
                <p>站点 <strong>{WebUtility.HtmlEncode(siteName)}</strong> 收到一条新的客户咨询：</p>
                <table style="border-collapse:collapse;width:100%;max-width:560px;">
                    <tr><td style="padding:6px 10px;border:1px solid #e5e7eb;width:110px;background:#f8fafc;">姓名</td><td style="padding:6px 10px;border:1px solid #e5e7eb;">{WebUtility.HtmlEncode(lead.Name)}</td></tr>
                    <tr><td style="padding:6px 10px;border:1px solid #e5e7eb;background:#f8fafc;">电话</td><td style="padding:6px 10px;border:1px solid #e5e7eb;">{WebUtility.HtmlEncode(lead.Phone ?? "-")}</td></tr>
                    <tr><td style="padding:6px 10px;border:1px solid #e5e7eb;background:#f8fafc;">邮箱</td><td style="padding:6px 10px;border:1px solid #e5e7eb;">{WebUtility.HtmlEncode(lead.Email ?? "-")}</td></tr>
                    <tr><td style="padding:6px 10px;border:1px solid #e5e7eb;background:#f8fafc;">公司</td><td style="padding:6px 10px;border:1px solid #e5e7eb;">{WebUtility.HtmlEncode(lead.Company ?? "-")}</td></tr>
                    <tr><td style="padding:6px 10px;border:1px solid #e5e7eb;background:#f8fafc;">咨询内容</td><td style="padding:6px 10px;border:1px solid #e5e7eb;white-space:pre-wrap;">{WebUtility.HtmlEncode(lead.Message)}</td></tr>
                    <tr><td style="padding:6px 10px;border:1px solid #e5e7eb;background:#f8fafc;">提交时间</td><td style="padding:6px 10px;border:1px solid #e5e7eb;">{lead.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}</td></tr>
                </table>
            </div>
            """;

        try
        {
            await emailQueue.EnqueueAsync(new EnqueueEmailRequest(toEmail, subject, body, Priority: 1), ct);
            // 立即触发一次发送：成功即标记 SentOnUtc，后续定时任务不再重复发送
            await emailQueue.ProcessPendingAsync(maxBatch: 5, maxTries: 3, ct);
        }
        catch
        {
            // 邮件已入队列，若本次瞬态发送失败，由后台邮件队列任务按 maxTries=3 重试；成功一次即标记 SentOnUtc 停止发送
        }
    }

    public async Task<List<CmsLeadDto>> ListBySiteAsync(Guid siteId, CancellationToken ct = default) =>
        (await leads.ListBySiteAsync(siteId, ct)).OrderByDescending(l => l.CreatedAtUtc).Select(l => l.ToDto()).ToList();

    public async Task<CmsLeadDto> UpdateStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(status))
            throw new InvalidOperationException("状态不能为空。");
        var lead = await leads.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("留资记录不存在。");
        lead.Status = status.Trim();
        lead.MarkModified(DateTimeOffset.UtcNow, null);
        await uow.SaveChangesAsync(ct);
        return lead.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var lead = await leads.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("留资记录不存在。");
        leads.Remove(lead);
        await uow.SaveChangesAsync(ct);
    }
}
