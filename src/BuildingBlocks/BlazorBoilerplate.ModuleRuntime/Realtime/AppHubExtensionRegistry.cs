using BlazorBoilerplate.ModuleAbstractions;

namespace BlazorBoilerplate.ModuleRuntime.Realtime;

/// <summary>线程安全的 AppHub 模块扩展注册表。</summary>
internal sealed class AppHubExtensionRegistry : IAppHubExtensionRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, IReadOnlyList<IAppHubExtension>> _byModule = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<IAppHubExtension> _snapshot = [];

    public void RegisterModule(string moduleId, IReadOnlyList<IAppHubExtension> extensions)
    {
        lock (_gate)
        {
            if (extensions.Count == 0)
                _byModule.Remove(moduleId);
            else
                _byModule[moduleId] = extensions;
            RebuildSnapshot();
        }
    }

    public void UnregisterModule(string moduleId)
    {
        lock (_gate)
        {
            if (_byModule.Remove(moduleId))
                RebuildSnapshot();
        }
    }

    public IReadOnlyList<IAppHubExtension> GetExtensions() => Volatile.Read(ref _snapshot);

    private void RebuildSnapshot()
    {
        _snapshot = _byModule.Values.SelectMany(x => x).ToArray();
    }
}

