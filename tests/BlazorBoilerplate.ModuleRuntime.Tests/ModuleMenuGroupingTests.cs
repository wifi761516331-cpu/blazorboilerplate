using System.Text.Json;
using BlazorBoilerplate.ModuleAbstractions;
using Xunit;

namespace BlazorBoilerplate.ModuleRuntime.Tests;

public sealed class ModuleMenuGroupingTests
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void CmsManifest_DeclaresSameGroupAndIconForBothMenus()
    {
        var manifestPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "Modules", "Cms", "BlazorBoilerplate.Modules.Cms", "manifest.json"));
        Assert.True(File.Exists(manifestPath), $"Expected manifest at {manifestPath}");

        var manifest = JsonSerializer.Deserialize<ModuleManifest>(File.ReadAllText(manifestPath), ManifestJsonOptions);
        Assert.NotNull(manifest);
        Assert.True(manifest.Frontend.DomainRoutable);
        Assert.Equal(2, manifest.Frontend.MenuItems.Length);

        var cmsMenu = manifest.Frontend.MenuItems.Single(m => m.Route == "/cms");
        var layoutMenu = manifest.Frontend.MenuItems.Single(m => m.Route == "/cms/layouts");

        Assert.Equal("内容管理", cmsMenu.Group);
        Assert.Equal("内容管理", layoutMenu.Group);
        Assert.Equal("EditList", cmsMenu.GroupIcon);
        Assert.Equal("EditList", layoutMenu.GroupIcon);
        Assert.Equal("Edit", cmsMenu.Icon);
        Assert.Equal("Grid", layoutMenu.Icon);
    }

    [Fact]
    public void ModuleMenuManifest_GroupAndGroupIcon_DeserializeCorrectly()
    {
        const string json = """
        {
          "id": "cms",
          "name": "Cms",
          "version": "1.0.0",
          "entryAssembly": "Cms.dll",
          "entryType": "Cms.CmsModule",
          "frontend": {
            "menuItems": [
              { "text": "官网管理", "route": "/cms", "icon": "Edit", "group": "内容管理", "groupIcon": "EditList", "sort": 220 },
              { "text": "布局页设计", "route": "/cms/layouts", "icon": "Grid", "group": "内容管理", "groupIcon": "EditList", "sort": 221 }
            ]
          }
        }
        """;

        var manifest = JsonSerializer.Deserialize<ModuleManifest>(json, ManifestJsonOptions)!;
        Assert.All(manifest.Frontend.MenuItems, item =>
        {
            Assert.Equal("内容管理", item.Group);
            Assert.Equal("EditList", item.GroupIcon);
        });
    }
}
