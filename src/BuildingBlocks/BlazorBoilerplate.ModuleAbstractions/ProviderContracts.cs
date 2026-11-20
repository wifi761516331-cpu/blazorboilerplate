namespace BlazorBoilerplate.ModuleAbstractions;

/// <summary>可插拔策略提供者标记接口（一个模块可包含多个提供者实现）。</summary>
public interface IProvider;

/// <summary>声明提供者系统唯一标识。</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SystemNameAttribute(string systemName) : Attribute
{
    public string SystemName { get; } = systemName;
}

/// <summary>声明提供者友好显示名称。</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class FriendlyNameAttribute(string friendlyName) : Attribute
{
    public string FriendlyName { get; } = friendlyName;
}

/// <summary>声明提供者排序权重（默认 3000）。</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ProviderOrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}

/// <summary>声明提供者图标名称。</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ProviderIconAttribute(string icon) : Attribute
{
    public string Icon { get; } = icon;
}

/// <summary>标记提供者在管理列表中隐藏。</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HiddenProviderAttribute : Attribute;

/// <summary>提供者元数据快照（枚举列表时无需实例化提供者）。</summary>
public sealed record ProviderMetadata(
    string SystemName,
    string FriendlyName,
    string ModuleId,
    Type ProviderType,
    int Order = ModuleStartupDefaults.DefaultOrder,
    string? Icon = null,
    bool IsHidden = false);

/// <summary>延迟激活的提供者包装器。</summary>
public sealed class Provider<TProvider>(ProviderMetadata metadata, Lazy<TProvider> lazyValue)
    where TProvider : IProvider
{
    public ProviderMetadata Metadata { get; } = metadata;
    public TProvider Value => lazyValue.Value;
    public bool IsValueCreated => lazyValue.IsValueCreated;
}

/// <summary>策略提供者管理器（支持延迟激活与按租户启停）。</summary>
public interface IProviderManager
{
    /// <summary>按系统名获取指定类型的提供者。</summary>
    Provider<TProvider>? GetProvider<TProvider>(string? systemName, Guid? tenantId = null)
        where TProvider : class, IProvider;

    /// <summary>枚举指定类型的全部已激活提供者（未调用 Value 前不实例化）。</summary>
    IReadOnlyList<Provider<TProvider>> GetAllProviders<TProvider>(Guid? tenantId = null, bool includeDisabled = false)
        where TProvider : class, IProvider;

    /// <summary>列出所有已注册提供者的元数据。</summary>
    IReadOnlyList<ProviderMetadata> GetAllMetadata(bool includeHidden = false);

    /// <summary>检查提供者在指定租户下是否启用。</summary>
    bool IsEnabled(string systemName, Guid? tenantId = null);

    /// <summary>设置提供者在指定租户下的启用状态。</summary>
    Task SetEnabledAsync(string systemName, bool enabled, Guid? tenantId = null, CancellationToken cancellationToken = default);
}

