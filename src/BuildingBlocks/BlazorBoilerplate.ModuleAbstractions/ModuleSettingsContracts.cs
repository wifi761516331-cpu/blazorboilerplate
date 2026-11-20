namespace BlazorBoilerplate.ModuleAbstractions;

/// <summary>强类型模块配置标记接口。</summary>
public interface ISettings;

/// <summary>强类型配置读写服务（支持全局默认与租户级覆盖）。</summary>
public interface ISettingService
{
    /// <summary>加载强类型配置（优先读取指定租户覆盖，缺失回退全局默认值）。</summary>
    Task<T> LoadSettingAsync<T>(Guid? tenantId = null, CancellationToken cancellationToken = default)
        where T : class, ISettings, new();

    /// <summary>保存强类型配置（tenantId 为空时保存为全局配置）。</summary>
    Task SaveSettingAsync<T>(T settings, Guid? tenantId = null, CancellationToken cancellationToken = default)
        where T : class, ISettings, new();

    /// <summary>删除指定租户或全局的强类型配置。</summary>
    Task DeleteSettingAsync<T>(Guid? tenantId = null, CancellationToken cancellationToken = default)
        where T : class, ISettings, new();

    /// <summary>查询所有扁平化配置键值。</summary>
    Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(
        string? prefix = null,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default);
}

