using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Catalog.Widgets;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Catalog;

/// <summary>Catalog 模块前端启动器：向首页插槽注册商品统计卡与分类分布图表微件。</summary>
public sealed class CatalogClientStartup : IModuleClientStartup
{
    public int Order => 200;

    public void ConfigureServices(IServiceCollection services)
    {
        WidgetZoneRegistry.Register("home_stats", typeof(CatalogStatWidget), "catalog", order: 200, requiredPermission: "catalog.product.read");
        WidgetZoneRegistry.Register("home_widgets", typeof(CatalogCategoryChartWidget), "catalog", order: 100, requiredPermission: "catalog.product.read");
        WidgetZoneRegistry.Register("home_widgets", typeof(CatalogCategoryShareWidget), "catalog", order: 200, requiredPermission: "catalog.product.read");
    }
}
