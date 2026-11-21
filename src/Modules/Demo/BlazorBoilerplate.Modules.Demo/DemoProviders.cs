using BlazorBoilerplate.ModuleAbstractions;

namespace BlazorBoilerplate.Modules.Demo;

/// <summary>
/// 可插拔策略提供者契约示例（继承 <see cref="IProvider"/>）。
/// </summary>
public interface IDemoNotifierProvider : IProvider
{
    string SendPreview(string target, string message);
}

[SystemName("demo.notifier.log")]
[FriendlyName("Demo 日志通知策略")]
[ProviderOrder(100)]
[ProviderIcon("Document")]
public sealed class LogDemoNotifierProvider : IDemoNotifierProvider
{
    public string SendPreview(string target, string message) =>
        $"[LogProvider] -> {target}: {message}";
}

[SystemName("demo.notifier.email")]
[FriendlyName("Demo 邮件通知策略")]
[ProviderOrder(200)]
[ProviderIcon("Mail")]
public sealed class EmailDemoNotifierProvider : IDemoNotifierProvider
{
    public string SendPreview(string target, string message) =>
        $"[EmailProvider] -> {target}: {message}";
}

