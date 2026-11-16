using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Modules.Identity.Domain;

/// <summary>租户成员。邀请（Accepted=false）与接受由 TenantService 维护。</summary>
public sealed class TenantUser : AuditableEntity<Guid>
{
    private TenantUser() { }

    private TenantUser(Guid tenantId, Guid userId, bool isOwner, bool accepted)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        UserId = userId;
        Accepted = accepted;
        IsOwner = isOwner;
        InvitedAtUtc = DateTimeOffset.UtcNow;
        AcceptedAtUtc = accepted ? InvitedAtUtc : null;
    }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public bool Accepted { get; private set; }
    public bool IsOwner { get; private set; }
    public DateTimeOffset InvitedAtUtc { get; private set; }
    public DateTimeOffset? AcceptedAtUtc { get; private set; }

    public Tenant? Tenant { get; private set; }
    public User? User { get; private set; }

    public static TenantUser Create(Guid tenantId, Guid userId, bool isOwner = false, bool accepted = false) =>
        new(tenantId, userId, isOwner, accepted);

    public void Accept()
    {
        if (Accepted) return;
        Accepted = true;
        AcceptedAtUtc = DateTimeOffset.UtcNow;
    }
}

