using System.Text.Json.Serialization;

namespace BlazorBoilerplate.Modules.Catalog.OData;

/// <summary>模块私有投影，与 Catalog 后端稳定 DTO 对齐（camelCase）。</summary>
public sealed record ProductDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("category")] string Category);
