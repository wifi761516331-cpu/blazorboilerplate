using BlazorBoilerplate.Shared.Features.SystemSettings;
using BlazorBoilerplate.Web.Shared.Services;

namespace BlazorBoilerplate.WebHost.Services;

/// <summary>
/// 公开系统设置状态（随 Blazor 线路缓存），供 MainLayout 渲染品牌名、页脚与系统公告。
/// </summary>
public sealed class SystemSettingsState(BlazorBoilerplateApiClient api)
{
    public PublicSystemSettingsDto Public { get; private set; } = new(
        "BlazorBoilerplate 平台",
        ".NET 10 · Bit.BlazorUI · 动态模块运行时",
        string.Empty,
        true,
        "zh-CN",
        "Asia/Shanghai",
        11,
        6);

    private bool _loaded;
    public event Action? Changed;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            Public = await api.GetPublicSystemSettingsAsync();
            _loaded = true;
            Changed?.Invoke();
        }
        catch
        {
            // ApiHost 暂不可达时保留默认展示值
        }
    }

    public void ApplyUpdated(SystemSettingsDto dto)
    {
        Public = new PublicSystemSettingsDto(
            dto.SystemName,
            dto.FooterText,
            dto.Notice,
            dto.RegisterEnabled,
            dto.DefaultCulture,
            dto.DefaultTimeZone,
            dto.MaxUploadSizeMb,
            dto.PasswordMinLength,
            dto.AllowedExtensions,
            dto.StorageProvider);
        _loaded = true;
        Changed?.Invoke();
    }
}

