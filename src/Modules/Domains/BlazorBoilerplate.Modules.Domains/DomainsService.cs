using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Modules.Domains;

public sealed record DomainRouteBindingDto(
    Guid Id,
    string Host,
    string? ModuleId,
    string? SiteKey,
    string EntryPath,
    bool AllowAdminRoutes,
    bool Enabled,
    string? Description,
    Guid? TenantId,
    DateTimeOffset CreatedAtUtc);

public sealed record DomainRouteUpsertRequest(
    string Host,
    string? ModuleId,
    string? SiteKey,
    string EntryPath,
    bool AllowAdminRoutes,
    bool Enabled,
    string? Description,
    Guid? TenantId = null);

public sealed record DomainResolveResultDto(
    bool Found,
    string? Host = null,
    string? ModuleId = null,
    string? SiteKey = null,
    string? EntryPath = null,
    bool AllowAdminRoutes = true,
    Guid? TenantId = null);

public sealed class DomainsService(DomainsDbContext db, ICacheService cache)
{
    private const string CacheTag = "domains:routes";

    public async Task<List<DomainRouteBindingDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.Bindings.AsNoTracking()
            .OrderBy(x => x.Host)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<DomainRouteBindingDto> CreateAsync(DomainRouteUpsertRequest req, CancellationToken ct = default)
    {
        var (host, entryPath) = ValidateAndNormalize(req);
        if (await db.Bindings.AnyAsync(x => x.Host == host, ct))
            throw new LocalizedApiException("domains.error.hostExists", $"域名已存在：{host}", host);

        var entity = new DomainRouteBinding
        {
            Host = host,
            ModuleId = NormalizeOptional(req.ModuleId),
            SiteKey = NormalizeOptional(req.SiteKey),
            EntryPath = entryPath,
            AllowAdminRoutes = req.AllowAdminRoutes,
            Enabled = req.Enabled,
            Description = NormalizeOptional(req.Description),
            TenantId = req.TenantId
        };
        db.Bindings.Add(entity);
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheTag, ct);
        return ToDto(entity);
    }

    public async Task<DomainRouteBindingDto> UpdateAsync(Guid id, DomainRouteUpsertRequest req, CancellationToken ct = default)
    {
        var entity = await db.Bindings.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException($"域名绑定不存在：{id}");

        var (host, entryPath) = ValidateAndNormalize(req);
        if (await db.Bindings.AnyAsync(x => x.Host == host && x.Id != id, ct))
            throw new LocalizedApiException("domains.error.hostExists", $"域名已存在：{host}", host);

        entity.Host = host;
        entity.ModuleId = NormalizeOptional(req.ModuleId);
        entity.SiteKey = NormalizeOptional(req.SiteKey);
        entity.EntryPath = entryPath;
        entity.AllowAdminRoutes = req.AllowAdminRoutes;
        entity.Enabled = req.Enabled;
        entity.Description = NormalizeOptional(req.Description);
        entity.TenantId = req.TenantId;
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheTag, ct);
        return ToDto(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.Bindings.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException($"域名绑定不存在：{id}");
        db.Bindings.Remove(entity);
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheTag, ct);
    }

    public async Task<DomainResolveResultDto> ResolveAsync(string? rawHost, CancellationToken ct = default)
    {
        var host = NormalizeHost(rawHost);
        if (string.IsNullOrEmpty(host))
            return new DomainResolveResultDto(false);

        var cacheKey = $"domains:resolve:{host}";
        var cached = await cache.GetAsync<DomainResolveResultDto>(cacheKey, ct);
        if (cached is not null)
            return cached;

        var enabled = await db.Bindings.AsNoTracking()
            .Where(x => x.Enabled)
            .ToListAsync(ct);

        var match = enabled.FirstOrDefault(x => string.Equals(x.Host, host, StringComparison.OrdinalIgnoreCase))
            ?? enabled.FirstOrDefault(x => x.Host.StartsWith("*.", StringComparison.Ordinal)
                && host.EndsWith(x.Host[1..], StringComparison.OrdinalIgnoreCase));

        var result = match is null
            ? new DomainResolveResultDto(false, Host: host)
            : new DomainResolveResultDto(
                true,
                match.Host,
                match.ModuleId,
                match.SiteKey,
                match.EntryPath,
                match.AllowAdminRoutes,
                match.TenantId);

        await cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(5), CacheTag, ct);
        return result;
    }

    private static (string Host, string EntryPath) ValidateAndNormalize(DomainRouteUpsertRequest req)
    {
        var host = NormalizeHost(req.Host);
        if (string.IsNullOrWhiteSpace(host))
            throw new LocalizedApiException("domains.error.hostRequired", "域名不能为空。");

        var entryPath = (req.EntryPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(entryPath))
            throw new LocalizedApiException("domains.error.entryPathRequired", "入口路径不能为空。");

        if (entryPath.StartsWith("//", StringComparison.Ordinal) || entryPath.StartsWith("/\\", StringComparison.Ordinal))
            throw new LocalizedApiException("domains.error.invalidEntryPath", "入口路径格式无效。");

        if (!entryPath.StartsWith('/') && !entryPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !entryPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            entryPath = "/" + entryPath;

        return (host, entryPath);
    }

    private static string NormalizeHost(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var host = raw.Trim().ToLowerInvariant();
        var colon = host.IndexOf(':');
        if (colon > 0) host = host[..colon];
        return host;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DomainRouteBindingDto ToDto(DomainRouteBinding x) =>
        new(x.Id, x.Host, x.ModuleId, x.SiteKey, x.EntryPath, x.AllowAdminRoutes, x.Enabled, x.Description, x.TenantId, x.CreatedAtUtc);
}
