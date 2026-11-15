using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlazorBoilerplate.ModuleRuntime.Runtime;

/// <summary>
/// 单个模块的持久化运行状态：记录上次激活（运行）的版本以及是否处于运行状态。
/// </summary>
public sealed record PersistedModuleState(
    string ModuleId,
    string ActiveVersion,
    bool IsRunning,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// 将模块安装/运行版本与启停状态持久化到受控目录（<c>{PackagesRoot}/_state.json</c>），
/// 使宿主重启后能恢复上次各模块运行的准确版本与启停状态，而不是回退到磁盘枚举的最低版本。
/// </summary>
public sealed class ModuleStateStore(
    IOptions<ModuleRuntimeOptions> options,
    ILogger<ModuleStateStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();

    private string StateFilePath =>
        Path.Combine(Path.GetFullPath(options.Value.PackagesRoot), "_state.json");

    public IReadOnlyDictionary<string, PersistedModuleState> Load()
    {
        lock (_gate)
        {
            return LoadInternal();
        }
    }

    public void Save(string moduleId, string activeVersion, bool isRunning)
    {
        if (string.IsNullOrWhiteSpace(moduleId) || string.IsNullOrWhiteSpace(activeVersion))
            return;

        lock (_gate)
        {
            var map = LoadInternal();
            map[moduleId] = new PersistedModuleState(
                moduleId,
                activeVersion,
                isRunning,
                DateTimeOffset.UtcNow);
            WriteInternal(map);
        }
    }

    public void Remove(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
            return;

        lock (_gate)
        {
            var map = LoadInternal();
            if (map.Remove(moduleId))
                WriteInternal(map);
        }
    }

    private Dictionary<string, PersistedModuleState> LoadInternal()
    {
        var path = StateFilePath;
        if (!File.Exists(path))
            return new Dictionary<string, PersistedModuleState>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, PersistedModuleState>(StringComparer.OrdinalIgnoreCase);

            var list = JsonSerializer.Deserialize<List<PersistedModuleState>>(json, JsonOptions);
            var map = new Dictionary<string, PersistedModuleState>(StringComparer.OrdinalIgnoreCase);
            if (list is null)
                return map;

            foreach (var item in list)
            {
                if (!string.IsNullOrWhiteSpace(item.ModuleId) && !string.IsNullOrWhiteSpace(item.ActiveVersion))
                    map[item.ModuleId] = item;
            }
            return map;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read module state file '{Path}'; starting with empty state.", path);
            return new Dictionary<string, PersistedModuleState>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void WriteInternal(Dictionary<string, PersistedModuleState> map)
    {
        var path = StateFilePath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var ordered = map.Values
            .OrderBy(x => x.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var tmpPath = path + ".tmp";
        var json = JsonSerializer.Serialize(ordered, JsonOptions);
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, path, overwrite: true);
    }
}

