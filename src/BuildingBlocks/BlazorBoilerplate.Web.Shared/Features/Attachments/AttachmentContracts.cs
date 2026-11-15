using BlazorBoilerplate.Shared.Infrastructure.Services.Contracts;

namespace BlazorBoilerplate.Shared.Features.Attachments;

public enum AttachmentKind
{
    UserProfileImage = 0,
    ProductPrimaryImage = 1,
    General = 2
}

public sealed record AttachmentDto(
    Guid Id,
    AttachmentKind Kind,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid? OwnerId,
    Guid? UploadedByUserId,
    DateTimeOffset CreatedAtUtc,
    string DownloadUrl);

[Route("api/v1/[controller]/[action]/"), AuthorizedApi]
public interface IAttachmentController : IAppController
{
    [HttpGet]
    Task<AttachmentDto[]> GetList(AttachmentKind? kind = null, Guid? ownerId = null, CancellationToken cancellationToken = default);

    [HttpDelete("{attachmentId}/{kind}")]
    Task Delete(Guid attachmentId, AttachmentKind kind, CancellationToken cancellationToken);

    [HttpDelete]
    Task DeleteUserProfilePicture(CancellationToken cancellationToken);
}
