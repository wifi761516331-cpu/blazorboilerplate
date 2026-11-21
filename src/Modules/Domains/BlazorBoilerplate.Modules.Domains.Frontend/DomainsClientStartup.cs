using System.Collections.Concurrent;
using System.Net.Http.Json;
using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Domains;

/// <summary>
/// Domains 模块前端启动器：通过 <see cref="DomainRouteResolverRegistry"/> 向宿主注册域名解析器，
/// 实现「未安装模块零开销、安装后自动按域名分发至 C 端入口与门户布局」的零宿主耦合机制。
/// </summary>
public sealed class DomainsClientStartup : IModuleClientStartup
{
    public int Order => 100;

    public void ConfigureServices(IServiceCollection services)
    {
        DomainRouteResolverRegistry.Register("domains", new DomainsRouteResolver());
    }

    private sealed class DomainsRouteResolver : IDomainRouteResolver
    {
        private sealed record WireResult(
            bool Found,
            string? ModuleId,
            string? SiteKey,
            string? EntryPath,
            bool AllowAdminRoutes);

        private sealed record CacheEntry(DomainRouteMatch? Match, long ExpiresAtTicks);

        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

        public void InvalidateCache() => _cache.Clear();

        public async Task<DomainRouteMatch?> ResolveAsync(HttpClient http, string host, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(host))
                return null;

            var key = host.Trim().ToLowerInvariant();
            var now = Environment.TickCount64;
            if (_cache.TryGetValue(key, out var cached) && cached.ExpiresAtTicks > now)
                return cached.Match;

            try
            {
                var wire = await http.GetFromJsonAsync<WireResult>(
                    $"api/domains/resolve?host={Uri.EscapeDataString(key)}", ct);
                var match = wire is { Found: true } && !string.IsNullOrWhiteSpace(wire.EntryPath)
                    ? new DomainRouteMatch(wire.EntryPath, wire.AllowAdminRoutes, wire.ModuleId, wire.SiteKey)
                    : null;
                _cache[key] = new CacheEntry(match, now + 10_000);
                return match;
            }
            catch
            {
                return null;
            }
        }
    }
}
