namespace BlazorBoilerplate.Modules.Localization.Domain;

/// <summary>
/// 持久化多语言词条实体。
/// 通过 <see cref="IsOverridden"/> 区分系统同步默认词条与管理员人工覆盖词条，
/// 避免模块升级或重装载时覆盖已定制的翻译。
/// </summary>
public sealed class LocaleResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ModuleId { get; set; } = string.Empty;
    public string Culture { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsOverridden { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

