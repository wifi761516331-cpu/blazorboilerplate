using BlazorBoilerplate.Application.Platform;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Persistence.Platform;

/// <summary>基于 EF Core 的消息模板服务。</summary>
public sealed class EfMessageTemplateService(IMessageTemplateService fallback) : IMessageTemplateService
{
    public async Task<IReadOnlyList<MessageTemplateItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await PlatformDbAccessor.CreateReadyDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (db is null)
            return await fallback.GetAllAsync(cancellationToken).ConfigureAwait(false);

        var rows = await db.MessageTemplates.AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(x => new MessageTemplateItem(x.Name, x.Subject, x.Body, x.IsActive, x.UpdatedAtUtc)).ToArray();
    }

    public async Task<MessageTemplateItem?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var db = await PlatformDbAccessor.CreateReadyDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (db is null)
            return await fallback.GetByNameAsync(name, cancellationToken).ConfigureAwait(false);

        var row = await db.MessageTemplates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Name == name, cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : new MessageTemplateItem(row.Name, row.Subject, row.Body, row.IsActive, row.UpdatedAtUtc);
    }

    public async Task<MessageTemplateItem> SaveAsync(MessageTemplateItem template, CancellationToken cancellationToken = default)
    {
        await using var db = await PlatformDbAccessor.CreateReadyDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (db is null)
            return await fallback.SaveAsync(template, cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var existing = await db.MessageTemplates.FirstOrDefaultAsync(x => x.Name == template.Name, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            db.MessageTemplates.Add(new PlatformMessageTemplate
            {
                Name = template.Name,
                Subject = template.Subject,
                Body = template.Body,
                IsActive = template.IsActive,
                UpdatedAtUtc = now
            });
        }
        else
        {
            existing.Subject = template.Subject;
            existing.Body = template.Body;
            existing.IsActive = template.IsActive;
            existing.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return template with { UpdatedAtUtc = now };
    }

    public async Task<(string Subject, string Body)?> RenderAsync(
        string name,
        IReadOnlyDictionary<string, string?> tokens,
        CancellationToken cancellationToken = default)
    {
        var template = await GetByNameAsync(name, cancellationToken).ConfigureAwait(false);
        if (template is null || !template.IsActive)
            return null;

        var subject = template.Subject;
        var body = template.Body;
        foreach (var (key, value) in tokens)
        {
            var token = $"%{key}%";
            subject = subject.Replace(token, value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            body = body.Replace(token, value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        return (subject, body);
    }
}

