namespace BlazorBoilerplate.Modules.Localization.Models;

public sealed record LanguageDto(
    Guid Id,
    string Culture,
    string Name,
    string DisplayName,
    string? FlagCode,
    bool IsRtl,
    bool IsDefault,
    bool IsEnabled,
    int DisplayOrder,
    bool IsFallbackProtected);

public sealed record CreateLanguageRequest(
    string Culture,
    string Name,
    string DisplayName,
    string? FlagCode,
    bool IsRtl,
    bool IsDefault,
    bool IsEnabled,
    int DisplayOrder);

public sealed record UpdateLanguageRequest(
    string Name,
    string DisplayName,
    string? FlagCode,
    bool IsRtl,
    bool IsDefault,
    bool IsEnabled,
    int DisplayOrder);

public sealed class ResourceRowModel
{
    public string ModuleId { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string? FallbackValue { get; init; }
    public string? TargetValue { get; set; }
    public bool IsOverridden { get; set; }
    public bool IsMissing { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsDirty { get; set; }
}

public sealed record PagedResourcesResult(
    int TotalCount,
    IReadOnlyList<ResourceRowModel> Items);

public sealed record UpsertResourceItem(
    string ModuleId,
    string Key,
    string Value);

public sealed record UpsertResourcesRequest(
    string Culture,
    IReadOnlyList<UpsertResourceItem> Items);

public sealed record ResetResourceRequest(
    string ModuleId,
    string Culture,
    string Key);

public sealed record ImportResourcesRequest(
    string Culture,
    string ModuleId,
    Dictionary<string, string> Resources);

