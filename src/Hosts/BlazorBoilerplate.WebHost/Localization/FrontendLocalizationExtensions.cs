using System.Globalization;
using System.Net.Http.Json;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Web.Shared.Localization;
using Microsoft.AspNetCore.Http;

namespace BlazorBoilerplate.WebHost.Localization;

/// <summary>
/// 前端本地化请求中间件：让 Blazor 预渲染（同步索引器 L[key]）开始前，
/// 进程级词典已按当前用户文化预热就绪，避免首屏输出未经翻译的原始 key。
/// </summary>
public static class FrontendLocalizationExtensions
{
    private const string CultureCookie = "bb_culture";

    /// <summary>
    /// 1) 解析请求文化：bb_culture cookie（用户已选） > 浏览器 Accept-Language（首次访问） > 平台默认；
    ///    首次按浏览器语言解析出的文化会写回 cookie，供后续请求与 Blazor 线路复用；
    /// 2) 文档请求到达 Razor 渲染前，把当前文化的词典预热进 FrontendLocalizationService 的进程级缓存。
    /// 只处理文档请求、每种文化一次，静态资源与后续线路直接命中缓存，避免每请求都访问 ApiHost。
    /// </summary>
    public static IApplicationBuilder UseFrontendLocalization(this IApplicationBuilder app)
    {
        var defaultCulture = LocalizationDefaults.DefaultChineseCulture;
        var supported = LocalizationDefaults.BuiltInLanguages.Select(l => l.Culture).ToList();

        return app.Use(async (context, next) =>
        {
            var culture = context.Request.Cookies[CultureCookie];
            var resolvedFromBrowser = false;
            if (string.IsNullOrWhiteSpace(culture))
            {
                // 首次访问没有语言 cookie：按浏览器 Accept-Language 选平台支持的文化。
                var browserCulture = ResolveFromAcceptLanguage(
                    context.Request.Headers.AcceptLanguage.ToString(), supported);
                if (browserCulture is not null)
                {
                    culture = browserCulture;
                    resolvedFromBrowser = true;
                }
            }
            culture = string.IsNullOrWhiteSpace(culture) ? defaultCulture : culture.Trim();

            try
            {
                var info = CultureInfo.GetCultureInfo(culture);
                CultureInfo.CurrentCulture = info;
                CultureInfo.CurrentUICulture = info;
            }
            catch (CultureNotFoundException)
            {
                // 非法 culture 名回落默认语言，并同步线程文化。
                culture = defaultCulture;
                var info = CultureInfo.GetCultureInfo(defaultCulture);
                CultureInfo.CurrentCulture = info;
                CultureInfo.CurrentUICulture = info;
                resolvedFromBrowser = false;
            }

            // 首次按浏览器语言解析成功后固化到 cookie：后续请求与 Blazor 线路据此渲染同一语言，
            // 也避免浏览器语言在后续请求间波动。用户之后手动切语言会覆盖该 cookie。
            if (resolvedFromBrowser)
            {
                context.Response.Cookies.Append(CultureCookie, culture, new CookieOptions
                {
                    Path = "/",
                    MaxAge = TimeSpan.FromDays(365),
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax
                });
            }

            // 关键：Blazor 预渲染用同步索引器 L[key] 渲染，遇 await 会先输出一帧（词典空 → 原始 key），
            // 之后 Changed 无法改写已生成的 HTML。因此在文档到达 Razor 渲染前先把词典预热进进程级缓存。
            var path = context.Request.Path.Value ?? string.Empty;
            var isDocument = !Path.HasExtension(path) || path.EndsWith('/');
            if (isDocument)
            {
                await TryPrewarmAsync(context, culture);
                // 回退链的第二级（父文化 en-US）也预热：SSR 首屏同步索引器在目标 key 缺失时会查它，
                // 不预热则读到空缓存直接显示原始 key。进程级 HasCulture 去重，额外成本仅一次请求。
                if (!string.Equals(culture, LocalizationDefaults.DefaultFallbackCulture, StringComparison.OrdinalIgnoreCase))
                    await TryPrewarmAsync(context, LocalizationDefaults.DefaultFallbackCulture);
            }

            await next();
        });
    }

    /// <summary>预热单个文化的进程级词典；已预热或 ApiHost 不可达时静默跳过，不阻断渲染。</summary>
    private static async Task TryPrewarmAsync(HttpContext context, string culture)
    {
        if (FrontendLocalizationService.HasCulture(culture))
            return;
        try
        {
            var http = context.RequestServices
                .GetRequiredService<IHttpClientFactory>().CreateClient();
            var dict = await http.GetFromJsonAsync<Dictionary<string, string>>(
                $"api/localization/resources?culture={Uri.EscapeDataString(culture)}");
            if (dict is { Count: > 0 })
                FrontendLocalizationService.Prewarm(culture, dict);
        }
        catch
        {
            // ApiHost 暂不可达不阻断渲染；线路内 EnsureLoadedAsync 仍会重试。
        }
    }

    /// <summary>
    /// 按 Accept-Language 的 q 值从高到低挑出第一个平台支持的文化：
    /// 先精确匹配（zh-CN / en-US），再按两字母语言匹配（zh-* → zh-CN、en-* → en-US）。
    /// 都不匹配返回 null，由调用方回落到平台默认语言。
    /// </summary>
    private static string? ResolveFromAcceptLanguage(string? header, IReadOnlyList<string> supported)
    {
        if (string.IsNullOrWhiteSpace(header))
            return null;

        var candidates = new List<(string Tag, double Quality)>();
        foreach (var part in header.Split(','))
        {
            var chunk = part.Trim();
            if (chunk.Length == 0) continue;

            var pieces = chunk.Split(';');
            var tag = pieces[0].Trim();
            if (tag.Length == 0 || tag == "*") continue;

            var quality = 1.0;
            foreach (var param in pieces.Skip(1))
            {
                var kv = param.Trim();
                if (kv.StartsWith("q=", StringComparison.OrdinalIgnoreCase) &&
                    double.TryParse(kv[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out var q))
                    quality = q;
            }
            candidates.Add((tag, quality));
        }

        foreach (var (tag, _) in candidates.OrderByDescending(x => x.Quality))
        {
            CultureInfo info;
            try { info = CultureInfo.GetCultureInfo(tag); }
            catch (CultureNotFoundException) { continue; }

            var exact = supported.FirstOrDefault(c =>
                string.Equals(c, info.Name, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;

            var byLanguage = supported.FirstOrDefault(c =>
                string.Equals(
                    CultureInfo.GetCultureInfo(c).TwoLetterISOLanguageName,
                    info.TwoLetterISOLanguageName,
                    StringComparison.OrdinalIgnoreCase));
            if (byLanguage is not null) return byLanguage;
        }
        return null;
    }
}
