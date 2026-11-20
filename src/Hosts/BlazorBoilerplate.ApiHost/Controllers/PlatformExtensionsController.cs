using BlazorBoilerplate.ApiHost.Auth;
using BlazorBoilerplate.Application.Platform;
using BlazorBoilerplate.Application.Search;
using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorBoilerplate.ApiHost.Controllers;

/// <summary>平台扩展</summary>
[ApiController, Authorize]
[Route("api/v1/[controller]/[action]")]
public sealed class PlatformExtensionsController(
    IScheduleTaskManager scheduleTasks,
    IActivityLogService activityLogs,
    IMessageTemplateService messageTemplates,
    IQueuedEmailService queuedEmails,
    IProviderManager providerManager,
    ISearchService searchService,
    ISettingService settingService) : ControllerBase
{
    /// <summary>任务列表</summary>
    [HttpGet]
    [RequirePermission("system.settings.read")]
    public IReadOnlyList<ScheduleTaskDescriptor> GetScheduleTasks() =>
        scheduleTasks.List();

    /// <summary>更新任务</summary>
    [HttpPut]
    [RequirePermission("system.settings.update")]
    public IActionResult UpdateScheduleTask([FromBody] UpdateScheduleTaskRequest request) =>
        scheduleTasks.Update(request.TaskId, request.Enabled, request.Seconds)
            ? Ok()
            : NotFound(new { error = "计划任务不存在。" });

    /// <summary>执行任务</summary>
    [HttpPost]
    [RequirePermission("system.settings.update")]
    public async Task<ActionResult<ScheduleTaskDescriptor>> RunScheduleTask(
        [FromQuery] string taskId,
        CancellationToken cancellationToken)
    {
        var result = await scheduleTasks.RunNowAsync(taskId, cancellationToken);
        return result is null ? NotFound(new { error = "计划任务不存在。" }) : Ok(result);
    }

    /// <summary>活动日志</summary>
    [HttpGet]
    [RequirePermission("system.settings.read")]
    public Task<IReadOnlyList<ActivityLogEntry>> GetActivityLogs(
        [FromQuery] string? keyword = null,
        [FromQuery] string? systemKeyword = null,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) =>
        activityLogs.QueryAsync(keyword, systemKeyword, take, cancellationToken);

    /// <summary>消息模板</summary>
    [HttpGet]
    [RequirePermission("system.settings.read")]
    public Task<IReadOnlyList<MessageTemplateItem>> GetMessageTemplates(CancellationToken cancellationToken) =>
        messageTemplates.GetAllAsync(cancellationToken);

    /// <summary>保存模板</summary>
    [HttpPut]
    [RequirePermission("system.settings.update")]
    public Task<MessageTemplateItem> SaveMessageTemplate(
        [FromBody] MessageTemplateItem request,
        CancellationToken cancellationToken) =>
        messageTemplates.SaveAsync(request, cancellationToken);

    /// <summary>模块配置</summary>
    [HttpGet]
    [RequirePermission("system.settings.read")]
    public Task<IReadOnlyDictionary<string, string>> GetModuleSettings(
        [FromQuery] string? prefix = null,
        [FromQuery] Guid? tenantId = null,
        CancellationToken cancellationToken = default) =>
        settingService.GetAllSettingsAsync(prefix, tenantId, cancellationToken);

    /// <summary>邮件队列</summary>
    [HttpGet]
    [RequirePermission("system.settings.read")]
    public Task<IReadOnlyList<QueuedEmailItem>> GetQueuedEmails(
        [FromQuery] bool? pendingOnly = null,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) =>
        queuedEmails.QueryAsync(pendingOnly, take, cancellationToken);

    /// <summary>入队邮件</summary>
    [HttpPost]
    [RequirePermission("system.settings.update")]
    public Task<QueuedEmailItem> EnqueueEmail(
        [FromBody] EnqueueEmailRequest request,
        CancellationToken cancellationToken) =>
        queuedEmails.EnqueueAsync(request, cancellationToken);

    /// <summary>处理队列</summary>
    [HttpPost]
    [RequirePermission("system.settings.update")]
    public Task<int> ProcessEmailQueue(CancellationToken cancellationToken) =>
        queuedEmails.ProcessPendingAsync(cancellationToken: cancellationToken);

    /// <summary>重发邮件</summary>
    [HttpPost]
    [RequirePermission("system.settings.update")]
    public async Task<IActionResult> RequeueEmail([FromQuery] Guid id, CancellationToken cancellationToken) =>
        await queuedEmails.RequeueAsync(id, cancellationToken)
            ? Ok()
            : NotFound(new { error = "邮件队列记录不存在。" });

    /// <summary>策略列表</summary>
    [HttpGet]
    [RequirePermission("system.settings.read")]
    public IReadOnlyList<ProviderStatusDto> GetProviders(
        [FromQuery] bool includeHidden = false,
        [FromQuery] Guid? tenantId = null) =>
        providerManager.GetAllMetadata(includeHidden)
            .Select(m => new ProviderStatusDto(
                m.SystemName,
                m.FriendlyName,
                m.ModuleId,
                m.Order,
                m.Icon,
                m.IsHidden,
                providerManager.IsEnabled(m.SystemName, tenantId)))
            .ToArray();

    /// <summary>启停策略</summary>
    [HttpPut]
    [RequirePermission("system.settings.update")]
    public async Task<IActionResult> SetProviderEnabled(
        [FromBody] SetProviderEnabledRequest request,
        CancellationToken cancellationToken)
    {
        await providerManager.SetEnabledAsync(request.SystemName, request.Enabled, request.TenantId, cancellationToken);
        return Ok();
    }

    /// <summary>全局搜索</summary>
    [HttpGet]
    public Task<IReadOnlyList<SearchHit>> Search(
        [FromQuery] string keyword,
        [FromQuery] string? indexName = null,
        [FromQuery] int top = 20,
        CancellationToken cancellationToken = default) =>
        searchService.SearchAsync(new SearchQuery(keyword, indexName, top), cancellationToken);

    /// <summary>更新任务</summary>
    public sealed record UpdateScheduleTaskRequest(string TaskId, bool Enabled, int? Seconds = null);

    /// <summary>启停策略</summary>
    public sealed record SetProviderEnabledRequest(string SystemName, bool Enabled, Guid? TenantId = null);

    /// <summary>策略状态</summary>
    public sealed record ProviderStatusDto(
        string SystemName,
        string FriendlyName,
        string ModuleId,
        int Order,
        string? Icon,
        bool IsHidden,
        bool Enabled);
}

