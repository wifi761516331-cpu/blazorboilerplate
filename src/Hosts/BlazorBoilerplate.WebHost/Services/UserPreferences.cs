using System.Text.Json.Serialization;
using BlazorBoilerplate.Shared.Features.Account;
using BlazorBoilerplate.Shared.Features.Identity;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.JSInterop;

namespace BlazorBoilerplate.WebHost.Services;

/// <summary>
/// 用户设备偏好（scoped）：localStorage 优先，服务端 UserPreference 作为跨设备兜底，
/// 再兜底默认值。写操作先落地本地并即时 apply，再「尽力」同步服务端；
/// 服务端不可用时只保留本地、不抛致命错误（原因记在 <see cref="LastServerError"/>）。
/// </summary>
public sealed class UserPreferences(IJSRuntime js, BlazorBoilerplateApiClient api)
{
    private const string DeviceInfo = "browser";

    /// <summary>兜底默认：zh-CN / Asia/Shanghai / light / 无强调色 / 关闭通知。</summary>
    private static readonly UserPreferenceDto Defaults = new();

    private UserPreferenceDto? _cached;

    /// <summary>最近一次服务端同步失败的原因（本地仍生效）；null 表示成功或尚未同步。</summary>
    public string? LastServerError { get; private set; }

    /// <summary>读取偏好：blazorPrefs.get() → Api.GetPreferencesAsync() → 默认值。</summary>
    public async Task<UserPreferenceDto> GetAsync()
    {
        if (_cached is not null) return _cached;

        UserPreferenceDto? value = null;
        try
        {
            var payload = await js.InvokeAsync<PrefsPayload?>("blazorPrefs.get");
            if (payload is not null && HasAny(payload))
                value = Merge(Defaults, payload);
        }
        catch
        {
            // 预渲染阶段 JS interop 不可用 / 脚本未就绪：继续走服务端兜底。
        }

        if (value is null)
        {
            try
            {
                value = await api.GetPreferencesAsync();
            }
            catch
            {
                // 服务端不可用：使用默认值。
            }
        }

        _cached = value ?? Defaults;
        return _cached;
    }

    /// <summary>
    /// 写入完整偏好值：localStorage（blazorPrefs.set）+ 即时 apply +
    /// 服务端 SavePreferences / UpdateSession（失败只保留本地）。
    /// </summary>
    public async Task SetAsync(UserPreferenceDto value)
    {
        _cached = value;
        LastServerError = null;

        // 1) 本地：持久化 + 即时改 DOM / culture cookie。
        try
        {
            await js.InvokeVoidAsync("blazorPrefs.set", new
            {
                culture = value.Culture,
                timeZone = value.TimeZone,
                theme = value.Theme,
                accentColor = value.AccentColor,
                notificationsEnabled = value.NotificationsEnabled
            });
        }
        catch
        {
            // 预渲染 / 脚本未就绪：本地写入失败不阻断后续链路。
        }
        await ApplyAsync(value.Theme, value.AccentColor, value.Culture);

        // 2) 服务端：跨设备保存 + 会话字段同步（尽力而为）。
        try
        {
            await api.SavePreferencesAsync(value);
            await api.UpdateSessionAsync(new UpdateUserSessionRequestDto(
                value.Culture, value.TimeZone, value.NotificationsEnabled, DeviceInfo));
        }
        catch (Exception ex)
        {
            LastServerError = ex.Message;
        }
    }

    /// <summary>即时把主题 / 强调色 / 语言应用到 DOM 与 culture cookie。</summary>
    public async Task ApplyAsync(string theme, string? accentColor, string culture)
    {
        try
        {
            await js.InvokeVoidAsync("blazorPrefs.apply", theme, accentColor, culture);
        }
        catch
        {
            // 预渲染阶段 JS interop 不可用，首屏由 App.razor 的 cookie 渲染。
        }
    }

    private static bool HasAny(PrefsPayload p) =>
        p.Culture is not null || p.TimeZone is not null || p.Theme is not null ||
        p.AccentColor is not null || p.NotificationsEnabled is not null;

    private static UserPreferenceDto Merge(UserPreferenceDto fallback, PrefsPayload p) => new(
        Culture: p.Culture ?? fallback.Culture,
        TimeZone: p.TimeZone ?? fallback.TimeZone,
        Theme: p.Theme ?? fallback.Theme,
        AccentColor: p.AccentColor ?? fallback.AccentColor,
        NotificationsEnabled: p.NotificationsEnabled ?? fallback.NotificationsEnabled);

    /// <summary>blazorPrefs.get() 的 JSON 形状（camelCase），字段可缺省。</summary>
    private sealed class PrefsPayload
    {
        [JsonPropertyName("culture")] public string? Culture { get; set; }
        [JsonPropertyName("timeZone")] public string? TimeZone { get; set; }
        [JsonPropertyName("theme")] public string? Theme { get; set; }
        [JsonPropertyName("accentColor")] public string? AccentColor { get; set; }
        [JsonPropertyName("notificationsEnabled")] public bool? NotificationsEnabled { get; set; }
    }
}
