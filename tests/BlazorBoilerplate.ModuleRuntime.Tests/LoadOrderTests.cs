using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.ModuleRuntime.Runtime;
using Xunit;

namespace BlazorBoilerplate.ModuleRuntime.Tests;

/// <summary>
/// 验证 nopCommerce 风格的模块加载顺序：
/// 1) DisplayOrder 升序；2) 依赖优先于 DisplayOrder；3) 可在中间任意插入模块；
/// 4) 缺失依赖与循环依赖被校验拦截。
/// </summary>
public class LoadOrderTests
{
    private static ModuleCatalog NewCatalog() => new(new InMemoryModuleAudit());

    private static void Add(ModuleCatalog catalog, string id, int displayOrder, string[]? dependencies = null)
    {
        var manifest = new ModuleManifest
        {
            Id = id,
            Name = id,
            Version = "1.0.0",
            Publisher = "Test",
            EntryAssembly = id + ".dll",
            EntryType = id + ".Module",
            ContractVersion = "1.0",
            DisplayOrder = displayOrder,
            Dependencies = dependencies ?? []
        };
        var package = new ModulePackage(
            Path.Combine("modules", "installed", id, "1.0.0"),
            manifest,
            Path.Combine("modules", "installed", id, "1.0.0", "backend", id + ".dll"));
        catalog.Register(package, ModuleState.Installed);
    }

    [Fact]
    public void ResolveLoadOrder_SortsByDisplayOrderAscending()
    {
        var catalog = NewCatalog();
        Add(catalog, "zeta", 300);
        Add(catalog, "alpha", 100);
        Add(catalog, "mid", 200);

        var order = catalog.ResolveLoadOrder();

        Assert.Equal(new[] { "alpha", "mid", "zeta" }, order);
    }

    [Fact]
    public void ResolveLoadOrder_DependencyTakesPrecedenceOverDisplayOrder()
    {
        var catalog = NewCatalog();
        // reports 的 DisplayOrder 更小（更想先加载），但它依赖 analytics。
        Add(catalog, "analytics", 500);
        Add(catalog, "reports", 100, ["analytics"]);

        var order = catalog.ResolveLoadOrder();

        Assert.Equal("analytics", order[0]);
        Assert.Equal("reports", order[1]);
    }

    [Fact]
    public void ResolveLoadOrder_InsertInMiddleDoesNotShiftOthers()
    {
        var catalog = NewCatalog();
        Add(catalog, "a", 100);
        Add(catalog, "c", 300);
        var before = catalog.ResolveLoadOrder();
        Assert.Equal(new[] { "a", "c" }, before);

        // 在中间插入一个模块，不应影响 a、c 的相对次序。
        Add(catalog, "b", 200);
        var after = catalog.ResolveLoadOrder();

        Assert.Equal(new[] { "a", "b", "c" }, after);
    }

    [Fact]
    public void ResolveLoadOrder_HonorsTransitiveDependencies()
    {
        var catalog = NewCatalog();
        Add(catalog, "c", 100, ["b"]);
        Add(catalog, "b", 200, ["a"]);
        Add(catalog, "a", 300);

        var order = catalog.ResolveLoadOrder();

        Assert.Equal(new[] { "a", "b", "c" }, order);
    }

    [Fact]
    public void ValidateLoadGraph_DetectsMissingDependency()
    {
        var catalog = NewCatalog();
        Add(catalog, "a", 100, ["ghost"]);

        var result = catalog.ValidateLoadGraph();

        Assert.False(result.IsValid);
        Assert.Contains(result.MissingDependencies, d => d.Contains("ghost"));
    }

    [Fact]
    public void ValidateLoadGraph_DetectsCycle()
    {
        var catalog = NewCatalog();
        Add(catalog, "a", 100, ["b"]);
        Add(catalog, "b", 200, ["a"]);

        var result = catalog.ValidateLoadGraph();

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Cycles);
    }

    [Fact]
    public void ListOrdered_ReflectsResolvedOrderWithDisplayOrderField()
    {
        var catalog = NewCatalog();
        Add(catalog, "c", 300);
        Add(catalog, "a", 100);
        Add(catalog, "b", 200);

        var ordered = catalog.ListOrdered().ToArray();

        Assert.Equal(new[] { "a", "b", "c" }, ordered.Select(d => d.Id));
        Assert.Equal(100, ordered[0].Order);
        Assert.Equal(200, ordered[1].Order);
        Assert.Equal(300, ordered[2].Order);
    }
}
