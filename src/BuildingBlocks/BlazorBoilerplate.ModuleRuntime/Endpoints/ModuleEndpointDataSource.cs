using System.Threading;
using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace BlazorBoilerplate.ModuleRuntime.Endpoints;

/// <summary>Endpoint 上携带的稳定元数据（不含模块私有 Type）。</summary>
public sealed record ModuleEndpointMetadata(string ModuleId, string Version, string Permission);

/// <summary>
/// 自定义 EndpointDataSource：通过变更令牌通知 ASP.NET Core 路由中间件重建 matcher。
/// 每次变更写入不可变快照，路由切换是原子的；卸载后新请求不再进入旧模块。
/// </summary>
public sealed class ModuleEndpointDataSource : EndpointDataSource
{
    private readonly object _gate = new();
    private IReadOnlyList<Endpoint> _endpoints = [];
    private CancellationTokenSource _changeSource = new();

    public override IReadOnlyList<Endpoint> Endpoints => Volatile.Read(ref _endpoints);

    public override IChangeToken GetChangeToken()
    {
        lock (_gate)
            return new CancellationChangeToken(_changeSource.Token);
    }

    public void Replace(IReadOnlyList<Endpoint> endpoints)
    {
        lock (_gate)
        {
            _endpoints = endpoints;
            var previous = _changeSource;
            _changeSource = new CancellationTokenSource();
            previous.Cancel();
            previous.Dispose();
        }
    }
}

/// <summary>
/// 聚合所有活动模块的 Endpoint 定义，重建后一次性替换快照。
/// Registry 只保存稳定契约里的 ModuleEndpointDefinition（其 Handler 是委托，
/// 卸载时必须连同 RouteEndpoint 一起从快照移除，否则会阻止 ALC 回收）。
/// </summary>
public sealed class ModuleEndpointRegistry(
    ModuleEndpointDataSource dataSource,
    ILogger<ModuleEndpointRegistry> logger)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ModuleRegistration> _registrations = new(StringComparer.OrdinalIgnoreCase);

    private sealed record ModuleRegistration(string ModuleId, string Version, IReadOnlyList<ModuleEndpointDefinition> Definitions);

    public int EndpointCount => dataSource.Endpoints.Count;

    public void Register(string moduleId, string version, IReadOnlyList<ModuleEndpointDefinition> definitions)
    {
        lock (_gate)
        {
            _registrations[moduleId] = new ModuleRegistration(moduleId, version, definitions);
            PublishSnapshot();
        }
    }

    public void Remove(string moduleId)
    {
        lock (_gate)
        {
            if (_registrations.Remove(moduleId))
                PublishSnapshot();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _registrations.Clear();
            PublishSnapshot();
        }
    }

    private void PublishSnapshot()
    {
        var snapshot = new List<Endpoint>();
        foreach (var registration in _registrations.Values)
        {
            foreach (var definition in registration.Definitions)
            {
                var metadata = new EndpointMetadataCollection(
                    new ModuleEndpointMetadata(registration.ModuleId, registration.Version, definition.Permission),
                    new HttpMethodMetadata([definition.HttpMethod]));

                var endpoint = new RouteEndpoint(
                    definition.Handler,
                    RoutePatternFactory.Parse(definition.Pattern),
                    order: 0,
                    metadata,
                    displayName: $"{registration.ModuleId}:{registration.Version} {definition.HttpMethod} {definition.Pattern}");

                snapshot.Add(endpoint);
            }
        }

        dataSource.Replace(snapshot);
        logger.LogInformation("Module endpoint snapshot replaced. Modules={Modules}, Endpoints={Endpoints}",
            _registrations.Count, snapshot.Count);
    }
}
