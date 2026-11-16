using BlazorBoilerplate.Application.OData;

namespace BlazorBoilerplate.Modules.Catalog.OData;

/// <summary>
/// Catalog 模块私有 OData 查询构造器。只产生本模块白名单字段（id/name/price/category）组合的查询，
/// 所有值都经过转义，避免注入。
/// </summary>
public sealed class ODataQueryBuilder
{
    private static readonly HashSet<string> AllowedFields =
        new(StringComparer.OrdinalIgnoreCase) { "id", "name", "price", "category" };

    private readonly List<string> _filters = [];
    private readonly List<string> _orderBys = [];
    private int? _top;
    private int? _skip;
    private bool _count;

    private static string Field(string field)
    {
        if (!AllowedFields.Contains(field))
            throw new InvalidOperationException($"OData 字段 '{field}' 不在白名单中。");
        return field;
    }

    public ODataQueryBuilder Equal(string field, string value)
    {
        _filters.Add($"{Field(field)} eq '{Escape(value)}'");
        return this;
    }

    public ODataQueryBuilder Contains(string field, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return this;
        _filters.Add($"contains({Field(field)},'{Escape(value)}')");
        return this;
    }

    public ODataQueryBuilder GreaterThanOrEqual(string field, decimal value)
    {
        _filters.Add($"{Field(field)} ge {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        return this;
    }

    public ODataQueryBuilder OrderBy(string field, bool descending = false)
    {
        _orderBys.Add(descending ? $"{Field(field)} desc" : Field(field));
        return this;
    }

    public ODataQueryBuilder Top(int top)
    {
        _top = top;
        return this;
    }

    public ODataQueryBuilder Skip(int skip)
    {
        _skip = skip;
        return this;
    }

    public ODataQueryBuilder Count()
    {
        _count = true;
        return this;
    }

    public ODataQuery Build() => new()
    {
        Filter = _filters.Count > 0 ? string.Join(" and ", _filters) : null,
        OrderBy = _orderBys.Count > 0 ? string.Join(",", _orderBys) : null,
        Top = _top,
        Skip = _skip,
        Count = _count
    };

    private static string Escape(string value) => value.Replace("'", "''");
}
