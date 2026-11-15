using System.Text.Json;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.ModuleRuntime.Endpoints;
using BlazorBoilerplate.ModuleRuntime.LoadContext;
using BlazorBoilerplate.ModuleRuntime.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BlazorBoilerplate.ModuleRuntime.Tests;

public sealed class ModuleStatePersistenceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "bb-mod-state-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void RegisterMultipleVersions_WithoutState_DefaultsToHighestSemanticVersion()
    {
        var catalog = new ModuleCatalog(new InMemoryModuleAudit());
        catalog.Register(CreatePackage("attachments", "1.0.0"), ModuleState.Installed);
        catalog.Register(CreatePackage("attachments", "1.0.5"), ModuleState.Installed);
        catalog.Register(CreatePackage("attachments", "1.0.2"), ModuleState.Installed);

        var descriptor = catalog.List().Single();
        Assert.Equal("1.0.5", descriptor.Version);
        Assert.Equal(3, descriptor.VersionCount);
    }

    [Fact]
    public async Task RefreshCatalogAsync_RestoresPersistedActiveVersion_AndLoadAllRespectsStoppedState()
    {
        WriteFakePackageOnDisk("demo", "1.0.0");
        WriteFakePackageOnDisk("demo", "1.0.2");
        WriteFakePackageOnDisk("demo", "1.0.5");

        var opts = Options.Create(new ModuleRuntimeOptions { PackagesRoot = _tempRoot, VerifyChecksum = false });
        var store = new ModuleStateStore(opts, NullLogger<ModuleStateStore>.Instance);
        store.Save("demo", "1.0.2", isRunning: true);

        var manager1 = CreateManager(opts, store);
        await manager1.RefreshCatalogAsync(CancellationToken.None);
        Assert.Equal("1.0.2", manager1.Get("demo").Version);

        // 模拟上次停用了 demo（仍保留当前激活版本 1.0.2），重启后 LoadAllAsync(respectPersistedState: true) 应跳过加载。
        store.Save("demo", "1.0.2", isRunning: false);
        var manager2 = CreateManager(opts, store);
        await manager2.RefreshCatalogAsync(CancellationToken.None);
        var results = await manager2.LoadAllAsync(CancellationToken.None, respectPersistedState: true);

        var desc = Assert.Single(results);
        Assert.Equal("1.0.2", desc.Version);
        Assert.Equal(ModuleState.Installed, desc.State);
    }

    private static ModulePackage CreatePackage(string id, string version) =>
        new(
            $"pkg/{id}/{version}",
            new ModuleManifest
            {
                Id = id,
                Name = id,
                Version = version,
                Publisher = "Test",
                EntryAssembly = $"{id}.dll",
                EntryType = $"{id}.Module",
                ContractVersion = PlatformContract.Version
            },
            $"pkg/{id}/{version}/backend/{id}.dll");

    private void WriteFakePackageOnDisk(string id, string version)
    {
        var dir = Path.Combine(_tempRoot, id, version);
        var backend = Path.Combine(dir, ModulePackageLayout.BackendFolder);
        Directory.CreateDirectory(backend);
        File.WriteAllBytes(Path.Combine(backend, $"{id}.dll"), [0x01, 0x02]);
        var manifest = new ModuleManifest
        {
            Id = id,
            Name = id,
            Version = version,
            Publisher = "Test",
            EntryAssembly = $"{id}.dll",
            EntryType = $"{id}.Module",
            ContractVersion = PlatformContract.Version
        };
        File.WriteAllText(Path.Combine(dir, ModulePackageLayout.ManifestFile), JsonSerializer.Serialize(manifest));
    }

    private static ModuleLifecycleManager CreateManager(IOptions<ModuleRuntimeOptions> opts, ModuleStateStore store)
    {
        var audit = new InMemoryModuleAudit();
        var packages = new ModulePackageManager(opts, audit, NullLogger<ModulePackageManager>.Instance);
        var discovery = new ModuleDiscovery(opts, packages, audit, NullLogger<ModuleDiscovery>.Instance);
        var catalog = new ModuleCatalog(audit);
        var alcFactory = new ModuleLoadContextFactory();
        var registry = new ModuleEndpointRegistry(new ModuleEndpointDataSource(), NullLogger<ModuleEndpointRegistry>.Instance);
        var hostServices = new ServiceCollection().BuildServiceProvider();
        return new ModuleLifecycleManager(
            opts,
            packages,
            discovery,
            catalog,
            alcFactory,
            registry,
            hostServices,
            NullLoggerFactory.Instance,
            audit,
            NullLogger<ModuleLifecycleManager>.Instance,
            store);
    }
}
