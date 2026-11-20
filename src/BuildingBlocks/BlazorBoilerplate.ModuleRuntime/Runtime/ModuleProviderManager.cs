using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.ModuleRuntime.Runtime;

/// <summary>
/// 策略提供者管理器实现：支持延迟激活（Lazy&lt;T&gt;）、按租户启停持久化及模块热卸载自动注销。
/// </summary>
public sealed class ModuleProviderManager(string dataDirectory) : IProviderManager
{
    private sealed record ProviderRegistration(ProviderMetadata Metadata, IServiceProvider ModuleServices);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _stateFile = Path.Combine(dataDirectory, "provider-states.json");
    private readonly ConcurrentDictionary<string, ProviderRegistration> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, HashSet<string>>? _disabledByScope;

    public void RegisterModuleProviders(string moduleId, Assembly assembly, IServiceProvider moduleServices)
    {
        var providerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IProvider).IsAssignableFrom(t))
            .ToArray();

        foreach (var type in providerTypes)
        {
            var sysName = type.GetCustomAttribute<SystemNameAttribute>()?.SystemName ?? type.FullName ?? type.Name;
            var friendlyName = type.GetCustomAttribute<FriendlyNameAttribute>()?.FriendlyName ?? type.Name;
            var order = type.GetCustomAttribute<ProviderOrderAttribute>()?.Order ?? ModuleStartupDefaults.DefaultOrder;
            var icon = type.GetCustomAttribute<ProviderIconAttribute>()?.Icon;
            var hidden = type.GetCustomAttribute<HiddenProviderAttribute>() is not null;

            var meta = new ProviderMetadata(sysName, friendlyName, moduleId, type, order, icon, hidden);
            _registrations[sysName] = new ProviderRegistration(meta, moduleServices);
        }
    }

    public void UnregisterModule(string moduleId)
    {
        foreach (var kv in _registrations.Where(x => string.Equals(x.Value.Metadata.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase)).ToArray())
            _registrations.TryRemove(kv.Key, out _);
    }

    public Provider<TProvider>? GetProvider<TProvider>(string? systemName, Guid? tenantId = null)
        where TProvider : class, IProvider
    {
        if (string.IsNullOrWhiteSpace(systemName))
            return null;
        if (!_registrations.TryGetValue(systemName, out var reg))
            return null;
        if (!typeof(TProvider).IsAssignableFrom(reg.Metadata.ProviderType))
            return null;
        if (!IsEnabled(systemName, tenantId))
            return null;

        return CreateWrapper<TProvider>(reg);
    }

    public IReadOnlyList<Provider<TProvider>> GetAllProviders<TProvider>(Guid? tenantId = null, bool includeDisabled = false)
        where TProvider : class, IProvider
    {
        return _registrations.Values
            .Where(r => typeof(TProvider).IsAssignableFrom(r.Metadata.ProviderType))
            .Where(r => includeDisabled || IsEnabled(r.Metadata.SystemName, tenantId))
            .OrderBy(r => r.Metadata.Order)
            .ThenBy(r => r.Metadata.SystemName, StringComparer.OrdinalIgnoreCase)
            .Select(CreateWrapper<TProvider>)
            .ToArray();
    }

    public IReadOnlyList<ProviderMetadata> GetAllMetadata(bool includeHidden = false) =>
        _registrations.Values
            .Select(r => r.Metadata)
            .Where(m => includeHidden || !m.IsHidden)
            .OrderBy(m => m.Order)
            .ThenBy(m => m.SystemName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool IsEnabled(string systemName, Guid? tenantId = null)
    {
        var map = EnsureStateLoaded();
        var scope = (tenantId ?? Guid.Empty).ToString("N");
        if (map.TryGetValue(scope, out var tenantDisabled) && tenantDisabled.Contains(systemName))
            return false;
        if (tenantId.HasValue && tenantId.Value != Guid.Empty &&
            map.TryGetValue(Guid.Empty.ToString("N"), out var globalDisabled) &&
            globalDisabled.Contains(systemName))
            return false;
        return true;
    }

    public async Task SetEnabledAsync(string systemName, bool enabled, Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        var scope = (tenantId ?? Guid.Empty).ToString("N");
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var map = EnsureStateLoadedUnsafe();
            if (!map.TryGetValue(scope, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[scope] = set;
            }

            if (enabled)
                set.Remove(systemName);
            else
                set.Add(systemName);

            Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
            var serializable = map.ToDictionary(k => k.Key, v => v.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
            await using var stream = File.Create(_stateFile);
            await JsonSerializer.SerializeAsync(stream, serializable, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static Provider<TProvider> CreateWrapper<TProvider>(ProviderRegistration reg) where TProvider : class, IProvider
    {
        var lazy = new Lazy<TProvider>(() =>
            (TProvider)ActivatorUtilities.GetServiceOrCreateInstance(reg.ModuleServices, reg.Metadata.ProviderType));
        return new Provider<TProvider>(reg.Metadata, lazy);
    }

    private Dictionary<string, HashSet<string>> EnsureStateLoaded()
    {
        if (_disabledByScope is not null)
            return _disabledByScope;
        _lock.Wait();
        try { return EnsureStateLoadedUnsafe(); }
        finally { _lock.Release(); }
    }

    private Dictionary<string, HashSet<string>> EnsureStateLoadedUnsafe()
    {
        if (_disabledByScope is not null)
            return _disabledByScope;

        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(_stateFile))
        {
            try
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(_stateFile), JsonOptions);
                if (raw is not null)
                {
                    foreach (var kv in raw)
                        result[kv.Key] = new HashSet<string>(kv.Value, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { }
        }
        _disabledByScope = result;
        return _disabledByScope;
    }
}

