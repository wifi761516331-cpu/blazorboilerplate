using System.Globalization;
using BlazorBoilerplate.Application.OData;
using BlazorBoilerplate.Modules.Catalog.Domain;

namespace BlazorBoilerplate.Modules.Catalog.Application;

/// <summary>稳定 DTO，跨边界序列化用，不暴露 Domain Entity。</summary>
public sealed record ProductDto(Guid Id, string Name, decimal Price, string Category);

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken);
    Task AddAsync(Product product, CancellationToken cancellationToken);
}

public sealed class ProductService(IProductRepository repository)
{
    // 模块只声明自己的字段白名单与取值/转换规则；$filter/$orderby/分页的解析全部由共享 ODataQueryApplier 承担。
    private static readonly ODataFieldMap FieldMap = new(["id", "name", "price", "category"]);
    private static readonly ODataQueryApplier<ProductDto> Applier = new(FieldMap, FieldValue, Coerce);

    public async Task<ODataPage<ProductDto>> QueryAsync(ODataQuery query, CancellationToken cancellationToken)
    {
        var items = (await repository.ListAsync(cancellationToken))
            .Select(p => new ProductDto(p.Id, p.Name, p.Price, p.Category));
        return Applier.Apply(items, query);
    }

    private static object? FieldValue(ProductDto product, string field) => field.ToLowerInvariant() switch
    {
        "id" => product.Id,
        "name" => product.Name,
        "price" => product.Price,
        "category" => product.Category,
        _ => null
    };

    private static object? Coerce(string field, string raw) => field.ToLowerInvariant() switch
    {
        "price" => decimal.Parse(raw, CultureInfo.InvariantCulture),
        "id" => Guid.Parse(raw),
        _ => raw
    };
}
