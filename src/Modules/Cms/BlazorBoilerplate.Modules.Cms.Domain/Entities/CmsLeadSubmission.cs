using BlazorBoilerplate.Domain;
using BlazorBoilerplate.Modules.Cms.Domain.Events;

namespace BlazorBoilerplate.Modules.Cms.Domain.Entities;

/// <summary>官网访客留资/联系表单提交记录。</summary>
public sealed class CmsLeadSubmission : AuditableEntity<Guid>, IAggregateRoot
{
    public CmsLeadSubmission()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid SiteId { get; set; }
    public Guid? SectionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Company { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ExtraJson { get; set; }
    public string? SourceIp { get; set; }
    public string Status { get; set; } = "New";

    public CmsSite? Site { get; set; }
    public CmsSection? Section { get; set; }

    public void MarkSubmitted() => Raise(new CmsLeadSubmittedEvent(Id, SiteId, Name, Email, Phone));
}
