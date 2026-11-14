using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.ModuleRuntime.Runtime;
using Xunit;

namespace BlazorBoilerplate.ModuleRuntime.Tests;

/// <summary>
/// 动态模块「卸载后可再次加载」的状态机约束：
/// 卸载 = 停用并回收 ALC，包仍留在 modules/installed，状态应回到 Installed（而非 Uninstalled）。
/// 另外校验必要模块标记能从 manifest 透传到 descriptor。
/// </summary>
public class DynamicReloadTests
{
    [Fact]
    public void UnloadThenReload_IsAllowedByStateMachine()
    {
        Assert.True(ModuleStateMachine.CanMove(ModuleState.Running, ModuleState.Draining));
        Assert.True(ModuleStateMachine.CanMove(ModuleState.Draining, ModuleState.Unloading));
        Assert.True(ModuleStateMachine.CanMove(ModuleState.Unloading, ModuleState.Installed));
        Assert.True(ModuleStateMachine.CanMove(ModuleState.Installed, ModuleState.Loading));
        Assert.True(ModuleStateMachine.CanMove(ModuleState.Loading, ModuleState.Running));
    }

    [Fact]
    public void Uninstalled_CannotBeLoadedDirectly()
    {
        // Uninstalled 表示包已从目录移除，必须重新发现（Discovered→Installed）后才能加载。
        Assert.False(ModuleStateMachine.CanMove(ModuleState.Uninstalled, ModuleState.Loading));
    }

    [Fact]
    public void EssentialFlag_IsExposedInDescriptor()
    {
        var catalog = new ModuleCatalog(new InMemoryModuleAudit());
        var manifest = new ModuleManifest
        {
            Id = "identity",
            Name = "Identity",
            Version = "1.0.0",
            Publisher = "Test",
            EntryAssembly = "identity.dll",
            EntryType = "identity.Module",
            ContractVersion = "1.0",
            DisplayOrder = 10,
            IsEssential = true
        };
        var package = new ModulePackage("pkg", manifest, "pkg/backend/identity.dll");
        catalog.Register(package, ModuleState.Installed);

        var descriptor = catalog.List().Single();
        Assert.True(descriptor.IsEssential);
        Assert.Equal(10, descriptor.Order);
    }
}
