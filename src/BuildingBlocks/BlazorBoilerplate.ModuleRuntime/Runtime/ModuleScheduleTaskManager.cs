using System.Collections.Concurrent;
using BlazorBoilerplate.Application.Platform;
using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.ModuleRuntime.Runtime;

/// <summary>计划任务管理器（支持模块注册/注销、分布式/本地防重入锁、手动触发与状态追踪）。</summary>
public sealed class ModuleScheduleTaskManager(
    ILogger<ModuleScheduleTaskManager> logger,
    IDistributedLockService? distributedLock = null) : IScheduleTaskManager
{
    private sealed class Entry
    {
        public required string Id { get; init; }
        public required string ModuleId { get; init; }
        public required IScheduleTask Task { get; init; }
        public required SemaphoreSlim Gate { get; init; }
        public int Seconds;
        public bool Enabled;
        public bool IsRunning;
        public DateTimeOffset? LastStartUtc;
        public DateTimeOffset? LastEndUtc;
        public DateTimeOffset? LastSuccessUtc;
        public string? LastError;

        public ScheduleTaskDescriptor ToDescriptor() => new(
            Id, Task.Name, ModuleId, Seconds, Enabled, Task.StopOnError,
            IsRunning, LastStartUtc, LastEndUtc, LastSuccessUtc, LastError);
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ScheduleTaskDescriptor> List() =>
        _entries.Values
            .OrderBy(e => e.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Task.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e => e.ToDescriptor())
            .ToArray();

    public void Register(string moduleId, string taskId, IScheduleTask task)
    {
        _entries[taskId] = new Entry
        {
            Id = taskId,
            ModuleId = moduleId,
            Task = task,
            Gate = new SemaphoreSlim(1, 1),
            Seconds = Math.Max(1, task.Seconds),
            Enabled = task.EnabledByDefault
        };
    }

    public void UnregisterModule(string moduleId)
    {
        foreach (var kv in _entries.Where(x => string.Equals(x.Value.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            if (_entries.TryRemove(kv.Key, out var removed))
                removed.Gate.Dispose();
        }
    }

    public bool Update(string taskId, bool enabled, int? seconds = null)
    {
        if (!_entries.TryGetValue(taskId, out var entry))
            return false;
        entry.Enabled = enabled;
        if (seconds.HasValue && seconds.Value > 0)
            entry.Seconds = seconds.Value;
        return true;
    }

    public async Task<ScheduleTaskDescriptor?> RunNowAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(taskId, out var entry))
            return null;
        await ExecuteEntryAsync(entry, cancellationToken).ConfigureAwait(false);
        return entry.ToDescriptor();
    }

    internal async Task RunDueTasksAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in _entries.Values)
        {
            if (!entry.Enabled || entry.IsRunning)
                continue;

            var due = !entry.LastStartUtc.HasValue || (now - entry.LastStartUtc.Value).TotalSeconds >= entry.Seconds;
            if (!due)
                continue;

            await ExecuteEntryAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExecuteEntryAsync(Entry entry, CancellationToken cancellationToken)
    {
        if (!await entry.Gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return;

        await using var distHandle = distributedLock is null
            ? null
            : await distributedLock.TryAcquireAsync(
                $"lock:task:{entry.Id}",
                TimeSpan.FromSeconds(Math.Max(60, entry.Seconds)),
                cancellationToken).ConfigureAwait(false);
        if (distributedLock is not null && distHandle is null)
        {
            entry.Gate.Release();
            return;
        }

        entry.IsRunning = true;
        entry.LastStartUtc = DateTimeOffset.UtcNow;
        entry.LastError = null;
        try
        {
            await entry.Task.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            entry.LastSuccessUtc = DateTimeOffset.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            entry.LastError = ex.GetBaseException().Message;
            logger.LogError(ex, "计划任务执行失败：{TaskId} ({TaskName})", entry.Id, entry.Task.Name);
            if (entry.Task.StopOnError)
                entry.Enabled = false;
        }
        finally
        {
            entry.LastEndUtc = DateTimeOffset.UtcNow;
            entry.IsRunning = false;
            entry.Gate.Release();
        }
    }
}

/// <summary>计划任务后台巡检调度器。</summary>
public sealed class ScheduleTaskBackgroundService(IScheduleTaskManager manager) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (manager is not ModuleScheduleTaskManager impl)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await impl.RunDueTasksAsync(stoppingToken).ConfigureAwait(false);
        }
    }
}

