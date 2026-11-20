using System.Text.Json;
using BlazorBoilerplate.Application.Platform;

namespace BlazorBoilerplate.Infrastructure.Platform;

/// <summary>基于本地 JSON 环状缓冲的业务活动日志服务。</summary>
public sealed class JsonActivityLogService(string dataDirectory) : IActivityLogService
{
    private const int MaxEntries = 2000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _filePath = Path.Combine(dataDirectory, "activity-logs.json");
    private readonly SemaphoreSlim _lock = new(1, 1);

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
        var entry = new ActivityLogEntry(
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow,
            systemKeyword,
            comment,
            userId,
            userName,
            entityName,
            entityId,
            ipAddress);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            list.Insert(0, entry);
            if (list.Count > MaxEntries)
                list.RemoveRange(MaxEntries, list.Count - MaxEntries);
            await WriteUnsafeAsync(list, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<ActivityLogEntry>> QueryAsync(
        string? keyword = null,
        string? systemKeyword = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var list = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            IEnumerable<ActivityLogEntry> query = list;
            if (!string.IsNullOrWhiteSpace(systemKeyword))
                query = query.Where(x => string.Equals(x.SystemKeyword, systemKeyword, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(keyword))
                query = query.Where(x =>
                    x.Comment.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    x.SystemKeyword.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    (x.UserName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (x.EntityName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false));

            return query.Take(Math.Clamp(take, 1, 500)).ToArray();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<ActivityLogEntry>> ReadUnsafeAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath)) return [];
        try
        {
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<List<ActivityLogEntry>>(stream, JsonOptions, ct).ConfigureAwait(false) ?? [];
        }
        catch { return []; }
    }

    private async Task WriteUnsafeAsync(List<ActivityLogEntry> items, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tmp = _filePath + ".tmp";
        await using (var stream = File.Create(tmp))
            await JsonSerializer.SerializeAsync(stream, items, JsonOptions, ct).ConfigureAwait(false);
        File.Move(tmp, _filePath, overwrite: true);
    }
}

/// <summary>可配置消息模板与 %Token% 占位符渲染服务。</summary>
public sealed class JsonMessageTemplateService(string dataDirectory) : IMessageTemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _filePath = Path.Combine(dataDirectory, "message-templates.json");
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly MessageTemplateItem[] BuiltInTemplates =
    [
        new("User.Welcome", "欢迎加入 %Site.Name%", "<p>您好 %User.DisplayName%，欢迎加入 %Site.Name%。</p>", true, DateTimeOffset.UtcNow),
        new("Auth.EmailCode", "[%Site.Name%] 邮箱验证码", "<p>您的验证码为：<strong>%Auth.Code%</strong>，请在 %Auth.ExpireMinutes% 分钟内使用。</p>", true, DateTimeOffset.UtcNow),
        new("Tenant.Invite", "租户邀请：%Tenant.Name%", "<p>%Inviter.Name% 邀请您加入租户「%Tenant.Name%」。</p>", true, DateTimeOffset.UtcNow)
    ];

    public async Task<IReadOnlyList<MessageTemplateItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var map = await EnsureLoadedUnsafeAsync(cancellationToken).ConfigureAwait(false);
            return map.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<MessageTemplateItem?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var map = await EnsureLoadedUnsafeAsync(cancellationToken).ConfigureAwait(false);
            return map.TryGetValue(name, out var item) ? item : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<MessageTemplateItem> SaveAsync(MessageTemplateItem template, CancellationToken cancellationToken = default)
    {
        var updated = template with { UpdatedAtUtc = DateTimeOffset.UtcNow };
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var map = await EnsureLoadedUnsafeAsync(cancellationToken).ConfigureAwait(false);
            map[updated.Name] = updated;
            await WriteUnsafeAsync(map.Values.ToList(), cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<(string Subject, string Body)?> RenderAsync(
        string name,
        IReadOnlyDictionary<string, string?> tokens,
        CancellationToken cancellationToken = default)
    {
        var template = await GetByNameAsync(name, cancellationToken).ConfigureAwait(false);
        if (template is null || !template.IsActive)
            return null;

        var subject = ReplaceTokens(template.Subject, tokens);
        var body = ReplaceTokens(template.Body, tokens);
        return (subject, body);
    }

    private static string ReplaceTokens(string input, IReadOnlyDictionary<string, string?> tokens)
    {
        var result = input;
        foreach (var kv in tokens)
            result = result.Replace($"%{kv.Key}%", kv.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        return result;
    }

    private async Task<Dictionary<string, MessageTemplateItem>> EnsureLoadedUnsafeAsync(CancellationToken ct)
    {
        var map = new Dictionary<string, MessageTemplateItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in BuiltInTemplates)
            map[b.Name] = b;

        if (File.Exists(_filePath))
        {
            try
            {
                await using var stream = File.OpenRead(_filePath);
                var items = await JsonSerializer.DeserializeAsync<List<MessageTemplateItem>>(stream, JsonOptions, ct).ConfigureAwait(false);
                if (items is not null)
                    foreach (var item in items)
                        map[item.Name] = item;
            }
            catch { }
        }
        return map;
    }

    private async Task WriteUnsafeAsync(List<MessageTemplateItem> items, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tmp = _filePath + ".tmp";
        await using (var stream = File.Create(tmp))
            await JsonSerializer.SerializeAsync(stream, items, JsonOptions, ct).ConfigureAwait(false);
        File.Move(tmp, _filePath, overwrite: true);
    }
}

