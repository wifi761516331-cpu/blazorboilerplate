namespace BlazorBoilerplate.Application.Platform;

/// <summary>业务活动日志条目。</summary>
public sealed record ActivityLogEntry(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    string SystemKeyword,
    string Comment,
    Guid? UserId,
    string? UserName,
    string? EntityName,
    string? EntityId,
    string? IpAddress);

/// <summary>业务活动日志服务。</summary>
public interface IActivityLogService
{
    /// <summary>记录一条业务活动日志。</summary>
    Task LogAsync(
        string systemKeyword,
        string comment,
        string? entityName = null,
        string? entityId = null,
        Guid? userId = null,
        string? userName = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);

    /// <summary>分页或按条件查询活动日志。</summary>
    Task<IReadOnlyList<ActivityLogEntry>> QueryAsync(
        string? keyword = null,
        string? systemKeyword = null,
        int take = 100,
        CancellationToken cancellationToken = default);
}

/// <summary>可配置消息模板。</summary>
public sealed record MessageTemplateItem(
    string Name,
    string Subject,
    string Body,
    bool IsActive,
    DateTimeOffset UpdatedAtUtc);

/// <summary>消息模板管理与占位符渲染服务。</summary>
public interface IMessageTemplateService
{
    /// <summary>获取全部消息模板。</summary>
    Task<IReadOnlyList<MessageTemplateItem>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>按名称获取模板。</summary>
    Task<MessageTemplateItem?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>保存或更新消息模板。</summary>
    Task<MessageTemplateItem> SaveAsync(MessageTemplateItem template, CancellationToken cancellationToken = default);

    /// <summary>使用 %Token% 占位符渲染模板主题与正文。</summary>
    Task<(string Subject, string Body)?> RenderAsync(
        string name,
        IReadOnlyDictionary<string, string?> tokens,
        CancellationToken cancellationToken = default);
}

