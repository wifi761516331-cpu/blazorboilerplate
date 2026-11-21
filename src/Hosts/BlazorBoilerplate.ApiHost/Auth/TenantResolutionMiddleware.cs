using BlazorBoilerplate.ApiHost.Setup;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BlazorBoilerplate.ApiHost.Auth;

public sealed record ResolvedTenant(Guid Id, string Name, string? Title, string? Domain, bool IsActive);

public static class TenantHttpItems
{
    public const string ResolvedTenantKey = "BlazorBoilerplate.ResolvedTenant";
    public const string TenantHostHeader = "X-Tenant-Host";
    internal const string LookupCacheKey = "tenant:lookup";

    public static ResolvedTenant? GetResolvedTenant(this HttpContext? context) =>
        context?.Items.TryGetValue(ResolvedTenantKey, out var value) == true ? value as ResolvedTenant : null;

    public static void InvalidateLookupCache(IMemoryCache cache) => cache.Remove(LookupCacheKey);

    public static void InvalidateDomainCache(IMemoryCache cache, string? domain) =>
        cache.Remove(LookupCacheKey);

    internal static string? ExtractHost(HttpContext context)
    {
        var raw = context.Request.Headers[TenantHostHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
            raw = context.Request.Host.Host;
        return NormalizeHost(raw);
    }

    internal static string? NormalizeHost(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var host = raw.Trim();
        var colon = host.IndexOf(':');
        if (colon > 0)
            host = host[..colon];
        return string.IsNullOrWhiteSpace(host) ? null : host.ToLowerInvariant();
    }
}

/// <summary>
/// 按请求域名（优先 X-Tenant-Host，回退 Request.Host）解析租户并写入 HttpContext.Items：
/// 1) 优先匹配自定义域名（Tenant.Domain）；
/// 2) 回退匹配子域名（如 acme.example.com / acme.localhost 匹配 Tenant.Name）；
/// 3) 若匹配到已停用租户，直接拦截并返回 403。
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next, IMemoryCache cache)
{
    private sealed record TenantsLookup(
        Dictionary<string, ResolvedTenant> ByDomain,
        Dictionary<string, ResolvedTenant> ByName);

    public async Task InvokeAsync(HttpContext context)
    {
        var setup = context.RequestServices.GetService<SetupStateProvider>();
        if (setup?.CurrentMode != SetupMode.Bootstrap)
        {
            var host = TenantHttpItems.ExtractHost(context);
            if (!string.IsNullOrWhiteSpace(host))
            {
                var lookup = await GetLookupAsync(context, context.RequestAborted);
                var resolved = ResolveFromLookup(lookup, host);

                if (resolved is not null)
                {
                    context.Items[TenantHttpItems.ResolvedTenantKey] = resolved;
                    var path = context.Request.Path.Value ?? string.Empty;
                    if (!resolved.IsActive &&
                        !path.StartsWith("/api/v1/Tenant/ResolveByDomain", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(
                            new { error = $"租户「{resolved.Title ?? resolved.Name}」已停用。" },
                            context.RequestAborted);
                        return;
                    }
                }
            }
        }

        await next(context);
    }

    private async Task<TenantsLookup?> GetLookupAsync(HttpContext context, CancellationToken ct)
    {
        if (cache.TryGetValue(TenantHttpItems.LookupCacheKey, out TenantsLookup? cached) && cached is not null)
            return cached;

        var db = context.RequestServices.GetService<IdentityDbContext>();
        if (db is null) return null;

        var rows = await db.Tenants.AsNoTracking()
            .Select(t => new ResolvedTenant(t.Id, t.Name, t.Title, t.Domain, t.IsActive))
            .ToListAsync(ct);

        var byDomain = new Dictionary<string, ResolvedTenant>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, ResolvedTenant>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in rows)
        {
            if (!string.IsNullOrWhiteSpace(t.Name))
                byName.TryAdd(t.Name.Trim().ToLowerInvariant(), t);
            if (!string.IsNullOrWhiteSpace(t.Domain))
                byDomain.TryAdd(t.Domain.Trim().ToLowerInvariant(), t);
        }

        var lookup = new TenantsLookup(byDomain, byName);
        cache.Set(TenantHttpItems.LookupCacheKey, lookup, TimeSpan.FromSeconds(30));
        return lookup;
    }

    private static ResolvedTenant? ResolveFromLookup(TenantsLookup? lookup, string host)
    {
        if (lookup is null) return null;

        if (lookup.ByDomain.TryGetValue(host, out var byDomain))
            return byDomain;

        var segments = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if ((segments.Length > 2 || (segments.Length == 2 && string.Equals(segments[1], "localhost", StringComparison.OrdinalIgnoreCase)))
            && lookup.ByName.TryGetValue(segments[0], out var bySubDomain))
        {
            return bySubDomain;
        }

        return null;
    }
}

public static class TenantResolutionExtensions
{
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantResolutionMiddleware>();
}
