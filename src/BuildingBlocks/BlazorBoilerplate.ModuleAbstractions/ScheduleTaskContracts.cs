namespace BlazorBoilerplate.ModuleAbstractions;

/// <summary>后台计划任务契约。</summary>
public interface IScheduleTask
{
    /// <summary>任务显示名称。</summary>
    string Name { get; }

    /// <summary>执行间隔（秒）。</summary>
    int Seconds { get; }

    /// <summary>默认是否启用。</summary>
    bool EnabledByDefault => true;

    /// <summary>出错后是否自动停用。</summary>
    bool StopOnError => false;

    /// <summary>执行任务逻辑。</summary>
    Task ExecuteAsync(CancellationToken cancellationToken);
}

/// <summary>计划任务运行状态快照。</summary>
public sealed record ScheduleTaskDescriptor(
    string Id,
    string Name,
    string ModuleId,
    int Seconds,
    bool Enabled,
    bool StopOnError,
    bool IsRunning,
    DateTimeOffset? LastStartUtc,
    DateTimeOffset? LastEndUtc,
    DateTimeOffset? LastSuccessUtc,
    string? LastError);

/// <summary>计划任务注册与调度管理接口。</summary>
public interface IScheduleTaskManager
{
    /// <summary>获取所有已注册的计划任务。</summary>
    IReadOnlyList<ScheduleTaskDescriptor> List();

    /// <summary>注册一个模块任务。</summary>
    void Register(string moduleId, string taskId, IScheduleTask task);

    /// <summary>注销指定模块的全部任务（热卸载时调用）。</summary>
    void UnregisterModule(string moduleId);

    /// <summary>更新任务启用状态或间隔。</summary>
    bool Update(string taskId, bool enabled, int? seconds = null);

    /// <summary>立即执行一次指定任务。</summary>
    Task<ScheduleTaskDescriptor?> RunNowAsync(string taskId, CancellationToken cancellationToken = default);
}

