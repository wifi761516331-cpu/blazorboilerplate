using BlazorBoilerplate.Application.OData;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Modules.Catalog.OData;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using BlazorBoilerplate.Web.Shared.OData;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components;
using Bit.BlazorUI;

namespace BlazorBoilerplate.Modules.Catalog.Pages;

public partial class Catalog
{
    [Inject]
    private HttpClient Http { get; set; } = default!;

    [Inject]
    private ILogger<Catalog> Logger { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    [Inject]
    private FrontendLocalizationService FrontendLoc { get; set; } = default!;

    private static readonly ODataFieldMap FieldMap = new(["id", "name", "price", "category"]);
    private const int PageSize = 10;

    private ODataServerSource<ProductDto> _source = default!;
    private BitDataGridStrings _gridStrings = new();
    private string? _loadError;

    protected override async Task OnInitializedAsync()
    {
        await FrontendLoc.EnsureLoadedAsync();
        _source = new ODataServerSource<ProductDto>(Http, "api/catalog/products", FieldMap);
        _gridStrings = new BitDataGridStrings
        {
            FilterPlaceholder = L["catalog.grid.filterPlaceholder"],
            FilterByFormat = L["catalog.grid.filterBy"],
            FilterOperatorLabel = L["catalog.grid.filterOperator"],
            ClearFiltersText = L["catalog.grid.clearFilters"],
            FilterAllText = L["catalog.grid.all"],
            FilterOpContains = L["catalog.grid.op.contains"],
            FilterOpDoesNotContain = L["catalog.grid.op.doesNotContain"],
            FilterOpStartsWith = L["catalog.grid.op.startsWith"],
            FilterOpEndsWith = L["catalog.grid.op.endsWith"],
            FilterOpEquals = L["catalog.grid.op.equals"],
            FilterOpNotEquals = L["catalog.grid.op.notEquals"],
            FilterOpGreaterThan = L["catalog.grid.op.greaterThan"],
            FilterOpGreaterThanOrEqual = L["catalog.grid.op.greaterThanOrEqual"],
            FilterOpLessThan = L["catalog.grid.op.lessThan"],
            FilterOpLessThanOrEqual = L["catalog.grid.op.lessThanOrEqual"],
            FilterOpIsEmpty = L["catalog.grid.op.isEmpty"],
            FilterOpIsNotEmpty = L["catalog.grid.op.isNotEmpty"],
            FilterOpIn = L["catalog.grid.op.in"],
            FilterOpNotIn = L["catalog.grid.op.notIn"],
            FilterListPlaceholder = L["catalog.grid.listPlaceholder"],
            NoMatchesText = L["catalog.grid.noMatches"],
            // 内置分页文案（Bit fork QuickGrid 默认硬编码英文）：词条在平台内置词典 platform.grid.*
            PagerRangeFormat = L.GetString("platform.grid.pagerRange"),
            PagerPageFormat = L.GetString("platform.grid.pagerPage"),
            PerPageFormat = L.GetString("platform.grid.perPage"),
            RowsPerPageLabel = L.GetString("platform.grid.rowsPerPage")
        };
    }

    /// <summary>
    /// BitDataGrid 服务端模式：把它的 ReadRequest（skip/take/sorts/filters）映射到 OData 服务器数据源，
    /// 翻页/排序/筛选都在服务端完成，不退化成当前页内存分页。字段白名单与转义由 ODataServerSource 统一处理。
    /// </summary>
    private async Task<BitDataGridReadResult<ProductDto>> ReadAsync(BitDataGridReadRequest req)
    {
        // 表头排序 → OData $orderby；列名即 ColumnId（OData 白名单字段名）。
        var sorts = req.Sorts.Select(s => new ODataSort(s.ColumnId, s.Direction == BitDataGridSortDirection.Descending)).ToList();

        // 表头筛选行的输入 → OData $filter；BitDataGrid 操作符映射到 OData 操作符。
        var filters = req.Filters
            .Select(ToODataFilter)
            .Where(f => f is not null)
            .Select(f => f!)
            .ToList();

        var request = new ODataServerRequest(req.Skip, req.Take, sorts, filters, null);
        try
        {
            var page = await _source.LoadAsync(request);
            return new BitDataGridReadResult<ProductDto>(page.Items, (int)page.TotalCount);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "OData 查询失败");
            _loadError = L.GetString("catalog.error.queryFailed", ex.Message);
            Toast.Error(_loadError);
            return new BitDataGridReadResult<ProductDto>([], 0);
        }
    }

    /// <summary>把 BitDataGrid 的列筛选描述符转成 OData 筛选；文本列退化成 contains，价格列按所选比较符。</summary>
    private static ODataFilter? ToODataFilter(BitDataGridFilterDescriptor filter)
    {
        if (filter.Value is null) return null;

        var op = filter.Operator switch
        {
            BitDataGridFilterOperator.Contains
                or BitDataGridFilterOperator.StartsWith
                or BitDataGridFilterOperator.EndsWith
                or BitDataGridFilterOperator.DoesNotContain => "contains",
            BitDataGridFilterOperator.Equals => "eq",
            BitDataGridFilterOperator.NotEquals => "ne",
            BitDataGridFilterOperator.GreaterThan => "gt",
            BitDataGridFilterOperator.GreaterThanOrEqual => "ge",
            BitDataGridFilterOperator.LessThan => "lt",
            BitDataGridFilterOperator.LessThanOrEqual => "le",
            _ => null
        };
        if (op is null) return null;

        var value = Convert.ToString(filter.Value, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(value) ? null : new ODataFilter(filter.ColumnId, op, value);
    }
}
