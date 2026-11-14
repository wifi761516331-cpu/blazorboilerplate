using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.ModuleRuntime.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BlazorBoilerplate.ModuleRuntime.Tests;

/// <summary>
/// 动态端点 POC 的自动化断言：注册→快照上线、移除→快照下线、元数据与变更令牌。
/// 不依赖真实 WebApplicationFactory；运行时整链路见 docs/spikes/2026-11-14-ef-migrations-e2e.md。
/// </summary>
public class DynamicEndpointTests
{
    private static ModuleEndpointDefinition Def(string pattern, string method = "GET") => new()
    {
        Pattern = pattern,
        HttpMethod = method,
        Permission = "test.read",
        Handler = _ => Task.CompletedTask
    };

    private static (ModuleEndpointDataSource Source, ModuleEndpointRegistry Registry) NewRuntime()
    {
        var source = new ModuleEndpointDataSource();
        var registry = new ModuleEndpointRegistry(source, NullLogger<ModuleEndpointRegistry>.Instance);
        return (source, registry);
    }

    [Fact]
    public void Register_PublishesEndpointsWithStableMetadata()
    {
        var (source, registry) = NewRuntime();
        registry.Register("catalog", "1.0.0", [Def("/api/catalog/products"), Def("/api/catalog/products", "POST")]);

        Assert.Equal(2, source.Endpoints.Count);
        foreach (var endpoint in source.Endpoints)
        {
            var meta = endpoint.Metadata.GetMetadata<ModuleEndpointMetadata>();
            Assert.NotNull(meta);
            Assert.Equal("catalog", meta!.ModuleId);
            Assert.Equal("1.0.0", meta.Version);
            Assert.Equal("test.read", meta.Permission);
            Assert.NotNull(endpoint.Metadata.GetMetadata<HttpMethodMetadata>());
        }
    }

    [Fact]
    public void Remove_DropsEndpointsFromSnapshot()
    {
        var (source, registry) = NewRuntime();
        registry.Register("catalog", "1.0.0", [Def("/api/catalog/products")]);
        Assert.Single(source.Endpoints);

        registry.Remove("catalog");
        Assert.Empty(source.Endpoints);
    }

    [Fact]
    public void Register_ReplacesSnapshotAtomicallyAndSignalsChangeToken()
    {
        var (source, registry) = NewRuntime();
        var tokenBefore = source.GetChangeToken();
        var changed = false;
        tokenBefore.RegisterChangeCallback(_ => changed = true, null);

        registry.Register("identity", "1.0.0", [Def("/api/identity/users")]);

        Assert.True(changed, "EndpointDataSource 必须发出变更令牌，路由中间件才能重建 matcher");
        Assert.Single(source.Endpoints);
        Assert.NotSame(tokenBefore, source.GetChangeToken());

        registry.Clear();
        Assert.Empty(source.Endpoints);
    }
}
