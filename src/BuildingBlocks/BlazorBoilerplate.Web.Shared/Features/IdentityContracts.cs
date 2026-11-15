using BlazorBoilerplate.Shared.Infrastructure.Services.Contracts;

namespace BlazorBoilerplate.Shared.Features.Identity;

/// <summary>
/// 认证契约（Bluebird 写法：共享接口 + [Route("api/v1/[controller]/[action]/")] + IAppController）。
/// 服务端由 ApiHost/Controllers/IdentityController.cs 实现；匿名方法不带 AuthorizedApi。
/// </summary>
[Route("api/v1/[controller]/[action]/")]
public interface IIdentityController : IAppController
{
    [HttpPost] Task<SignInResponseDto> SignIn(SignInRequestDto request, CancellationToken cancellationToken) => default!;
    [HttpPost] Task<SignInResponseDto> Register(RegisterRequestDto request, CancellationToken cancellationToken) => default!;
    [HttpPost] Task SendEmailCode(SendEmailCodeRequestDto request, CancellationToken cancellationToken) => Task.CompletedTask;
    [HttpPost] Task ResetPasswordByEmail(ResetPasswordByEmailRequestDto request, CancellationToken cancellationToken) => Task.CompletedTask;
    [HttpPost] Task<TokenResponseDto> Refresh(RefreshTokenRequestDto request, CancellationToken cancellationToken) => default!;
    [HttpPost] Task RevokeRefreshToken(RefreshTokenRequestDto request, CancellationToken cancellationToken);
    [HttpGet] Task<IReadOnlyList<string>> GetSupportedCultures(CancellationToken cancellationToken) => default!;
}

/// <summary>
/// 当前用户 / 会话 / 资料 / 租户成员 / 权限码（Bluebird IUserController 的等价契约）。
/// AuthorizedApi：客户端必须附加 Bearer access token。
/// </summary>
[Route("api/v1/[controller]/[action]/"), AuthorizedApi]
public interface IUserController : IAppController
{
    [HttpGet] Task<CurrentUserDto> GetCurrentUser(CancellationToken cancellationToken);
    [HttpPost] Task SignOut(CancellationToken cancellationToken);
    [HttpPut] Task<CurrentUserDto> Update(EditUserRequestDto request, CancellationToken cancellationToken);
    [HttpPost] Task ChangePassword(ChangePasswordRequestDto request, CancellationToken cancellationToken);
    [HttpGet] Task<IReadOnlyList<UserSessionDto>> GetUserSessions(CancellationToken cancellationToken) => default!;
    [HttpPost("{id}")] Task RevokeSession(Guid id, CancellationToken cancellationToken);
    [HttpPost] Task UpdateSession(UpdateUserSessionRequestDto request, CancellationToken cancellationToken);
    [HttpPost("{enabled}")] Task SetNotificationEnabled(bool enabled, CancellationToken cancellationToken);
    [HttpGet] Task<IReadOnlyList<TenantDto>> GetTenants(CancellationToken cancellationToken) => default!;
    [HttpPost("{tenantId}")] Task LeaveTenant(Guid tenantId, CancellationToken cancellationToken);
    [HttpGet] Task<IReadOnlyList<PermissionDefinitionDto>> GetPermissionCatalog(CancellationToken cancellationToken) => default!;
    [HttpGet("{roleId}")] Task<IReadOnlyList<string>> GetRolePermissions(Guid roleId, CancellationToken cancellationToken) => default!;
    [HttpPost("{roleId}")] Task SetRolePermissions(Guid roleId, string[] codes, CancellationToken cancellationToken);
}

// ---- DTO（camelCase JSON：ApiHost 与 ApiClient 统一用 CamelCase 命名策略）----

public sealed record SignInRequestDto(string UserName, string Password, bool RememberMe = true, string? TenantId = null);
public sealed record RegisterRequestDto(string UserName, string Email, string Password, string? DisplayName = null, string? VerificationCode = null);
public sealed record SendEmailCodeRequestDto(string Email, string Purpose);
public sealed record ResetPasswordByEmailRequestDto(string Email, string VerificationCode, string NewPassword);
public record TokenResponseDto(string AccessToken, string RefreshToken, int ExpiresIn);
public sealed record SignInResponseDto(string AccessToken, string RefreshToken, int ExpiresIn, CurrentUserDto User)
    : TokenResponseDto(AccessToken, RefreshToken, ExpiresIn);
public sealed record RefreshTokenRequestDto(string RefreshToken, string? RequestedTenantId = null);

public sealed record CurrentUserDto(
    Guid Id,
    string UserName,
    string Email,
    string DisplayName,
    string[] Roles,
    string[] Permissions,
    string[] MenuPaths,
    Guid? TenantId,
    string? TenantName,
    bool NotificationsEnabled);

public sealed record EditUserRequestDto(string Email, string DisplayName);
public sealed record ChangePasswordRequestDto(string OldPassword, string NewPassword);

public sealed record UserSessionDto(
    Guid Id,
    string? IP,
    string? DeviceInfo,
    string? CultureName,
    string? TimeZone,
    bool NotificationsEnabled,
    DateTimeOffset StartedOnUtc,
    bool Current);

public sealed record UpdateUserSessionRequestDto(
    string? CultureName,
    string? TimeZone,
    bool? NotificationsEnabled,
    string? DeviceInfo);

public sealed record TenantDto(
    Guid Id,
    string Name,
    string? Title,
    string? Domain,
    bool IsActive,
    bool Accepted,
    bool Current);

public sealed record PermissionDefinitionDto(
    string Code,
    string Name,
    string? ModuleId,
    string? MenuPath,
    string Kind);
