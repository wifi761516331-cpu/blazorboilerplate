using System.Collections.ObjectModel;
using BlazorBoilerplate.Application.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Infrastructure.Localization;

/// <summary>
/// 宿主侧持久化多语言存储代理：
/// 未挂载模块时返回空结果，使 <see cref="PlatformLocalizationService"/> 无感回退到内置注册表；
/// 挂载模块后每次调用均在模块隔离 DI 的独立异步作用域中解析 <see cref="ILocalizationStore"/>，
/// 兼容模块内 Scoped 服务（如 EF Core DbContext），并在模块卸载时释放引用以保证 ALC 可回收。
/// </summary>
public sealed class DelegatingLocalizationStore : ILocalizationStore, ILocalizationStoreBridge
{
    private static readonly IReadOnlyDictionary<string, string> EmptyResources =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private sealed record AttachedTarget(string ModuleId, IServiceScopeFactory ScopeFactory);

    private readonly object _gate = new();
    private AttachedTarget? _attached;

    public bool IsAttached => Volatile.Read(ref _attached) is not null;
    public string? AttachedModuleId => Volatile.Read(ref _attached)?.ModuleId;

    public void Attach(string moduleId, IServiceProvider moduleServiceProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(moduleServiceProvider);

        var scopeFactory = moduleServiceProvider.GetRequiredService<IServiceScopeFactory>();
        lock (_gate)
        {
            _attached = new AttachedTarget(moduleId.Trim(), scopeFactory);
        }
    }

    public void Detach(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
            return;

        var id = moduleId.Trim();
        lock (_gate)
        {
            if (_attached is not null && string.Equals(_attached.ModuleId, id, StringComparison.OrdinalIgnoreCase))
                _attached = null;
        }
    }

    public async Task<IReadOnlyList<LanguageInfo>> GetLanguagesAsync(CancellationToken cancellationToken = default)
    {
        var target = Volatile.Read(ref _attached);
        if (target is null)
            return [];

        await using var scope = target.ScopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetService<ILocalizationStore>();
        return store is null ? [] : await store.GetLanguagesAsync(cancellationToken);
    }

    public async Task<string?> TryGetResourceAsync(string culture, string key, CancellationToken cancellationToken = default)
    {
        var target = Volatile.Read(ref _attached);
        if (target is null)
            return null;

        await using var scope = target.ScopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetService<ILocalizationStore>();
        return store is null ? null : await store.TryGetResourceAsync(culture, key, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetResourcesAsync(string culture, CancellationToken cancellationToken = default)
    {
        var target = Volatile.Read(ref _attached);
        if (target is null)
            return EmptyResources;

        await using var scope = target.ScopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetService<ILocalizationStore>();
        return store is null ? EmptyResources : await store.GetResourcesAsync(culture, cancellationToken);
    }

    public async Task SyncModuleResourcesAsync(
        string moduleId,
        IReadOnlyList<LocaleResourceEntry> entries,
        CancellationToken cancellationToken = default)
    {
        var target = Volatile.Read(ref _attached);
        if (target is null)
            return;

        await using var scope = target.ScopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetService<ILocalizationStore>();
        if (store is not null)
            await store.SyncModuleResourcesAsync(moduleId, entries, cancellationToken);
    }
}

