using BlazorBoilerplate.Application.Abstractions;

namespace BlazorBoilerplate.Infrastructure.Attachments;

public sealed class AttachmentStorageOptions
{
    public const string SectionName = "Attachments";

    /// <summary>最大单文件上传大小（默认 11 MB，与 Bluebird 对齐）。</summary>
    public long MaxUploadSizeBytes { get; set; } = 11 * 1024 * 1024;

    /// <summary>Auto（未配置 MinIO 时走本地磁盘）、Local 或 S3。</summary>
    public string StorageMode { get; set; } = "Auto";

    /// <summary>本地附件存储目录（相对 ApiHost ContentRoot 或绝对路径）。</summary>
    public string LocalRootPath { get; set; } = "data/attachments";
}

public interface IAttachmentStorage
{
    Task PutAsync(string key, Stream content, CancellationToken cancellationToken);
    Task<Stream?> GetAsync(string key, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);
}

/// <summary>
/// 本地磁盘附件存储：与 Bluebird 一致，存储键完全由服务端 {Id}_{Kind} 决定，
/// 并校验规范化路径不越界，防止路径穿越。
/// </summary>
public sealed class LocalAttachmentStorage : IAttachmentStorage
{
    private readonly string _rootPath;

    public LocalAttachmentStorage(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public async Task PutAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        var fullPath = ResolveSafePath(key);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
    }

    public Task<Stream?> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = ResolveSafePath(key);
        if (!File.Exists(fullPath))
            return Task.FromResult<Stream?>(null);

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = ResolveSafePath(key);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(ResolveSafePath(key)));
    }

    private string ResolveSafePath(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var normalized = key.Replace('\\', '/').TrimStart('/');
        var candidate = Path.GetFullPath(Path.Combine(_rootPath, normalized));
        if (!candidate.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("非法附件路径。");
        return candidate;
    }
}

public sealed class S3AttachmentStorage(IFileStorage fileStorage) : IAttachmentStorage
{
    public Task PutAsync(string key, Stream content, CancellationToken cancellationToken) =>
        fileStorage.PutAsync(key, content, cancellationToken);

    public Task<Stream?> GetAsync(string key, CancellationToken cancellationToken) =>
        fileStorage.GetAsync(key, cancellationToken);

    public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        fileStorage.DeleteAsync(key, cancellationToken);

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        await using var stream = await fileStorage.GetAsync(key, cancellationToken).ConfigureAwait(false);
        return stream is not null;
    }
}

