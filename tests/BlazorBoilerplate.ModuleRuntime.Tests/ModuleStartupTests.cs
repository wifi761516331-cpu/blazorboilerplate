using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorBoilerplate.ModuleRuntime.Tests;

public class ModuleStartupTests
{
    private sealed class DefaultOrderStartup : IModuleStartup
    {
        public void ConfigureServices(IServiceCollection services, IModuleContext context) { }
    }

    private sealed class OrderedStartup(int order, List<int> log) : IModuleStartup
    {
        public int Order => order;

        public void ConfigureServices(IServiceCollection services, IModuleContext context) =>
            log.Add(Order);
    }

    [Fact]
    public void ModuleManifest_DefaultDisplayOrder_Is3000()
    {
        var manifest = new ModuleManifest();
        Assert.Equal(3000, ModuleStartupDefaults.DefaultOrder);
        Assert.Equal(3000, manifest.DisplayOrder);
    }

    [Fact]
    public void ModuleStartup_DefaultOrder_Is3000()
    {
        IModuleStartup startup = new DefaultOrderStartup();
        Assert.Equal(3000, startup.Order);
    }

    [Fact]
    public void ModuleStartups_ExecuteInAscendingOrder()
    {
        var log = new List<int>();
        var startups = new IModuleStartup[]
        {
            new OrderedStartup(5000, log),
            new DefaultOrderStartup(),
            new OrderedStartup(100, log)
        };

        var services = new ServiceCollection();
        foreach (var s in startups.OrderBy(x => x.Order))
        {
            s.ConfigureServices(services, null!);
        }

        Assert.Equal([100, 5000], log);
    }
}
