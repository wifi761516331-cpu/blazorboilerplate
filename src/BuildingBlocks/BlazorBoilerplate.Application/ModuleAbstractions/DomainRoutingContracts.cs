using System.Collections.Concurrent;

namespace BlazorBoilerplate.ModuleAbstractions;

/// <summary>域名路由匹配结果（平台与具体业务模块解耦）。</summary>
public sealed record DomainRouteMatch(
    string EntryPath,
    bool AllowAdminRoutes,
    string? ModuleId = null,
    string? SiteKey = null);

/// <summary>
/// 可选域名路由解析契约：由支持域名分发的模块（如 Domains）在前端启动器注册；
/// 未安装对应模块时注册表为空，宿主直接走默认平台行为。
/// </summary>
public interface IDomainRouteResolver
{
    Task<DomainRouteMatch?> ResolveAsync(HttpClient http, string host, CancellationToken ct = default);
    void InvalidateCache() { }
}

/// <summary>域名路由解析器注册表（支持动态模块热加载与卸载）。</summary>
public static class DomainRouteResolverRegistry
{
    public const string HttpItemKey = "BlazorBoilerplate.DomainRouteMatch";

    private static readonly string[] AdminPrefixes =
        ["/tenants", "/modules", "/system", "/domains", "/operations", "/settings", "/profile", "/notifications"];

    private static readonly ConcurrentDictionary<string, IDomainRouteResolver> Entries = new(StringComparer.OrdinalIgnoreCase);

    public static IDomainRouteResolver? Current => Entries.Values.FirstOrDefault();

    public static void Register(string moduleId, IDomainRouteResolver resolver)
    {
        if (string.IsNullOrWhiteSpace(moduleId) || resolver is null)
            return;
        Entries[moduleId.Trim()] = resolver;
    }

    public static void UnregisterModule(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
            return;
        Entries.TryRemove(moduleId.Trim(), out _);
    }

    public static bool IsAdminRoute(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var clean = path.Split('?', '#')[0].TrimEnd('/');
        return AdminPrefixes.Any(p =>
            clean.Equals(p, StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>判断是否需要拦截后台路由并重定向回入口（同时防止 EntryPath 本身为后台路径时陷入死循环）。</summary>
    public static bool ShouldRedirectAdminRoute(DomainRouteMatch match, string? path)
    {
        if (match.AllowAdminRoutes || !IsAdminRoute(path))
            return false;
        var cleanPath = (path ?? "/").Split('?', '#')[0].TrimEnd('/');
        var cleanEntry = (match.EntryPath ?? "/").Split('?', '#')[0].TrimEnd('/');
        return !string.Equals(cleanPath, cleanEntry, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>将前端展示路径映射为模块内部真实路由（例如 "/" -> "/shop"，"/aaa" -> "/shop/aaa"）。</summary>
    public static string ResolveTargetPath(DomainRouteMatch match, string? requestPath)
    {
        var entry = NormalizePath(match.EntryPath);
        var clean = NormalizePath(requestPath);
        if (entry == "/")
            return clean;
        if (clean == "/")
            return entry;
        if (clean.Equals(entry, StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith(entry + "/", StringComparison.OrdinalIgnoreCase))
            return clean;
        return $"{entry}/{clean.TrimStart('/')}";
    }

    /// <summary>若路径显式携带了模块前缀（如 "/shop/aaa"），剥离前缀还原为干净路径（"/aaa"）。</summary>
    public static bool TryStripEntryPrefix(DomainRouteMatch match, string? requestPath, out string strippedPath)
    {
        strippedPath = "/";
        var entry = NormalizePath(match.EntryPath);
        if (entry == "/")
            return false;

        var raw = string.IsNullOrWhiteSpace(requestPath) ? "/" : requestPath;
        var qIdx = raw.IndexOfAny(['?', '#']);
        var pathPart = qIdx >= 0 ? raw[..qIdx] : raw;
        var suffix = qIdx >= 0 ? raw[qIdx..] : string.Empty;
        var clean = NormalizePath(pathPart);

        // 路径恰好等于入口时不能剥离：否则 /site → / → 再次解析又到 /site，形成重定向循环。
        // 入口路径本身就是希望最终停留的真实路由，保持原样即可。
        if (clean.Equals(entry, StringComparison.OrdinalIgnoreCase))
            return false;

        if (clean.StartsWith(entry + "/", StringComparison.OrdinalIgnoreCase))
        {
            strippedPath = clean[entry.Length..] + suffix;
            return true;
        }

        return false;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";
        var clean = path.Split('?', '#')[0].Trim();
        if (!clean.StartsWith('/'))
            clean = "/" + clean;
        clean = clean.TrimEnd('/');
        return clean.Length == 0 ? "/" : clean;
    }
}
