using System.Security.Claims;
using BlazorBoilerplate.Application.Abstractions;

namespace BlazorBoilerplate.ApiHost.Auth;

/// <summary>从当前 HTTP 请求的 ClaimsPrincipal 解析操作人与租户上下文，供审计拦截器使用。</summary>
public sealed class HttpCurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    public Guid? UserId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            var raw = user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public Guid? TenantId
    {
        get
        {
            var raw = httpContextAccessor.HttpContext?.User.FindFirstValue("tid");
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }
}

