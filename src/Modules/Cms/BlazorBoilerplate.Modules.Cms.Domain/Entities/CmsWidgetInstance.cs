using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Modules.Cms.Domain.Entities;

/// <summary>挂载在区块插槽中的微件实例（支持存为模板）。</summary>
public sealed class CmsWidgetInstance : AuditableEntity<Guid>
{
    public CmsWidgetInstance()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid SiteId { get; set; }
    public Guid SectionId { get; set; }
    public string Culture { get; set; } = "zh-CN";
    public string ZoneId { get; set; } = "main";
    public string WidgetType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string PropsJson { get; set; } = "{}";
    public string? StyleJson { get; set; }
    public int SortOrder { get; set; }
    public bool IsTemplate { get; set; }
    public string? TemplateName { get; set; }

    public CmsSite? Site { get; set; }
    public CmsSection? Section { get; set; }

    public void UpdateContent(string title, string propsJson, string? styleJson)
    {
        Title = title.Trim();
        PropsJson = string.IsNullOrWhiteSpace(propsJson) ? "{}" : propsJson;
        StyleJson = string.IsNullOrWhiteSpace(styleJson) ? null : styleJson;
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }

    public void MoveTo(Guid sectionId, string zoneId, int sortOrder)
    {
        SectionId = sectionId;
        ZoneId = zoneId.Trim();
        SortOrder = sortOrder;
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkAsTemplate(string templateName)
    {
        IsTemplate = true;
        TemplateName = templateName.Trim();
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }
}
