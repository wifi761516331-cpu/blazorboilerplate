namespace BlazorBoilerplate.Modules.Cms.Application.Caching;

/// <summary>
/// CMS 缓存键与 Tag 约定：
/// 所有缓存值均以 string（JSON）存储，避免模块 ALC 类型成为内存缓存 GC root 影响热卸载。
/// </summary>
public static class CmsCacheDefaults
{
    public const string PublicSiteTag = "cms:sites";
    public const string LayoutsTag = "cms:layouts";
    public const string LayoutsKey = "cms:layouts:all";

    public static string PublicSiteKey(string siteKey, string? culture = null)
    {
        var c = string.IsNullOrWhiteSpace(culture) ? "default" : culture.Trim().ToLowerInvariant();
        return $"cms:public-site:{siteKey.Trim().ToLowerInvariant()}:{c}";
    }
}
