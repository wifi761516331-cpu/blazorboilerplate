using System.Text.Json;
using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Application.Platform;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.Infrastructure.Platform;

/// <summary>基于原子 JSON 队列的异步邮件发送与失败重试服务。</summary>
public sealed class JsonQueuedEmailService(
    string dataDirectory,
    IEmailSender emailSender,
    ILogger<JsonQueuedEmailService> logger) : IQueuedEmailService
{
    private const int MaxStored = 2000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _filePath = Path.Combine(dataDirectory, "queued-emails.json");
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<QueuedEmailItem> EnqueueAsync(EnqueueEmailRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.To);
        var item = new QueuedEmailItem(
            Guid.CreateVersion7(),
            request.Priority,
            request.From,
            request.To.Trim(),
            request.Subject,
            request.Body,
            DateTimeOffset.UtcNow,
            0,
            null,
            null);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            list.Insert(0, item);
            if (list.Count > MaxStored)
                list.RemoveRange(MaxStored, list.Count - MaxStored);
            await WriteUnsafeAsync(list, cancellationToken).ConfigureAwait(false);
            return item;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<QueuedEmailItem>> QueryAsync(
        bool? pendingOnly = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            IEnumerable<QueuedEmailItem> query = list;
            if (pendingOnly == true)
                query = query.Where(x => x.SentOnUtc is null);
            else if (pendingOnly == false)
                query = query.Where(x => x.SentOnUtc is not null);

            return query.Take(Math.Clamp(take, 1, 500)).ToArray();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> ProcessPendingAsync(int maxBatch = 20, int maxTries = 3, CancellationToken cancellationToken = default)
    {
        List<QueuedEmailItem> candidates;
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            candidates = list
                .Where(x => x.SentOnUtc is null && x.SentTries < maxTries)
                .OrderByDescending(x => x.Priority)
                .ThenBy(x => x.CreatedOnUtc)
                .Take(Math.Max(1, maxBatch))
                .ToList();
        }
        finally
        {
            _lock.Release();
        }

        if (candidates.Count == 0)
            return 0;

        var sentCount = 0;
        var updates = new Dictionary<Guid, (int Tries, DateTimeOffset? SentOn, string? Error)>();

        foreach (var item in candidates)
        {
            try
            {
                await emailSender.SendAsync(item.To, item.Subject, item.Body, cancellationToken).ConfigureAwait(false);
                updates[item.Id] = (item.SentTries + 1, DateTimeOffset.UtcNow, null);
                sentCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var err = ex.GetBaseException().Message;
                updates[item.Id] = (item.SentTries + 1, null, err);
                logger.LogWarning(ex, "邮件队列发送失败：Id={Id}, To={To}, Tries={Tries}", item.Id, item.To, item.SentTries + 1);
            }
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            for (var i = 0; i < list.Count; i++)
            {
                if (updates.TryGetValue(list[i].Id, out var u))
                {
                    list[i] = list[i] with
                    {
                        SentTries = u.Tries,
                        SentOnUtc = u.SentOn,
                        LastError = u.Error
                    };
                }
            }
            await WriteUnsafeAsync(list, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }

        return sentCount;
    }

    public async Task<bool> RequeueAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var idx = list.FindIndex(x => x.Id == id);
            if (idx < 0) return false;
            list[idx] = list[idx] with { SentTries = 0, SentOnUtc = null, LastError = null };
            await WriteUnsafeAsync(list, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<QueuedEmailItem>> ReadUnsafeAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath)) return [];
        try
        {
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<List<QueuedEmailItem>>(stream, JsonOptions, ct).ConfigureAwait(false) ?? [];
        }
        catch { return []; }
    }

    private async Task WriteUnsafeAsync(List<QueuedEmailItem> items, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tmp = _filePath + ".tmp";
        await using (var stream = File.Create(tmp))
            await JsonSerializer.SerializeAsync(stream, items, JsonOptions, ct).ConfigureAwait(false);
        File.Move(tmp, _filePath, overwrite: true);
    }
}

