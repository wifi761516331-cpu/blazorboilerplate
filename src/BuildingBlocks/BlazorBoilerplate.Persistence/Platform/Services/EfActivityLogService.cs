using BlazorBoilerplate.Application.Platform;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Persistence.Platform;

/// <summary>基于 EF Core 的业务活动日志服务。</summary>
public sealed class EfActivityLogService(IActivityLogService fallback) : IActivityLogService
{
    public async Task LogAsync(
        string systemKeyword,
        string comment,
        string? entityName = null,
        string? entityId = null,
        Guid? userId = null,
        string? userName = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await PlatformDbAccessor.CreateReadyDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (db is null)
        {
            await fallback.LogAsync(systemKeyword, comment, entityName, entityId, userId, userName, ipAddress, cancellationToken).ConfigureAwait(false);
            return;
        }

        db.ActivityLogs.Add(new PlatformActivityLog
        {
            SystemKeyword = systemKeyword,
            Comment = comment,
            EntityName = entityName,
            EntityId = entityId,
            UserId = userId,
            UserName = userName,
            IpAddress = ipAddress
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ActivityLogEntry>> QueryAsync(
        string? keyword = null,
        string? systemKeyword = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        await using var db = await PlatformDbAccessor.CreateReadyDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (db is null)
            return await fallback.QueryAsync(keyword, systemKeyword, take, cancellationToken).ConfigureAwait(false);

        var query = db.ActivityLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(systemKeyword))
            query = query.Where(x => x.SystemKeyword == systemKeyword);
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(x =>
                x.Comment.Contains(keyword) ||
                x.SystemKeyword.Contains(keyword) ||
                (x.UserName != null && x.UserName.Contains(keyword)));

        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(x => new ActivityLogEntry(
            x.Id, x.CreatedAtUtc, x.SystemKeyword, x.Comment,
            x.UserId, x.UserName, x.EntityName, x.EntityId, x.IpAddress)).ToArray();
    }
}

