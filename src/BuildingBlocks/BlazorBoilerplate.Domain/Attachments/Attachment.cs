namespace BlazorBoilerplate.Domain.Attachments;

/// <summary>
/// 附件领域实体（参考 Bluebird Attachment：复合键 {Id, Kind}，Path 仅由服务端生成）。
/// </summary>
public sealed class Attachment : IAuditableEntity, ISoftDeletable
{
    public Attachment() { }

    public Attachment(
        Guid id,
        int kind,
        string fileName,
        string contentType,
        long sizeBytes,
        string path,
        Guid? ownerId,
        Guid? uploadedByUserId)
    {
        Id = id;
        Kind = kind;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Path = path;
        OwnerId = ownerId;
        UploadedByUserId = uploadedByUserId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        CreatedBy = uploadedByUserId;
    }

    public Guid Id { get; set; }
    public int Kind { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Path { get; set; } = string.Empty;
    public Guid? OwnerId { get; set; }
    public Guid? UploadedByUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? ModifiedAtUtc { get; set; }
    public Guid? ModifiedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public Guid? DeletedBy { get; set; }

    public void UpdateMetadata(
        string fileName,
        string contentType,
        long sizeBytes,
        string path,
        Guid? ownerId,
        Guid? uploadedByUserId)
    {
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Path = path;
        OwnerId = ownerId;
        UploadedByUserId = uploadedByUserId;
        MarkModified(DateTimeOffset.UtcNow, uploadedByUserId);
    }

    public void MarkCreated(DateTimeOffset utcNow, Guid? userId)
    {
        if (CreatedAtUtc == default) CreatedAtUtc = utcNow;
        CreatedBy ??= userId;
    }

    public void MarkModified(DateTimeOffset utcNow, Guid? userId)
    {
        ModifiedAtUtc = utcNow;
        if (userId.HasValue) ModifiedBy = userId;
    }

    public void MarkDeleted(DateTimeOffset utcNow, Guid? userId)
    {
        IsDeleted = true;
        DeletedAtUtc = utcNow;
        if (userId.HasValue) DeletedBy = userId;
        ModifiedAtUtc = utcNow;
        if (userId.HasValue) ModifiedBy = userId;
    }

    public void Restore(DateTimeOffset utcNow, Guid? userId)
    {
        IsDeleted = false;
        DeletedAtUtc = null;
        DeletedBy = null;
        ModifiedAtUtc = utcNow;
        if (userId.HasValue) ModifiedBy = userId;
    }
}

