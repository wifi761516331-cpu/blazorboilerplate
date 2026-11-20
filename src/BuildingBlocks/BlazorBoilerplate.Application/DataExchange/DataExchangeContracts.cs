namespace BlazorBoilerplate.Application.DataExchange;

/// <summary>数据导入导出列映射。</summary>
public sealed record DataColumnMap(string PropertyName, string HeaderName);

/// <summary>单行导入错误记录。</summary>
public sealed record DataRowError(int RowNumber, string Message);

/// <summary>批量数据导入结果。</summary>
public sealed record DataImportResult<T>(
    int TotalRows,
    int SuccessCount,
    IReadOnlyList<T> Items,
    IReadOnlyList<DataRowError> Errors);

/// <summary>通用 CSV / JSON 数据导入导出服务。</summary>
public interface IDataExchangeService
{
    /// <summary>导出数据为 UTF-8（含 BOM）CSV 字节流。</summary>
    byte[] ExportCsv<T>(IEnumerable<T> items, IReadOnlyList<DataColumnMap>? columns = null);

    /// <summary>从 CSV 流导入并映射为强类型集合（逐行收集错误，不中断整批）。</summary>
    DataImportResult<T> ImportCsv<T>(Stream stream, IReadOnlyList<DataColumnMap>? columns = null) where T : new();

    /// <summary>导出数据为格式化 JSON 字节流。</summary>
    byte[] ExportJson<T>(IEnumerable<T> items);

    /// <summary>从 JSON 流导入强类型集合。</summary>
    DataImportResult<T> ImportJson<T>(Stream stream);
}

