using System.Net;

namespace BlazorBoilerplate.Application.Localization;

/// <summary>
/// 后端业务异常：携带稳定的多语言错误码而非硬编码文案。
/// ApiExceptionFilter 会把它序列化为 { "error": 回退文案, "errorCode": "...", "errorArgs": [...] }，
/// 前端按当前界面语言用 ILocalizationService 翻译；词典缺失时回落 error 原文。
/// </summary>
public sealed class LocalizedApiException : InvalidOperationException
{
    public string ErrorCode { get; }
    public object[] Args { get; }

    public LocalizedApiException(string errorCode, string fallbackMessage, params object[] args)
        : base(fallbackMessage)
    {
        ErrorCode = errorCode;
        Args = args;
    }
}

/// <summary>
/// 前端异常：EnsureSuccessAsync 从错误体解析到 errorCode 后抛出，
/// 页面用 GetUserMessage 扩展方法按当前语言翻译。
/// </summary>
public sealed class ApiRequestException : HttpRequestException
{
    public string? ErrorCode { get; }
    public object[] Args { get; }

    public ApiRequestException(string message, string? errorCode, object[] args) : base(message)
    {
        ErrorCode = errorCode;
        Args = args;
    }
}

public static class ApiErrorExtensions
{
    /// <summary>
    /// 把异常转为当前语言的用户可读消息：
    /// 带 errorCode 且词典命中时按模板翻译（可带 {0} 占位参数），否则回落原始消息。
    /// </summary>
    public static string GetUserMessage(this Exception ex, ILocalizationService localization)
    {
        if (ex is ApiRequestException { ErrorCode: not null } are)
        {
            // GetString 未命中词条时原样返回 key，用它探测词典是否存在该错误码。
            var probe = localization.GetString(are.ErrorCode);
            if (!string.Equals(probe, are.ErrorCode, StringComparison.Ordinal))
                return localization.GetString(are.ErrorCode, are.Args);
        }
        return ex.Message;
    }
}
