using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BlazorBoilerplate.Application.DataExchange;

namespace BlazorBoilerplate.Infrastructure.DataExchange;

/// <summary>通用 CSV / JSON 数据导入导出实现（含 UTF-8 BOM、引号转义与逐行错误收集）。</summary>
public sealed class PlatformDataExchangeService : IDataExchangeService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public byte[] ExportCsv<T>(IEnumerable<T> items, IReadOnlyList<DataColumnMap>? columns = null)
    {
        var props = ResolveColumns<T>(columns);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", props.Select(p => EscapeCsv(p.Header))));

        foreach (var item in items)
        {
            var cells = props.Select(p =>
            {
                var raw = p.Property?.GetValue(item);
                var str = raw switch
                {
                    null => string.Empty,
                    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                    _ => raw.ToString() ?? string.Empty
                };
                return EscapeCsv(str);
            });
            sb.AppendLine(string.Join(",", cells));
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }

    public DataImportResult<T> ImportCsv<T>(Stream stream, IReadOnlyList<DataColumnMap>? columns = null) where T : new()
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
            return new DataImportResult<T>(0, 0, [], [new DataRowError(0, "CSV 表头为空。")]);

        var headers = ParseCsvLine(headerLine);
        var propMap = BuildImportColumnMap<T>(headers, columns);

        var items = new List<T>();
        var errors = new List<DataRowError>();
        var rowNum = 1;

        while (reader.ReadLine() is { } line)
        {
            rowNum++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var fields = ParseCsvLine(line);
                var entity = new T();
                for (var i = 0; i < headers.Count && i < fields.Count; i++)
                {
                    if (!propMap.TryGetValue(i, out var prop) || !prop.CanWrite)
                        continue;
                    var converted = ConvertValue(fields[i], prop.PropertyType);
                    prop.SetValue(entity, converted);
                }
                items.Add(entity);
            }
            catch (Exception ex)
            {
                errors.Add(new DataRowError(rowNum, ex.GetBaseException().Message));
            }
        }

        return new DataImportResult<T>(items.Count + errors.Count, items.Count, items, errors);
    }

    public byte[] ExportJson<T>(IEnumerable<T> items) =>
        JsonSerializer.SerializeToUtf8Bytes(items, JsonOptions);

    public DataImportResult<T> ImportJson<T>(Stream stream)
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<T>>(stream, JsonOptions) ?? [];
            return new DataImportResult<T>(list.Count, list.Count, list, []);
        }
        catch (Exception ex)
        {
            return new DataImportResult<T>(0, 0, [], [new DataRowError(0, ex.GetBaseException().Message)]);
        }
    }

    private static List<(string Header, PropertyInfo? Property)> ResolveColumns<T>(IReadOnlyList<DataColumnMap>? columns)
    {
        var allProps = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead)
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        if (columns is { Count: > 0 })
        {
            return columns
                .Select(c => (c.HeaderName, allProps.GetValueOrDefault(c.PropertyName)))
                .ToList();
        }

        return allProps.Values
            .Select(p => (p.Name, (PropertyInfo?)p))
            .ToList();
    }

    private static Dictionary<int, PropertyInfo> BuildImportColumnMap<T>(List<string> headers, IReadOnlyList<DataColumnMap>? columns)
    {
        var allProps = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        var headerToProp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (columns is not null)
        {
            foreach (var c in columns)
                headerToProp[c.HeaderName] = c.PropertyName;
        }

        var map = new Dictionary<int, PropertyInfo>();
        for (var i = 0; i < headers.Count; i++)
        {
            var h = headers[i].Trim();
            var propName = headerToProp.TryGetValue(h, out var mapped) ? mapped : h;
            if (allProps.TryGetValue(propName, out var prop))
                map[i] = prop;
        }
        return map;
    }

    private static object? ConvertValue(string raw, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType);
        if (underlying is not null && string.IsNullOrWhiteSpace(raw))
            return null;

        var type = underlying ?? targetType;
        if (type == typeof(string)) return raw;
        if (type == typeof(Guid)) return Guid.Parse(raw);
        if (type == typeof(bool)) return bool.Parse(raw);
        if (type.IsEnum) return Enum.Parse(type, raw, ignoreCase: true);
        if (type == typeof(DateTimeOffset)) return DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture);
        if (type == typeof(DateTime)) return DateTime.Parse(raw, CultureInfo.InvariantCulture);
        return Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        result.Add(current.ToString());
        return result;
    }
}

