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
}

public static class ODataLimits
{
    public const int MaxTop = 200;
    public const int DefaultTop = 20;
    public const int MaxFilterLength = 512;
    public const int MaxOrderByParts = 3;
}
