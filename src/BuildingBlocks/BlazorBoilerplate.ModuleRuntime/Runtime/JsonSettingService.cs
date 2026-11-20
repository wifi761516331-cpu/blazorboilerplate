using System.Text.Json;
using BlazorBoilerplate.ModuleAbstractions;

namespace BlazorBoilerplate.ModuleRuntime.Runtime;

/// <summary>
/// 基于原子 JSON 文件的强类型配置存储（支持全局默认与租户覆盖）。
/// </summary>
public sealed class JsonSettingService(string dataDirectory) : ISettingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath = Path.Combine(dataDirectory, "module-settings.json");
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<T> LoadSettingAsync<T>(Guid? tenantId = null, CancellationToken cancellationToken = default)
        where T : class, ISettings, new()
    {
        var all = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var instance = new T();
        var prefix = typeof(T).Name + ".";

        var globalScope = Guid.Empty.ToString("N");
        var tenantScope = tenantId.HasValue && tenantId.Value != Guid.Empty ? tenantId.Value.ToString("N") : null;

        all.TryGetValue(globalScope, out var globalMap);
        Dictionary<string, string>? tenantMap = null;
        if (tenantScope is not null)
            all.TryGetValue(tenantScope, out tenantMap);

        foreach (var prop in typeof(T).GetProperties())
        {
            if (!prop.CanWrite) continue;
            var key = prefix + prop.Name;
            string? raw = null;
            if (tenantMap is not null && tenantMap.TryGetValue(key, out var tv))
                raw = tv;
            else if (globalMap is not null && globalMap.TryGetValue(key, out var gv))
                raw = gv;

            if (raw is null) continue;
            try
            {
                var val = JsonSerializer.Deserialize(raw, prop.PropertyType, JsonOptions);
                prop.SetValue(instance, val);
            }
            catch
            {
                // 忽略格式不兼容的历史配置项，保持默认值
            }
        }

        return instance;
    }

    public async Task SaveSettingAsync<T>(T settings, Guid? tenantId = null, CancellationToken cancellationToken = default)
        where T : class, ISettings, new()
    {
        ArgumentNullException.ThrowIfNull(settings);
        var scope = (tenantId ?? Guid.Empty).ToString("N");
        var prefix = typeof(T).Name + ".";

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var all = await ReadAllUnsafeAsync(cancellationToken).ConfigureAwait(false);
            if (!all.TryGetValue(scope, out var map))
            {
                map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                all[scope] = map;
            }

            foreach (var prop in typeof(T).GetProperties())
            {
                if (!prop.CanRead) continue;
                var val = prop.GetValue(settings);
                map[prefix + prop.Name] = JsonSerializer.Serialize(val, prop.PropertyType, JsonOptions);
            }

            await WriteAllUnsafeAsync(all, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteSettingAsync<T>(Guid? tenantId = null, CancellationToken cancellationToken = default)
        where T : class, ISettings, new()
    {
        var scope = (tenantId ?? Guid.Empty).ToString("N");
        var prefix = typeof(T).Name + ".";

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var all = await ReadAllUnsafeAsync(cancellationToken).ConfigureAwait(false);
            if (!all.TryGetValue(scope, out var map))
                return;

            foreach (var k in map.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
                map.Remove(k);

            await WriteAllUnsafeAsync(all, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(
        string? prefix = null,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var all = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var scope = (tenantId ?? Guid.Empty).ToString("N");
        if (!all.TryGetValue(scope, out var map))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(prefix))
            return new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);

        return map
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, Dictionary<string, string>>> ReadAllAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadAllUnsafeAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Dictionary<string, Dictionary<string, string>>> ReadAllUnsafeAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return new(StringComparer.OrdinalIgnoreCase);

        try
        {
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<Dictionary<string, Dictionary<string, string>>>(stream, JsonOptions, ct).ConfigureAwait(false)
                ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task WriteAllUnsafeAsync(Dictionary<string, Dictionary<string, string>> data, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tmp = _filePath + ".tmp";
        await using (var stream = File.Create(tmp))
            await JsonSerializer.SerializeAsync(stream, data, JsonOptions, ct).ConfigureAwait(false);
        File.Move(tmp, _filePath, overwrite: true);
    }
}

