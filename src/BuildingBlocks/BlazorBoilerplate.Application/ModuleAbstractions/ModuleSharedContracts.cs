namespace BlazorBoilerplate.ModuleAbstractions;

public enum ModuleState
{
    Discovered,
    Verifying,
    Staging,
    Installed,
    Loading,
    Running,
    Draining,
    Disabling,
    Disabled,
    Updating,
    Unloading,
    Uninstalled,
    Failed,
    Quarantined
}

/// <summary>模块启动顺序常量（普通模块默认 3000）。</summary>
public static class ModuleStartupDefaults
{
    /// <summary>普通模块默认启动顺序。</summary>
    public const int DefaultOrder = 3000;
}

/// <summary>Host 对外暴露的稳定模块快照，绝不包含模块私有 Type/Assembly。</summary>
public sealed record ModuleDescriptor(
    string Id,
    string Name,
    string Version,
    string Publisher,
    ModuleState State,
    string PackagePath,
    DateTimeOffset? LoadedAtUtc,
    string? Error,
    string[] Permissions,
    int Order = ModuleStartupDefaults.DefaultOrder,
    bool IsEssential = false,
    int VersionCount = 1,
    string? DisplayName = null)
{
    /// <summary>UI 展示名：优先 DisplayName（中文名），为空回退 Name。</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;

    /// <summary>模块声明的页面路由（frontend.routes ∪ menuItems[].route），导航据此判断菜单归属模块。</summary>
    public string[] Routes { get; init; } = [];

    /// <summary>是否允许作为域名路由跳转目标（由 manifest.json frontend.domainRoutable 声明）。</summary>
    public bool DomainRoutable { get; init; }

    /// <summary>作为独立域名入口时的默认路径（由 manifest.json frontend.defaultSiteEntry 声明）。</summary>
    public string? DefaultSiteEntry { get; init; }

    /// <summary>模块展示图标（取自 manifest.json frontend.menuItems[].icon）。</summary>
    public string? Icon { get; init; }
}

/// <summary>模块某版本的快照，供「版本历史」接口与 UI 使用。</summary>
public sealed record ModuleVersionDto(
    string Id,
    string Version,
    ModuleState State,
    bool IsActive,
    DateTimeOffset? LoadedAtUtc,
    string? Error);

public sealed record ModuleAuditEntry(
    DateTimeOffset AtUtc,
    string ModuleId,
    string Version,
    string Operation,
    string Result,
    string? Detail);

