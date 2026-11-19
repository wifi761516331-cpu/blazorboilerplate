using System.Globalization;
using System.Text.Json.Serialization;

namespace BlazorBoilerplate.Application.OData;

/// <summary>稳定 OData 查询 DTO。跨模块只传这个契约，不传模块私有类型。</summary>
public sealed record ODataQuery
{
    public string? Filter { get; init; }
    public string? OrderBy { get; init; }
    public string? Select { get; init; }
    public int? Top { get; init; }
    public int? Skip { get; init; }
    public bool Count { get; init; }
    public string? Expand { get; init; }
}

/// <summary>标准 OData JSON 响应形状：value / @odata.count / @odata.nextLink。</summary>
public sealed class ODataPage<T>
{
    [JsonPropertyName("value")]
    public required IReadOnlyList<T> Value { get; init; }

    [JsonPropertyName("@odata.count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Count { get; init; }

    [JsonPropertyName("@odata.nextLink")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextLink { get; init; }
}

/// <summary>服务端字段白名单。UI 字段必须先映射到白名单属性，禁止任意字段拼接。</summary>
public sealed class ODataFieldMap
{
    private readonly HashSet<string> _allowed;

    public ODataFieldMap(IEnumerable<string> allowedFields) =>
        _allowed = new HashSet<string>(allowedFields, StringComparer.OrdinalIgnoreCase);

    public bool IsAllowed(string field) => _allowed.Contains(field);

    public string Resolve(string field) =>
        _allowed.TryGetValue(field, out var canonical)
            ? canonical
            : throw new InvalidOperationException($"OData field '{field}' is not in the allow list.");

    /// <summary>OData 字符串字面量统一转义，所有查询构造路径共用，避免各模块重复实现。</summary>
    public static string EscapeLiteral(string value) => value.Replace("'", "''");
}

/// <summary>
/// 跨模块共享的 OData 查询构造器：字段经 ODataFieldMap 白名单校验，字符串字面量统一转义。
/// 前端页面与后端服务都复用它，不必在每个模块各写一份私有 builder。
/// </summary>
public sealed class ODataQueryBuilder(ODataFieldMap fieldMap)
{
    private readonly List<string> _filters = [];
    private readonly List<string> _orderBys = [];
    private int? _top;
    private int? _skip;
    private bool _count;
    private string? _select;
    private string? _expand;

    private string Field(string field) => fieldMap.Resolve(field);

    public ODataQueryBuilder Equal(string field, string value)
    {
        _filters.Add($"{Field(field)} eq '{ODataFieldMap.EscapeLiteral(value)}'");
        return this;
    }

    public ODataQueryBuilder NotEqual(string field, string value)
    {
        _filters.Add($"{Field(field)} ne '{ODataFieldMap.EscapeLiteral(value)}'");
        return this;
    }

    public ODataQueryBuilder Contains(string field, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _filters.Add($"contains({Field(field)},'{ODataFieldMap.EscapeLiteral(value)}')");
        return this;
    }

    public ODataQueryBuilder Compare(string field, string op, decimal value)
    {
        var normalized = op.Trim().ToLowerInvariant() switch
        {
            "gt" or ">" => "gt",
            "ge" or ">=" => "ge",
            "lt" or "<" => "lt",
            "le" or "<=" => "le",
            _ => throw new InvalidOperationException($"不支持的数值操作符 '{op}'。")
        };
        _filters.Add($"{Field(field)} {normalized} {value.ToString(CultureInfo.InvariantCulture)}");
        return this;
    }

    public ODataQueryBuilder OrderBy(string field, bool descending = false)
    {
        var resolved = Field(field);
        _orderBys.Add(descending ? $"{resolved} desc" : resolved);
        return this;
    }

    public ODataQueryBuilder Top(int top)
    {
        _top = Math.Clamp(top, 0, ODataLimits.MaxTop);
        return this;
    }

    public ODataQueryBuilder Skip(int skip)
    {
        _skip = Math.Max(0, skip);
        return this;
    }

    public ODataQueryBuilder Count()
    {
        _count = true;
        return this;
    }

    public ODataQueryBuilder Select(params string[] fields)
    {
        if (fields.Length > 0) _select = string.Join(",", fields.Select(Field));
        return this;
    }

    public ODataQueryBuilder Expand(params string[] fields)
    {
        if (fields.Length > 0) _expand = string.Join(",", fields.Select(Field));
        return this;
    }

    public ODataQuery Build() => new()
    {
        Filter = _filters.Count > 0 ? string.Join(" and ", _filters) : null,
        OrderBy = _orderBys.Count > 0 ? string.Join(",", _orderBys) : null,
        Select = _select,
        Top = _top,
        Skip = _skip,
        Count = _count,
        Expand = _expand
    };
}

/// <summary>
/// 跨模块共享的 OData 内存查询执行器：统一 $filter/$orderby/$top/$skip/$count 语义。
/// 模块只提供字段白名单、字段取值器与可选字面量转换器，不再各自复制解析逻辑。
/// </summary>
public sealed class ODataQueryApplier<T>(
    ODataFieldMap fieldMap,
    Func<T, string, object?> fieldAccessor,
    Func<string, string, object?>? literalCoercer = null)
{
    private static readonly string[] ComparisonOperators = [" ge ", " le ", " gt ", " lt ", " eq ", " ne "];

    public ODataPage<T> Apply(IEnumerable<T> source, ODataQuery query)
    {
        var coerce = literalCoercer ?? ((_, raw) => raw);
        IEnumerable<T> items = source;

        if (!string.IsNullOrWhiteSpace(query.Filter))
            foreach (var clause in query.Filter.Split(" and ", StringSplitOptions.RemoveEmptyEntries))
                items = ApplyFilter(items, clause.Trim(), coerce);

        if (!string.IsNullOrWhiteSpace(query.OrderBy))
            foreach (var part in query.OrderBy.Split(',', StringSplitOptions.TrimEntries).Reverse())
            {
                var (field, descending) = ParseOrderBy(part);
                items = descending
                    ? items.OrderByDescending(x => fieldAccessor(x, field))
                    : items.OrderBy(x => fieldAccessor(x, field));
            }

        var totalCount = (long)items.Count();
        var skip = query.Skip ?? 0;
        var top = Math.Min(query.Top ?? ODataLimits.DefaultTop, ODataLimits.MaxTop);
        return new ODataPage<T>
        {
            Value = items.Skip(skip).Take(top).ToList(),
            Count = query.Count ? totalCount : null
        };
    }

    private IEnumerable<T> ApplyFilter(
        IEnumerable<T> source, string clause, Func<string, string, object?> coerce)
    {
        if (clause.StartsWith("contains(", StringComparison.OrdinalIgnoreCase))
        {
            var inner = clause["contains(".Length..].TrimEnd(')');
            var comma = inner.IndexOf(',');
            if (comma < 0)
                throw new InvalidOperationException($"无效的 contains 子句: '{clause}'。");
            var field = fieldMap.Resolve(inner[..comma].Trim().Trim('\''));
            var value = inner[(comma + 1)..].Trim().Trim('\'');
            return source.Where(x =>
                (fieldAccessor(x, field)?.ToString() ?? string.Empty)
                .Contains(value, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var op in ComparisonOperators)
        {
            var index = clause.IndexOf(op, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var field = fieldMap.Resolve(clause[..index].Trim());
            var raw = clause[(index + op.Length)..].Trim().Trim('\'');
            return op.Trim().ToLowerInvariant() switch
            {
                "eq" => source.Where(x => Equals(fieldAccessor(x, field), coerce(field, raw))),
                "ne" => source.Where(x => !Equals(fieldAccessor(x, field), coerce(field, raw))),
                "gt" => source.Where(x => Compare(x, field, raw, coerce) > 0),
                "ge" => source.Where(x => Compare(x, field, raw, coerce) >= 0),
                "lt" => source.Where(x => Compare(x, field, raw, coerce) < 0),
                "le" => source.Where(x => Compare(x, field, raw, coerce) <= 0),
                _ => source
            };
        }

        throw new InvalidOperationException(
            $"不支持的 $filter 子句: '{clause}'。允许 eq/ne/gt/ge/lt/le/contains，用 and 连接。");
    }

    private (string Field, bool Descending) ParseOrderBy(string part)
    {
        var segments = part.Split(' ', StringSplitOptions.TrimEntries);
        return (fieldMap.Resolve(segments[0]),
            segments.Length > 1 && segments[1].Equals("desc", StringComparison.OrdinalIgnoreCase));
    }

    private int Compare(T item, string field, string raw, Func<string, string, object?> coerce) =>
        Comparer<object>.Default.Compare(fieldAccessor(item, field)!, coerce(field, raw)!);
}

public static class ODataLimits
{
    public const int MaxTop = 200;
    public const int DefaultTop = 20;
    public const int MaxFilterLength = 512;
    public const int MaxOrderByParts = 3;
}
