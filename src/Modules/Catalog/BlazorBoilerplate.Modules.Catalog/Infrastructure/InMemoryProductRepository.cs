using BlazorBoilerplate.Modules.Catalog.Application;
using BlazorBoilerplate.Modules.Catalog.Domain;

namespace BlazorBoilerplate.Modules.Catalog.Infrastructure;

/// <summary>第一批内存仓储（构造期播种演示数据）；PostgreSQL + 独立 Schema/DbContext/迁移在批次 2 接入（见 ROADMAP）。</summary>
public sealed class InMemoryProductRepository : IProductRepository
{
    private readonly List<Product> _products =
    [
        new("机械键盘", 399m, "外设"),
        new("无线鼠标", 199m, "外设"),
        new("27寸显示器", 1899m, "显示器"),
        new("USB-C 扩展坞", 459m, "外设"),
        new("降噪耳机", 1299m, "音频"),
        new("桌面音箱", 699m, "音频"),
        new("人体工学椅", 2599m, "家具"),
        new("升降桌", 1899m, "家具"),
        new("网络摄像头", 599m, "外设"),
        new("麦克风", 899m, "音频")
    ];

    private readonly object _gate = new();

    public Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<Product>>(_products.ToArray());
    }

    public Task AddAsync(Product product, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            _products.Add(product);
        return Task.CompletedTask;
    }
}
