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
    private static readonly ODataFieldMap FieldMap = new(["id", "name", "price", "category"]);

    /// <summary>实现 OData 标准查询语义的受控子集：$filter(eq/ne/gt/ge/lt/le/contains + and)、$orderby、$top、$skip、$count。</summary>
    public async Task<ODataPage<ProductDto>> QueryAsync(ODataQuery query, CancellationToken cancellationToken)
    {
        IEnumerable<ProductDto> items = (await repository.ListAsync(cancellationToken))
            .Select(p => new ProductDto(p.Id, p.Name, p.Price, p.Category));

        if (!string.IsNullOrWhiteSpace(query.Filter))
        {
            foreach (var clause in query.Filter.Split(" and ", StringSplitOptions.RemoveEmptyEntries))
                items = ApplyFilter(items, clause.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.OrderBy))
        {
            foreach (var part in query.OrderBy.Split(',', StringSplitOptions.TrimEntries).Reverse())
            {
                var (field, descending) = ParseOrderBy(part);
                items = descending
                    ? items.OrderByDescending(p => FieldValue(p, field))
                    : items.OrderBy(p => FieldValue(p, field));
            }
        }

        var totalCount = (long)items.Count();
        var skip = query.Skip ?? 0;
        var top = Math.Min(query.Top ?? ODataLimits.DefaultTop, ODataLimits.MaxTop);
        var pageItems = items.Skip(skip).Take(top).ToList();

        return new ODataPage<ProductDto>
        {
            Value = pageItems,
            Count = query.Count ? totalCount : null
        };
    }

    private static IEnumerable<ProductDto> ApplyFilter(IEnumerable<ProductDto> source, string clause)
    {
        // contains(name,'键') / contains(name,键)
        if (clause.StartsWith("contains(", StringComparison.OrdinalIgnoreCase))
        {
            var inner = clause["contains(".Length..].TrimEnd(')');
            var comma = inner.IndexOf(',');
            var field = Validate(inner[..comma].Trim().Trim('\''));
            var value = inner[(comma + 1)..].Trim().Trim('\'');
            return source.Where(p => (FieldValue(p, field)?.ToString() ?? string.Empty)
                .Contains(value, StringComparison.OrdinalIgnoreCase));
        }

        var operators = new[] { " ge ", " le ", " gt ", " lt ", " eq ", " ne " };
        foreach (var op in operators)
        {
            var index = clause.IndexOf(op, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                continue;
            var field = Validate(clause[..index].Trim());
            var raw = clause[(index + op.Length)..].Trim().Trim('\'');
            return op.Trim().ToLowerInvariant() switch
            {
                "eq" => source.Where(p => Equals(FieldValue(p, field), Coerce(p, field, raw))),
                "ne" => source.Where(p => !Equals(FieldValue(p, field), Coerce(p, field, raw))),
                "gt" => source.Where(p => Compare(p, field, raw) > 0),
                "ge" => source.Where(p => Compare(p, field, raw) >= 0),
                "lt" => source.Where(p => Compare(p, field, raw) < 0),
                "le" => source.Where(p => Compare(p, field, raw) <= 0),
                _ => source
            };
        }

        throw new InvalidOperationException($"Unsupported $filter clause: '{clause}'. Allowed: eq/ne/gt/ge/lt/le/contains joined by 'and'.");
    }

    private static (string Field, bool Descending) ParseOrderBy(string part)
    {
        var segments = part.Split(' ', StringSplitOptions.TrimEntries);
        var field = Validate(segments[0]);
        var descending = segments.Length > 1 && segments[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
        return (field, descending);
    }

    private static string Validate(string field)
    {
        if (!FieldMap.IsAllowed(field))
            throw new InvalidOperationException($"Field '{field}' is not in the OData allow list (id, name, price, category).");
        return FieldMap.Resolve(field);
    }

    private static object? FieldValue(ProductDto product, string field) => field.ToLowerInvariant() switch
    {
        "id" => product.Id,
        "name" => product.Name,
        "price" => product.Price,
        "category" => product.Category,
        _ => null
    };

    private static object? Coerce(ProductDto product, string field, string raw) => field.ToLowerInvariant() switch
    {
        "price" => decimal.Parse(raw, CultureInfo.InvariantCulture),
        "id" => Guid.Parse(raw),
        _ => raw
    };

    private static int Compare(ProductDto product, string field, string raw) =>
        Comparer<object>.Default.Compare(FieldValue(product, field)!, Coerce(product, field, raw)!);
}
