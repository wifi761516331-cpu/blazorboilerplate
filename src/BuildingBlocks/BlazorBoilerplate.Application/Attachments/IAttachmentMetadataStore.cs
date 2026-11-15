using BlazorBoilerplate.Domain.Attachments;

namespace BlazorBoilerplate.Application.Attachments;

/// <summary>
/// 附件元数据持久化契约（位于 BuildingBlocks，不依赖具体业务模块）。
/// </summary>
public interface IAttachmentMetadataStore
{
    Task<IReadOnlyList<Attachment>> GetListAsync(int? kind = null, Guid? ownerId = null, CancellationToken cancellationToken = default);
    Task<Attachment?> GetAsync(Guid id, int kind, CancellationToken cancellationToken = default);
    Task UpsertAsync(Attachment attachment, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, int kind, CancellationToken cancellationToken = default);
}

