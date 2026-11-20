namespace BlazorBoilerplate.Application.Platform;

/// <summary>邮件队列条目。</summary>
public sealed record QueuedEmailItem(
    Guid Id,
    int Priority,
    string? From,
    string To,
    string Subject,
    string Body,
    DateTimeOffset CreatedOnUtc,
    int SentTries,
    DateTimeOffset? SentOnUtc,
    string? LastError);

/// <summary>入队邮件请求。</summary>
public sealed record EnqueueEmailRequest(
    string To,
    string Subject,
    string Body,
    int Priority = 5,
    string? From = null);

/// <summary>异步邮件发送队列服务。</summary>
public interface IQueuedEmailService
{
    /// <summary>将邮件加入发送队列。</summary>
    Task<QueuedEmailItem> EnqueueAsync(EnqueueEmailRequest request, CancellationToken cancellationToken = default);

    /// <summary>查询邮件队列。</summary>
    Task<IReadOnlyList<QueuedEmailItem>> QueryAsync(bool? pendingOnly = null, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>批量处理待发送邮件，返回本批成功发送数量。</summary>
    Task<int> ProcessPendingAsync(int maxBatch = 20, int maxTries = 3, CancellationToken cancellationToken = default);

    /// <summary>重置失败邮件为待发送状态。</summary>
    Task<bool> RequeueAsync(Guid id, CancellationToken cancellationToken = default);
}

