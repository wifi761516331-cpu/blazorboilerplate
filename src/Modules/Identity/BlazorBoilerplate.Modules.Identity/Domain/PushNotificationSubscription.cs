using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Modules.Identity.Domain;

/// <summary>推送订阅记录。匿名也可订阅，故 UserId 可空；不接外部推送网关。</summary>
public sealed class PushNotificationSubscription : AuditableEntity<Guid>
{
    private PushNotificationSubscription() { }

    private PushNotificationSubscription(string deviceId, string platform, string pushChannel,
        string? endpoint, string? p256dh, string? auth, Guid? userId)
    {
        Id = Guid.CreateVersion7();
        DeviceId = deviceId;
        Platform = platform;
        PushChannel = pushChannel;
        Endpoint = endpoint;
        P256dh = p256dh;
        Auth = auth;
        UserId = userId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public string DeviceId { get; private set; } = default!;
    public string Platform { get; private set; } = default!;
    public string PushChannel { get; private set; } = default!;
    public string? Endpoint { get; private set; }
    public string? P256dh { get; private set; }
    public string? Auth { get; private set; }
    public Guid? UserId { get; private set; }

    public User? User { get; private set; }

    public static PushNotificationSubscription Create(string deviceId, string platform, string pushChannel,
        string? endpoint = null, string? p256dh = null, string? auth = null, Guid? userId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(pushChannel);
        return new PushNotificationSubscription(deviceId.Trim(), platform.Trim(), pushChannel.Trim(),
            endpoint, p256dh, auth, userId);
    }

    public void Update(string platform, string pushChannel, string? endpoint, string? p256dh, string? auth, Guid? userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(pushChannel);
        Platform = platform.Trim();
        PushChannel = pushChannel.Trim();
        Endpoint = endpoint;
        P256dh = p256dh;
        Auth = auth;
        UserId = userId;
    }
}

