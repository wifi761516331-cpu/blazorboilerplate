using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Modules.Identity.Domain;

/// <summary>租户。成员关系（TenantUser）与存在性由 TenantService 校验。</summary>
public sealed class Tenant : AuditableEntity<Guid>
{
    private Tenant() { }

    private Tenant(string name, string? title, string? domain)
    {
        Id = Guid.CreateVersion7();
        Name = name;
        Title = title;
        Domain = domain;
        IsActive = true;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string Name { get; private set; } = default!;
    public string? Title { get; private set; }
    public string? Domain { get; private set; }
    public bool IsActive { get; private set; }

    public ICollection<TenantUser> TenantUsers { get; private set; } = new List<TenantUser>();
    public ICollection<UserSession> Sessions { get; private set; } = new List<UserSession>();

    public static Tenant Create(string name, string? title = null, string? domain = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Tenant(name.Trim(), title?.Trim(), domain?.Trim());
    }

    public void Update(string name, string? title, string? domain, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Title = title?.Trim();
        Domain = domain?.Trim();
        IsActive = isActive;
    }
}

