using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using BlazorBoilerplate.Application.OData;

namespace BlazorBoilerplate.Web.Shared.OData;

/// <summary>
/// 服务端 OData 查询的一页结果。与 UI 框架无关，供 BitDataGrid 等服务端分页控件适配。
/// </summary>
public sealed record ODataServerPage<T>(IReadOnlyList<T> Items, long TotalCount);

/// <summary>
/// 描述一次服务端分页请求。字段刻意对齐 BitDataGridReadRequest 的语义（skip/take/sorts/search），
/// 但不引用 Extras 类型，让封装层与 UI 框架解耦；页面回调里做一次简单映射即可。
/// </summary>
public sealed record ODataServerRequest(
    int Skip,
    int? Take,
    IReadOnlyList<ODataSort> Sorts,
    IReadOnlyList<ODataFilter> Filters,
    string? Search);

public sealed record ODataSort(string Field, bool Descending);

public sealed record ODataFilter(string Field, string Operator, string Value);

/// <summary>
/// 可复用的服务端 OData 数据源：
/// 把分页/排序/筛选/搜索请求转成 ODataQuery（走字段白名单），通过 HttpClient 拉取一页并拆出总数。
/// 典型用法：BitDataGrid 的 OnLoadMore 回调里调用 <see cref="LoadAsync"/>，
/// 其中 Skip/Take/Sorts/Filters/Search 从 BitDataGridReadRequest 映射过来。
/// </summary>
public sealed class ODataServerSource<TItem>(
    HttpClient http,
    string endpoint,
    ODataFieldMap fieldMap,
    JsonSerializerOptions? jsonOptions = null)
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<ODataServerPage<TItem>> LoadAsync(
        ODataServerRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = BuildQuery(request);
        var response = await http.GetAsync(endpoint + BuildQueryString(query), cancellationToken);
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<ODataPage<TItem>>(
            jsonOptions ?? DefaultJsonOptions, cancellationToken);

        return new ODataServerPage<TItem>(page?.Value ?? [], page?.Count ?? 0);
    }

    /// <summary>把 UI 层的排序/筛选描述符转成 ODataQuery，所有字段名都经白名单校验。</summary>
    public ODataQuery BuildQuery(ODataServerRequest request)
    {
        var orderBy = request.Sorts
            .OrderBy(s => s.Field, StringComparer.OrdinalIgnoreCase)
            .Select(s => fieldMap.Resolve(s.Field) + (s.Descending ? " desc" : ""))
            .ToList();
        if (orderBy.Count == 0 && !string.IsNullOrWhiteSpace(request.Search))
            orderBy.Add(fieldMap.Resolve(request.Search));

        var filters = request.Filters
            .Where(f => !string.IsNullOrWhiteSpace(f.Value))
            .Select(FormatFilter)
            .ToList();

        return new ODataQuery
        {
            Filter = filters.Count > 0 ? string.Join(" and ", filters) : null,
            OrderBy = orderBy.Count > 0 ? string.Join(",", orderBy) : null,
            Top = request.Take,
            Skip = request.Skip,
            Count = true
        };
    }

    public static string BuildQueryString(ODataQuery query)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.Filter)) parts.Add("$filter=" + Uri.EscapeDataString(query.Filter));
        if (!string.IsNullOrWhiteSpace(query.OrderBy)) parts.Add("$orderby=" + Uri.EscapeDataString(query.OrderBy));
        if (query.Top is { } top) parts.Add("$top=" + top.ToString(CultureInfo.InvariantCulture));
        if (query.Skip is { } skip) parts.Add("$skip=" + skip.ToString(CultureInfo.InvariantCulture));
        if (query.Count) parts.Add("$count=true");
        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private string FormatFilter(ODataFilter filter)
    {
        var field = fieldMap.Resolve(filter.Field);
        var op = NormalizeOperator(filter.Operator);
        var value = ODataFieldMap.EscapeLiteral(filter.Value);
        return op == "contains"
            ? $"contains({field},'{value}')"
            : $"{field} {op} {value}";
    }

    private static string NormalizeOperator(string op) => op.Trim().ToLowerInvariant() switch
    {
        "eq" or "=" => "eq",
        "ne" or "!=" => "ne",
        "gt" or ">" => "gt",
        "ge" or ">=" => "ge",
        "lt" or "<" => "lt",
        "le" or "<=" => "le",
        "contains" => "contains",
        _ => throw new InvalidOperationException($"不支持的 OData 操作符 '{op}'。")
    };
}
