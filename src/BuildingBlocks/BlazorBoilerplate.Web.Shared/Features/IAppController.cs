using System;

namespace BlazorBoilerplate.Shared.Infrastructure.Services.Contracts;

/// <summary>
/// 共享控制器契约标记（与 Bluebird.Shared 的 IAppController 语义一致）：
/// 接口定义 API 形状，服务端由具体 Controller 类实现同名路由，
/// 客户端由 BlazorBoilerplateApiClient 按约定 URL 调用。
/// </summary>
public interface IAppController;

/// <summary>标记该接口/方法的请求需要附加 Bearer access token（客户端侧语义，与 Bluebird 的 AuthorizedApi 一致）。</summary>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method)]
public sealed class AuthorizedApiAttribute : Attribute;

/// <summary>控制器路由模板（Bluebird 风格：api/v1/[controller]/[action]/）。</summary>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method)]
internal sealed class RouteAttribute(string template) : Attribute
{
    public string Template { get; } = template;
}

[AttributeUsage(AttributeTargets.Method)] internal sealed class HttpGetAttribute(string? template = null) : Attribute { public string? Template { get; } = template; }
[AttributeUsage(AttributeTargets.Method)] internal sealed class HttpPostAttribute(string? template = null) : Attribute { public string? Template { get; } = template; }
[AttributeUsage(AttributeTargets.Method)] internal sealed class HttpPutAttribute(string? template = null) : Attribute { public string? Template { get; } = template; }
[AttributeUsage(AttributeTargets.Method)] internal sealed class HttpDeleteAttribute(string? template = null) : Attribute { public string? Template { get; } = template; }
