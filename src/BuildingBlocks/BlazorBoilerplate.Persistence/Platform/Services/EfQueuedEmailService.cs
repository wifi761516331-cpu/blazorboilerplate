using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Application.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.Persistence.Platform;

/// <summary>基于 EF Core 的异步邮件发送队列服务。</summary>
public sealed class EfQueuedEmailService(
    IEmailSender emailSender,
    ILogger<EfQueuedEmailService> logger,
    IQueuedEmailService fallback) : IQueuedEmailService
{
    public async Task<QueuedEmailItem> EnqueueAsync(EnqueueEmailRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = await PlatformDbAccessor.CreateReadyDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (db is null)
            return await fallback.EnqueueAsync(request, cancellationToken).ConfigureAwait(false);

        var rec = new PlatformQueuedEmail
        {
            Priority = request.Priority,
            From = request.From,
            To = request.To,
            Subject = request.Subject,
            Body = request.Body,
            CreatedOnUtc = DateTimeOffset.UtcNow
        };
        db.QueuedEmails.Add(rec);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(rec);
    }

    public async Task<IReadOnlyList<QueuedEmailItem>> QueryAsync(bool? pendingOnly = null, int take = 100, CancellationToken cancellationToken = default)
    {
        await using var db = await PlatformDbAccessor.CreateReadyDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (db is null)
            return await fallback.QueryAsync(pendingOnly, take, cancellationToken).ConfigureAwait(false);

        var query = db.QueuedEmails.AsNoTracking();
        if (pendingOnly == true)
            query = query.Where(x => x.SentOnUtc == null);
        else if (pendingOnly == false)
            query = query.Where(x => x.SentOnUtc != null);

        var rows = await query
            .OrderByDescending(x => x.CreatedOnUtc)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToDto).ToArray();
    }

    public async Task<int> ProcessPendingAsync(int maxBatch = 20, int maxTries = 3, CancellationToken cancellationToken = default)
    {
        await using var db = await PlatformDbAccessor.CreateReadyDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (db is null)
            return await fallback.ProcessPendingAsync(maxBatch, maxTries, cancellationToken).ConfigureAwait(false);

        var pending = await db.QueuedEmails
            .Where(x => x.SentOnUtc == null && x.SentTries < maxTries)
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.CreatedOnUtc)
            .Take(Math.Clamp(maxBatch, 1, 100))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (pending.Count == 0)
            return 0;

        var sentCount = 0;
        foreach (var item in pending)
        {
            try
            {
                await emailSender.SendAsync(item.To, item.Subject, item.Body, cancellationToken).ConfigureAwait(false);
                item.SentOnUtc = DateTimeOffset.UtcNow;
                item.SentTries += 1;
                item.LastError = null;
                sentCount++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "发送队列邮件失败：{Id} -> {To}", item.Id, item.To);
                item.SentTries += 1;
                item.LastError = ex.Message;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return sentCount;
    }

    public async Task<bool> RequeueAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await PlatformDbAccessor.CreateReadyDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (db is null)
            return await fallback.RequeueAsync(id, cancellationToken).ConfigureAwait(false);

        var item = await db.QueuedEmails.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
        if (item is null)
            return false;

        item.SentTries = 0;
        item.SentOnUtc = null;
        item.LastError = null;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static QueuedEmailItem ToDto(PlatformQueuedEmail r) =>
        new(r.Id, r.Priority, r.From, r.To, r.Subject, r.Body, r.CreatedOnUtc, r.SentTries, r.SentOnUtc, r.LastError);
}

