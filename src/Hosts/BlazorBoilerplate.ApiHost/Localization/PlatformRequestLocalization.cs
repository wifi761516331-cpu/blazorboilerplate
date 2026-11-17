using System.Globalization;
using BlazorBoilerplate.Application.Localization;
using Microsoft.AspNetCore.Localization;

namespace BlazorBoilerplate.ApiHost.Localization;

/// <summary>
/// 平台请求本地化中间件扩展：
/// 按 Query(?culture=) -> Cookie(.AspNetCore.Culture) -> Accept-Language -> 平台默认语言 -> 中间回退语言(en-US) 解析当前文化，
/// 不写死封闭语言列表，兼容运行时通过 Localization 模块动态新增的语言。
/// </summary>
public static class PlatformRequestLocalizationExtensions
{
    public static IApplicationBuilder UsePlatformRequestLocalization(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var candidate = ResolveCandidateCulture(context);
            if (string.IsNullOrWhiteSpace(candidate))
            {
                var localization = context.RequestServices.GetService<ILocalizationService>();
                if (localization is not null)
                {
                    var languages = await localization.GetSupportedLanguagesAsync(context.RequestAborted);
                    candidate = languages.FirstOrDefault(l => l.IsEnabled && l.IsDefault)?.Culture;
                }
            }

            var cultureInfo = ResolveValidCulture(candidate);
            CultureInfo.CurrentCulture = cultureInfo;
            CultureInfo.CurrentUICulture = cultureInfo;
            context.Features.Set<IRequestCultureFeature>(
                new RequestCultureFeature(new RequestCulture(cultureInfo, cultureInfo), provider: null));

            await next();
        });

    private static string? ResolveCandidateCulture(HttpContext context)
    {
        var queryCulture = context.Request.Query["culture"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(queryCulture))
            return queryCulture.Trim();

        if (context.Request.Cookies.TryGetValue(CookieRequestCultureProvider.DefaultCookieName, out var cookieValue) &&
            !string.IsNullOrWhiteSpace(cookieValue))
        {
            var parsed = CookieRequestCultureProvider.ParseCookieValue(cookieValue);
            var uiCulture = parsed?.UICultures.FirstOrDefault().Value;
            if (!string.IsNullOrWhiteSpace(uiCulture))
                return uiCulture.Trim();
        }

        if (context.Request.Headers.AcceptLanguage.Count > 0)
        {
            return context.Request.Headers.AcceptLanguage
                .ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Split(';')[0].Trim())
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && x != "*");
        }

        return null;
    }

    private static CultureInfo ResolveValidCulture(string? candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            try
            {
                var parsed = CultureInfo.GetCultureInfo(candidate);
                if (!string.IsNullOrWhiteSpace(parsed.Name))
                    return parsed;
            }
            catch (CultureNotFoundException)
            {
                // 非法文化名回退默认中间语言
            }
        }

        return CultureInfo.GetCultureInfo(LocalizationDefaults.DefaultFallbackCulture);
    }
}

