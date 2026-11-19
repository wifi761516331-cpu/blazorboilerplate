using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BlazorBoilerplate.ApiHost.Auth;

/// <summary>
/// Controller 的业务异常 → { "error": "..." } 与恰当的状态码。
/// 否则 UseExceptionHandler 会把「账号或密码不正确」这类可预期错误包成 500 ProblemDetails，
/// 登录页/账户菜单就只显示「操作失败」，拿不到真正原因。
/// </summary>
public sealed class ApiExceptionFilter(ILogger<ApiExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var ex = context.Exception;
        var status = ex switch
        {
            KeyNotFoundException => StatusCodes.Status404NotFound,
            InvalidOperationException => StatusCodes.Status400BadRequest,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status500InternalServerError
        };

        if (status >= 500)
            logger.LogError(ex, "API 未处理异常：{Method} {Path}",
                context.HttpContext.Request.Method, context.HttpContext.Request.Path);

        context.Result = new ObjectResult(ex is Application.Localization.LocalizedApiException localized
            ? new { error = ex.Message, errorCode = localized.ErrorCode, errorArgs = localized.Args }
            : new { error = ex.Message })
        { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
