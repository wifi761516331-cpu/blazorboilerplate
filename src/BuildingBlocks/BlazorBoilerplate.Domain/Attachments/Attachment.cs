namespace BlazorBoilerplate.Domain.Attachments;

/// <summary>
/// 附件领域实体（参考 Bluebird Attachment：复合键 {Id, Kind}，Path 仅由服务端生成）。
/// </summary>
public sealed class Attachment
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
    }

    public Guid Id { get; set; }
    public int Kind { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Path { get; set; } = string.Empty;
    public Guid? OwnerId { get; set; }
    public Guid? UploadedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

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
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }
}

