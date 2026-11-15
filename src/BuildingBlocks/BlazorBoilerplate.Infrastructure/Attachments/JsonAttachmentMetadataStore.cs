using System.Text.Json;
using BlazorBoilerplate.Application.Attachments;
using BlazorBoilerplate.Domain.Attachments;

namespace BlazorBoilerplate.Infrastructure.Attachments;

/// <summary>
/// 基于本地 JSON 索引文件的附件元数据存储（原子写入 + 异步锁），
/// 使附件基础能力完全独立于 Identity 等业务模块。
/// </summary>
public sealed class JsonAttachmentMetadataStore : IAttachmentMetadataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _indexFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonAttachmentMetadataStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var fullRoot = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(fullRoot);
        _indexFilePath = Path.Combine(fullRoot, "_metadata.json");
    }

    public async Task<IReadOnlyList<Attachment>> GetListAsync(
        int? kind = null,
        Guid? ownerId = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await ReadAllUnsafeAsync(cancellationToken).ConfigureAwait(false);
            IEnumerable<Attachment> query = items.Where(a => !a.IsDeleted);
            if (kind.HasValue)
                query = query.Where(a => a.Kind == kind.Value);
            if (ownerId.HasValue)
                query = query.Where(a => a.OwnerId == ownerId.Value);

            return query
                .OrderByDescending(a => a.CreatedAtUtc)
                .Take(200)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Attachment?> GetAsync(Guid id, int kind, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await ReadAllUnsafeAsync(cancellationToken).ConfigureAwait(false);
            return items.FirstOrDefault(a => !a.IsDeleted && a.Id == id && a.Kind == kind);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertAsync(Attachment attachment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await ReadAllUnsafeAsync(cancellationToken).ConfigureAwait(false);
            items.RemoveAll(a => a.Id == attachment.Id && a.Kind == attachment.Kind);
            items.Add(attachment);
            await WriteAllUnsafeAsync(items, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid id, int kind, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = await ReadAllUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var target = items.FirstOrDefault(a => !a.IsDeleted && a.Id == id && a.Kind == kind);
            if (target is not null)
            {
                target.MarkDeleted(DateTimeOffset.UtcNow, null);
                await WriteAllUnsafeAsync(items, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<Attachment>> ReadAllUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_indexFilePath))
            return [];

        await using var stream = new FileStream(_indexFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        if (stream.Length == 0)
            return [];

        var items = await JsonSerializer.DeserializeAsync<List<Attachment>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return items ?? [];
    }

    private async Task WriteAllUnsafeAsync(List<Attachment> items, CancellationToken cancellationToken)
    {
        var tempPath = _indexFilePath + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, items, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        File.Move(tempPath, _indexFilePath, overwrite: true);
    }
}

