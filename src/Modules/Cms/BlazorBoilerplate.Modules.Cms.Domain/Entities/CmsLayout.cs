using BlazorBoilerplate.Domain;
using BlazorBoilerplate.Modules.Cms.Domain.Events;

namespace BlazorBoilerplate.Modules.Cms.Domain.Entities;

/// <summary>可复用的官网布局页模板（行/12栅格列/Zone插槽）。</summary>
public sealed class CmsLayout : AuditableEntity<Guid>, IAggregateRoot
{
    public CmsLayout()
    {
        Id = Guid.CreateVersion7();
    }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string RowsJson { get; set; } = "[]";
    public string? CustomCss { get; set; }
    public bool IsBuiltIn { get; set; }
    public int SortOrder { get; set; }

    public ICollection<CmsSection> Sections { get; set; } = [];

    public void NotifyChanged() => Raise(new CmsLayoutChangedEvent(Id));

    public void Update(string name, string? description, string rowsJson, string? customCss, string? thumbnailUrl, int sortOrder)
    {
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        RowsJson = string.IsNullOrWhiteSpace(rowsJson) ? "[]" : rowsJson;
        CustomCss = string.IsNullOrWhiteSpace(customCss) ? null : customCss;
        ThumbnailUrl = string.IsNullOrWhiteSpace(thumbnailUrl) ? null : thumbnailUrl.Trim();
        SortOrder = sortOrder;
        ModifiedAtUtc = DateTimeOffset.UtcNow;
        Raise(new CmsLayoutChangedEvent(Id));
    }
}
