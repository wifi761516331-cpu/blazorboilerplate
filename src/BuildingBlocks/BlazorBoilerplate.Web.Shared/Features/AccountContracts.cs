using BlazorBoilerplate.Application.OData;
using BlazorBoilerplate.Shared.Features.Identity;
using BlazorBoilerplate.Shared.Infrastructure.Services.Contracts;

namespace BlazorBoilerplate.Shared.Features.Account;

/// <summary>租户契约（Switch 通过刷新 access token 切换 t-id claim）。</summary>
[Route("api/v1/[controller]/[action]/"), AuthorizedApi]
public interface ITenantController : IAppController
{
    [HttpGet] Task<TenantDto?> GetCurrentTenant(CancellationToken cancellationToken) => default!;
    [HttpGet] Task<IReadOnlyList<TenantDto>> GetTenants(CancellationToken cancellationToken) => default!;
    [HttpGet] Task<ODataPage<TenantDto>> QueryTenants(CancellationToken cancellationToken) => default!;
    [HttpPost] Task<TenantDto> Create(TenantDto request, CancellationToken cancellationToken) => default!;
    [HttpPut] Task<TenantDto> Update(TenantDto request, CancellationToken cancellationToken) => default!;
    [HttpPost] Task InviteUser(InviteUserToTenantRequestDto request, CancellationToken cancellationToken);
    [HttpGet("{tenantId}")] Task<IReadOnlyList<TenantMemberDto>> GetMembers(Guid tenantId, CancellationToken cancellationToken) => default!;
    [HttpPost("{tenantId}/{userId}")] Task RemoveUser(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    [HttpPost("{tenantId}")] Task AcceptInvitation(Guid tenantId, CancellationToken cancellationToken);
    [HttpPost("{tenantId}")] Task<TokenResponseDto> Switch(Guid tenantId, CancellationToken cancellationToken) => default!;
}

/// <summary>平台全局租户管理（仅限平台超级管理员）。</summary>
[Route("api/v1/[controller]/[action]/"), AuthorizedApi]
public interface ITenantManagementController : IAppController
{
    [HttpGet] Task<IReadOnlyList<TenantDto>> GetTenants(CancellationToken cancellationToken) => default!;
    [HttpGet] Task<ODataPage<TenantDto>> QueryTenants(CancellationToken cancellationToken) => default!;
    [HttpPost] Task<TenantDto> Create(TenantDto request, CancellationToken cancellationToken) => default!;
    [HttpPut] Task<TenantDto> Update(TenantDto request, CancellationToken cancellationToken) => default!;
}

/// <summary>
/// 用户偏好（BlazorBoilerplate 扩展契约，Bluebird 把这些放在设备端 IStorageService）。
/// 服务端保存一份用于跨设备同步，客户端仍以 localStorage 为首选。
/// </summary>
[Route("api/v1/[controller]/[action]/"), AuthorizedApi]
public interface IUserPreferenceController : IAppController
{
    [HttpGet] Task<UserPreferenceDto> Get(CancellationToken cancellationToken) => default!;
    [HttpPost] Task<UserPreferenceDto> Save(UserPreferenceDto request, CancellationToken cancellationToken) => default!;
}

/// <summary>站内通知与邮件通知（会话状态 + 消息列表 + 已读 + SMTP 邮件通知）。</summary>
[Route("api/v1/[controller]/[action]/"), AuthorizedApi]
public interface INotificationController : IAppController
{
    [HttpGet] Task<IReadOnlyList<NotificationDto>> GetNotifications(CancellationToken cancellationToken) => default!;
    [HttpGet] Task<int> GetUnreadCount(CancellationToken cancellationToken) => default!;
    [HttpPost("{id}")] Task MarkRead(Guid id, CancellationToken cancellationToken);
    [HttpPost] Task MarkAllRead(CancellationToken cancellationToken);
    [HttpPost] Task<NotificationDto> SendTestNotification(CancellationToken cancellationToken) => default!;
    [HttpPost] Task<SendEmailNotificationResultDto> SendTestEmail(SendEmailNotificationRequestDto request, CancellationToken cancellationToken) => default!;
}

/// <summary>
/// 推送订阅（Bluebird IPushNotificationController 等价契约，匿名可订阅）。
/// 当前实现持久化订阅记录，不接外部 VAPID/FCM 推送网关（见 README 说明）。
/// </summary>
[Route("api/v1/[controller]/[action]/")]
public interface IPushNotificationController : IAppController
{
    [HttpPost] Task Subscribe(PushNotificationSubscriptionDto subscription, CancellationToken cancellationToken);
    [HttpPost] Task Unsubscribe(PushNotificationSubscriptionDto subscription, CancellationToken cancellationToken);
}

// ---- DTO ----

public sealed record InviteUserToTenantRequestDto(Guid TenantId, string? Email = null, string? PhoneNumber = null);

public sealed record TenantMemberDto(
    Guid UserId,
    string UserName,
    string Email,
    string? PhoneNumber,
    string DisplayName,
    bool IsOwner,
    bool Accepted);

public sealed record ElevatedAccessRequestDto(string Password);

public sealed record ElevatedAccessResponseDto(string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>设备偏好：语言 / 时区 / 主题 / 强调色 / 通知开关。</summary>
public sealed record UserPreferenceDto(
    string Culture = "zh-CN",
    string TimeZone = "Asia/Shanghai",
    string Theme = "light",
    string? AccentColor = null,
    bool NotificationsEnabled = false);

public sealed record NotificationDto(
    Guid Id,
    string Title,
    string Message,
    bool IsRead,
    DateTimeOffset CreatedAtUtc,
    string? PageUrl);

public sealed record SendEmailNotificationRequestDto(
    string? ToEmail = null,
    string? Subject = null,
    string? Message = null);

public sealed record SendEmailNotificationResultDto(
    string ToEmail,
    string Subject,
    DateTimeOffset SentAtUtc);

public sealed record PushNotificationSubscriptionDto(
    string DeviceId,
    string Platform,
    string PushChannel,
    string? Endpoint = null,
    string? P256dh = null,
    string? Auth = null);
