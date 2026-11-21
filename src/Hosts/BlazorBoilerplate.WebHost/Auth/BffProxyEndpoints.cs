using System.Net.Http.Headers;
using System.Net.WebSockets;
using BlazorBoilerplate.Web.Shared.Services;

namespace BlazorBoilerplate.WebHost.Auth;

/// <summary>
/// BFF 反向代理端点（将浏览器同源 /api/* 请求转发到 ApiHost）：
/// 服务端从 HttpOnly Cookie（bb_auth）解出 JWT 附加 Authorization: Bearer，
/// 同时透传 X-Tenant-Host 与 X-Elevated-Token，确保浏览器 WASM 侧不接触明文令牌。
/// </summary>
public static class BffProxyEndpoints
{
    private static readonly string[] HopByHopHeaders =
    [
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailers", "Transfer-Encoding", "Upgrade", "Host"
    ];

    public static void MapBffApiProxy(this IEndpointRouteBuilder endpoints)
    {
        endpoints.Map("/api/{**catchAll}", ProxyAsync).AllowAnonymous();
        endpoints.Map("/hubs/{**catchAll}", ProxyAsync).AllowAnonymous();
    }

    private static async Task ProxyAsync(
        HttpContext context,
        IHttpClientFactory httpFactory,
        IAuthSession authSession)
    {
        var client = httpFactory.CreateClient(WebAuth.RefreshClient);
        if (context.WebSockets.IsWebSocketRequest)
        {
            await ProxyWebSocketAsync(context, client.BaseAddress!, authSession);
            return;
        }

        var targetUri = new Uri(
            client.BaseAddress!,
            context.Request.Path.Value!.TrimStart('/') + context.Request.QueryString.Value);

        using var outbound = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

        if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            outbound.Content = new StreamContent(context.Request.Body);
            if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
                outbound.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
        }

        foreach (var header in context.Request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase) ||
                string.Equals(header.Key, "Cookie", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;

            outbound.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        if (!string.IsNullOrWhiteSpace(context.Request.Host.Host))
            outbound.Headers.TryAddWithoutValidation(AuthHeaderHandler.TenantHostHeader, context.Request.Host.Host);

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var token = await authSession.GetAccessTokenAsync(context.RequestAborted);
            if (!string.IsNullOrWhiteSpace(token))
                outbound.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else if (context.Request.Headers.TryGetValue("Authorization", out var incomingAuth) &&
                 !string.IsNullOrWhiteSpace(incomingAuth.ToString()))
        {
            outbound.Headers.TryAddWithoutValidation("Authorization", incomingAuth.ToArray());
        }

        using var response = await client.SendAsync(
            outbound,
            HttpCompletionOption.ResponseHeadersRead,
            context.RequestAborted);

        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
                context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        foreach (var header in response.Content.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
                context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    private static async Task ProxyWebSocketAsync(
        HttpContext context,
        Uri apiBase,
        IAuthSession authSession)
    {
        var wsScheme = string.Equals(apiBase.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
        var baseBuilder = new UriBuilder(apiBase) { Scheme = wsScheme };
        var targetUri = new Uri(
            baseBuilder.Uri,
            context.Request.Path.Value!.TrimStart('/') + context.Request.QueryString.Value);

        using var clientWs = new ClientWebSocket();
        foreach (var proto in context.WebSockets.WebSocketRequestedProtocols)
            clientWs.Options.AddSubProtocol(proto);

        if (!string.IsNullOrWhiteSpace(context.Request.Host.Host))
            clientWs.Options.SetRequestHeader(AuthHeaderHandler.TenantHostHeader, context.Request.Host.Host);

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var token = await authSession.GetAccessTokenAsync(context.RequestAborted);
            if (!string.IsNullOrWhiteSpace(token))
                clientWs.Options.SetRequestHeader("Authorization", $"Bearer {token}");
        }
        else if (context.Request.Headers.TryGetValue("Authorization", out var incomingAuth) &&
                 !string.IsNullOrWhiteSpace(incomingAuth.ToString()))
        {
            clientWs.Options.SetRequestHeader("Authorization", incomingAuth.ToString());
        }

        await clientWs.ConnectAsync(targetUri, context.RequestAborted);
        using var serverWs = await context.WebSockets.AcceptWebSocketAsync(clientWs.SubProtocol);

        await Task.WhenAny(
            PumpWebSocketAsync(serverWs, clientWs, context.RequestAborted),
            PumpWebSocketAsync(clientWs, serverWs, context.RequestAborted));
    }

    private static async Task PumpWebSocketAsync(WebSocket source, WebSocket destination, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (source.State == WebSocketState.Open && destination.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await source.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    if (destination.State is WebSocketState.Open or WebSocketState.CloseReceived)
                        await destination.CloseOutputAsync(result.CloseStatus ?? WebSocketCloseStatus.NormalClosure, result.CloseStatusDescription, ct);
                    break;
                }
                await destination.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }
}

