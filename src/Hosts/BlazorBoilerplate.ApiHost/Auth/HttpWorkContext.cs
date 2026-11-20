using System.Security.Claims;
using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Application.Platform;

namespace BlazorBoilerplate.ApiHost.Auth;

/// <summary>基于当前 HTTP 请求与本地化上下文的统一工作上下文。</summary>
public sealed class HttpWorkContext(
    ICurrentUserContext user,
    IHttpContextAccessor httpContextAccessor,
    ILocalizationService localization) : IWorkContext
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public Guid? UserId => user.UserId;
    public string? UserName => Principal?.Identity?.Name ?? Principal?.FindFirstValue("unique_name");
    public Guid? TenantId => user.TenantId;
    public string Culture => localization.CurrentCulture;

    public IReadOnlyCollection<string> Roles => Principal?
        .FindAll(ClaimTypes.Role)
        .Concat(Principal.FindAll("role"))
        .Select(c => c.Value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray() ?? [];

    public IReadOnlyCollection<string> Permissions => Principal?
        .FindAll("perm")
        .Select(c => c.Value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray() ?? [];
}
