using BlazorBoilerplate.ApiHost.Auth;
using BlazorBoilerplate.Application.Attachments;
using BlazorBoilerplate.Domain.Attachments;
using BlazorBoilerplate.Infrastructure.Attachments;
using BlazorBoilerplate.Modules.Identity.Persistence;
using BlazorBoilerplate.Modules.Identity.Services;
using BlazorBoilerplate.Shared.Features.Attachments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorBoilerplate.ApiHost.Controllers;

/// <summary>
/// 附件管理控制器（参考 Bluebird AttachmentController，基础存储与元数据位于 BuildingBlocks）：
/// 支持通用附件、用户头像、商品主图的上传、预览/下载与删除；
/// 存储路径完全由服务端按 {Id}_{Kind}{Ext} 生成，不信任客户端路径。
/// </summary>
[ApiController]
[Route("api/v1/[controller]/[action]")]
public sealed class AttachmentController(
    IdentityDbContext db,
    JwtTokenService tokens,
    CurrentUserFactory factory,
    IAttachmentStorage storage,
    IAttachmentMetadataStore metadataStore,
    AttachmentStorageOptions options,
    ILogger<AttachmentController> logger)
    : AppControllerBase(db, tokens, factory), IAttachmentController
{
    public const int MaxUploadSizeBytes = 11 * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp"
    };

    private static readonly Dictionary<string, string> AllowedGeneralContentTypes = new(AllowedImageContentTypes, StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain; charset=utf-8",
        [".md"] = "text/markdown; charset=utf-8",
        [".csv"] = "text/csv; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".zip"] = "application/zip",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    [HttpGet]
    public async Task<AttachmentDto[]> GetList(
        [FromQuery] AttachmentKind? kind = null,
        [FromQuery] Guid? ownerId = null,
        CancellationToken cancellationToken = default)
    {
        var rows = await metadataStore.GetListAsync(
            kind.HasValue ? (int)kind.Value : null,
            ownerId,
            cancellationToken);

        return rows.Select(ToDto).ToArray();
    }

    [HttpPost]
    [RequestSizeLimit(MaxUploadSizeBytes)]
    public async Task<ActionResult<AttachmentDto>> Upload(
        IFormFile? file,
        [FromForm] AttachmentKind kind = AttachmentKind.General,
        [FromForm] Guid? ownerId = null,
        CancellationToken cancellationToken = default)
    {
        var attachmentId = kind == AttachmentKind.UserProfileImage
            ? UserId
            : (kind == AttachmentKind.ProductPrimaryImage && ownerId.HasValue ? ownerId.Value : Guid.CreateVersion7());

        var effectiveOwnerId = kind == AttachmentKind.UserProfileImage ? UserId : ownerId;
        return await SaveAttachmentAsync(attachmentId, kind, effectiveOwnerId, file, cancellationToken);
    }

    [HttpPost]
    [RequestSizeLimit(MaxUploadSizeBytes)]
    public async Task<ActionResult<AttachmentDto>> UploadUserProfilePicture(
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        return await SaveAttachmentAsync(UserId, AttachmentKind.UserProfileImage, UserId, file, cancellationToken);
    }

    [AllowAnonymous]
    [HttpGet("/api/v1/attachments/{attachmentId}/{kind}")]
    [HttpGet("{attachmentId}/{kind}")]
    public async Task<IActionResult> GetAttachment(
        Guid attachmentId,
        AttachmentKind kind,
        [FromQuery] bool download = false,
        CancellationToken cancellationToken = default)
    {
        var entity = await metadataStore.GetAsync(attachmentId, (int)kind, cancellationToken);
        if (entity is null)
            return NotFound(new { error = "附件不存在。" });

        var stream = await storage.GetAsync(entity.Path, cancellationToken);
        if (stream is null)
            return NotFound(new { error = "附件文件内容不存在。" });

        return download
            ? File(stream, entity.ContentType, entity.FileName, enableRangeProcessing: true)
            : File(stream, entity.ContentType, enableRangeProcessing: true);
    }

    [HttpDelete("{attachmentId}/{kind}")]
    public async Task Delete(Guid attachmentId, AttachmentKind kind, CancellationToken cancellationToken)
    {
        await DeleteAttachmentInternalAsync(attachmentId, kind, cancellationToken);
    }

    [HttpDelete]
    public async Task DeleteUserProfilePicture(CancellationToken cancellationToken)
    {
        await DeleteAttachmentInternalAsync(UserId, AttachmentKind.UserProfileImage, cancellationToken);
    }

    private async Task<ActionResult<AttachmentDto>> SaveAttachmentAsync(
        Guid attachmentId,
        AttachmentKind kind,
        Guid? ownerId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length <= 0)
            return BadRequest(new { error = "请选择要上传的非空文件。" });

        var maxBytes = options.MaxUploadSizeBytes > 0 ? options.MaxUploadSizeBytes : MaxUploadSizeBytes;
        if (file.Length > maxBytes)
            return BadRequest(new { error = $"文件大小不能超过 {maxBytes / (1024 * 1024)} MB。" });

        var rawFileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(rawFileName))
            return BadRequest(new { error = "文件名无效。" });

        var ext = Path.GetExtension(rawFileName);
        var allowedMap = kind is AttachmentKind.UserProfileImage or AttachmentKind.ProductPrimaryImage
            ? AllowedImageContentTypes
            : AllowedGeneralContentTypes;

        if (string.IsNullOrWhiteSpace(ext) || !allowedMap.TryGetValue(ext, out var resolvedContentType))
        {
            var hint = kind is AttachmentKind.UserProfileImage or AttachmentKind.ProductPrimaryImage
                ? "仅支持 jpg/jpeg/png/webp/gif/bmp 图片格式。"
                : "不支持该文件格式。";
            return BadRequest(new { error = hint });
        }

        var normalizedExt = ext.ToLowerInvariant();
        var storageKey = BuildStorageKey(attachmentId, kind, normalizedExt);
        var kindInt = (int)kind;

        var existing = await metadataStore.GetAsync(attachmentId, kindInt, cancellationToken);
        var previousPath = existing?.Path;

        await using (var uploadStream = file.OpenReadStream())
        {
            await storage.PutAsync(storageKey, uploadStream, cancellationToken);
        }

        if (existing is null)
        {
            existing = new Attachment(
                attachmentId,
                kindInt,
                rawFileName,
                resolvedContentType,
                file.Length,
                storageKey,
                ownerId,
                UserId);
        }
        else
        {
            existing.UpdateMetadata(
                rawFileName,
                resolvedContentType,
                file.Length,
                storageKey,
                ownerId,
                UserId);
        }

        await metadataStore.UpsertAsync(existing, cancellationToken);

        if (!string.IsNullOrEmpty(previousPath) &&
            !string.Equals(previousPath, storageKey, StringComparison.OrdinalIgnoreCase))
        {
            await storage.DeleteAsync(previousPath, cancellationToken);
        }

        logger.LogInformation(
            "Attachment uploaded. Id={AttachmentId}, Kind={Kind}, FileName={FileName}, Size={SizeBytes}",
            existing.Id, kind, existing.FileName, existing.SizeBytes);

        return Ok(ToDto(existing));
    }

    private async Task DeleteAttachmentInternalAsync(Guid attachmentId, AttachmentKind kind, CancellationToken cancellationToken)
    {
        var kindInt = (int)kind;
        var entity = await metadataStore.GetAsync(attachmentId, kindInt, cancellationToken)
            ?? throw new KeyNotFoundException("附件不存在。");

        await storage.DeleteAsync(entity.Path, cancellationToken);
        await metadataStore.DeleteAsync(attachmentId, kindInt, cancellationToken);
    }

    private static string BuildStorageKey(Guid attachmentId, AttachmentKind kind, string normalizedExtension)
    {
        var folder = kind switch
        {
            AttachmentKind.UserProfileImage => "user-profiles",
            AttachmentKind.ProductPrimaryImage => "product-images",
            _ => "general"
        };
        return $"{folder}/{attachmentId:D}_{kind}{normalizedExtension}";
    }

    private static AttachmentDto ToDto(Attachment entity)
    {
        var kind = (AttachmentKind)entity.Kind;
        return new AttachmentDto(
            entity.Id,
            kind,
            entity.FileName,
            entity.ContentType,
            entity.SizeBytes,
            entity.OwnerId,
            entity.UploadedByUserId,
            entity.CreatedAtUtc,
            $"/api/v1/attachments/{entity.Id:D}/{kind}");
    }
}
