using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Modules.Cms.Domain.Entities;

/// <summary>单页官网的逻辑区块（导航锚点/虚拟视图）。</summary>
public sealed class CmsSection : AuditableEntity<Guid>
{
    public CmsSection()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid SiteId { get; set; }
    public Guid LayoutId { get; set; }
    public string Culture { get; set; } = "zh-CN";
    public string Title { get; set; } = string.Empty;
    public string AnchorSlug { get; set; } = string.Empty;
    public bool NavVisible { get; set; } = true;
    public int SortOrder { get; set; }
    public string? BackgroundStyle { get; set; }
    public bool IsEnabled { get; set; } = true;

    public CmsSite? Site { get; set; }
    public CmsLayout? Layout { get; set; }
    public ICollection<CmsWidgetInstance> Widgets { get; set; } = [];
    public ICollection<CmsLeadSubmission> Leads { get; set; } = [];

    public void Update(Guid layoutId, string title, string anchorSlug, bool navVisible, int sortOrder, string? backgroundStyle, bool isEnabled)
    {
        LayoutId = layoutId;
        Title = title.Trim();
        AnchorSlug = anchorSlug.Trim().ToLowerInvariant();
        NavVisible = navVisible;
        SortOrder = sortOrder;
        BackgroundStyle = string.IsNullOrWhiteSpace(backgroundStyle) ? null : backgroundStyle.Trim();
        IsEnabled = isEnabled;
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }
}
