namespace BlazorBoilerplate.Web.Shared.Services;

/// <summary>服务端渲染模式裁决器（用于 Server 导航到 Auto/WASM 模块时触发整页切换）。</summary>
public interface IRenderModeArbitrator
{
    bool ShouldUseWebAssembly(string? path);
}

/// <summary>会话实时吊销监听（Server 端通过 SignalR 连接 ApiHost；WASM 端可选）。</summary>
public interface ISessionRevocationWatcher
{
    event Func<Task>? OnSessionRevoked;
    Task StartAsync(CancellationToken cancellationToken = default);
}

