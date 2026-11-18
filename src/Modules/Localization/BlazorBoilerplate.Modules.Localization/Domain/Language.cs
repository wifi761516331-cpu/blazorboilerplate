namespace BlazorBoilerplate.Modules.Localization.Domain;

/// <summary>
/// 系统支持的语言配置实体。
/// 中间回退语言（en-US）受系统保护：不可删除、不可禁用。
/// </summary>
public sealed class Language
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Culture { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 国旗代码（如 US / CN / JP），用于前端 BitFlag 显示。</summary>
    public string? FlagCode { get; set; }

    public bool IsRtl { get; set; }
    public bool IsDefault { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int DisplayOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

