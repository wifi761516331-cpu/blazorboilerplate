using System.Text.Json.Serialization;
using BlazorBoilerplate.Shared.Features.Account;
using BlazorBoilerplate.Shared.Features.Identity;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.JSInterop;

namespace BlazorBoilerplate.WebHost.Services;

/// <summary>
/// 用户设备偏好（scoped）：localStorage 优先，服务端 UserPreference 作为跨设备兜底，
/// 再兜底默认值。写操作先落地本地并即时 apply，再「尽力」同步服务端；
/// 服务端不可用时只保留本地、不抛致命错误（原因记在 <see cref="LastServerError"/>）。
/// </summary>
public sealed class UserPreferences(
    IJSRuntime js,
    BlazorBoilerplateApiClient api,
    SystemSettingsState sysSettings,
    FrontendLocalizationService localization)
{
    private const string DeviceInfo = "browser";

    /// <summary>兜底默认：zh-CN / Asia/Shanghai / light / 无强调色 / 关闭通知。</summary>
    private static readonly UserPreferenceDto Defaults = new();

    private UserPreferenceDto? _cached;

    /// <summary>偏好变更通知（供顶栏 AccountMenu 等组件实时同步）。</summary>
    public event Action? Changed;

    /// <summary>最近一次服务端同步失败的原因（本地仍生效）；null 表示成功或尚未同步。</summary>
    public string? LastServerError { get; private set; }

    /// <summary>
    /// 读取偏好：blazorPrefs.get() → Api.GetPreferencesAsync() → 系统默认设置。
    /// 首次访问（本机无 culture 偏好）时以浏览器 culture 为准，见方法内注释。
    /// </summary>
    public async Task<UserPreferenceDto> GetAsync()
    {
        if (_cached is not null) return _cached;

        await sysSettings.EnsureLoadedAsync();

        // 当前 culture 由 WebHost 中间件按浏览器 Accept-Language 解析并固化到 bb_culture cookie；
        // SSR 首屏与 Blazor 线路的 CultureInfo 都据此设置，FrontendLoc.CurrentCulture 即为浏览器语言。
        // 首次访问若回落系统默认语言，MainLayout 会用默认值 SetCultureAsync，
        // 把首屏已按浏览器渲染好的语言又拉回默认（用户看到语言跳变）。
        var browserCulture = localization.CurrentCulture;
        var fallbackDefaults = new UserPreferenceDto(
            Culture: string.IsNullOrWhiteSpace(browserCulture)
                ? (string.IsNullOrWhiteSpace(sysSettings.Public.DefaultCulture) ? Defaults.Culture : sysSettings.Public.DefaultCulture)
                : browserCulture,
            TimeZone: string.IsNullOrWhiteSpace(sysSettings.Public.DefaultTimeZone) ? Defaults.TimeZone : sysSettings.Public.DefaultTimeZone);

        UserPreferenceDto? value = null;
        var hasLocalCulture = false;
        try
        {
            var payload = await js.InvokeAsync<PrefsPayload?>("blazorPrefs.get");
            if (payload is not null && HasAny(payload))
            {
                hasLocalCulture = !string.IsNullOrWhiteSpace(payload.Culture);
                value = Merge(fallbackDefaults, payload);
            }
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
                // 服务端不可用：使用系统默认设置。
            }
        }

        // 服务端在用户从未保存过偏好时也会返回一个「默认 culture」的 DTO（非 null）。
        // 折中：本机无 culture 偏好时——
        //   服务端仍是默认 culture ⇒ 说明从未显式保存过 ⇒ 让浏览器 culture 作为首次默认值；
        //   服务端是非默认 culture（如跨设备保存的 en-US）⇒ 尊重它，不被浏览器语言覆盖。
        var effectiveDefault = string.IsNullOrWhiteSpace(sysSettings.Public.DefaultCulture)
            ? Defaults.Culture
            : sysSettings.Public.DefaultCulture;
        if (!hasLocalCulture && value is not null && !string.IsNullOrWhiteSpace(browserCulture) &&
            string.Equals(value.Culture, effectiveDefault, StringComparison.OrdinalIgnoreCase))
            value = value with { Culture = browserCulture };

        _cached = value ?? fallbackDefaults;
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
        Changed?.Invoke();

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
        await localization.SetCultureAsync(culture);
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
