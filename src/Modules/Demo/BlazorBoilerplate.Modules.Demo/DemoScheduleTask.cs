using BlazorBoilerplate.Application.Platform;
using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.Modules.Demo;

/// <summary>
/// 后台计划任务示例（实现 <see cref="IScheduleTask"/>）。
/// 模块加载时由运行时自动扫描并注册到 <see cref="IScheduleTaskManager"/>；
/// 默认禁用（EnabledByDefault = false），可在运维中心「计划任务」中启用或立即执行。
/// </summary>
public sealed class DemoScheduleTask(
    ILogger<DemoScheduleTask> logger,
    IActivityLogService? activityLog = null) : IScheduleTask
{
    public string Name => "Demo 模板心跳示例任务";
    public int Seconds => 300;
    public bool EnabledByDefault => false;
    public bool StopOnError => false;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("DemoScheduleTask executed at {TimeUtc}", DateTimeOffset.UtcNow);
        if (activityLog is not null)
        {
            await activityLog.LogAsync(
                "Demo.ScheduleTask",
                "Demo 示例计划任务执行成功。",
                entityName: nameof(DemoScheduleTask),
                cancellationToken: cancellationToken);
        }
    }
}

