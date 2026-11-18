namespace BlazorBoilerplate.Modules.Localization.Application;

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

public sealed record ResourceItemDto(
    string ModuleId,
    string Key,
    string? FallbackValue,
    string? TargetValue,
    bool IsOverridden,
    bool IsMissing,
    DateTimeOffset? UpdatedAt);

public sealed record PagedResourcesResult(
    int TotalCount,
    IReadOnlyList<ResourceItemDto> Items);

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

