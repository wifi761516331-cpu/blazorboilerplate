namespace BlazorBoilerplate.ModuleAbstractions;

/// <summary>AI 对话消息。</summary>
public sealed record AIChatMessage(string Role, string Content);

/// <summary>AI 对话请求。</summary>
public sealed record AIChatRequest(
    IReadOnlyList<AIChatMessage> Messages,
    string? Model = null,
    double Temperature = 0.7);

/// <summary>AI 对话响应。</summary>
public sealed record AIChatResult(
    string Content,
    string? Model = null,
    int? TotalTokens = null);

/// <summary>统一 AI 能力提供者契约（可实现多模型接入与词条自动翻译）。</summary>
public interface IAIProvider : IProvider
{
    /// <summary>执行对话补全。</summary>
    Task<AIChatResult> ChatAsync(AIChatRequest request, CancellationToken cancellationToken = default);

    /// <summary>将文本翻译为目标语言文化。</summary>
    Task<string> TranslateAsync(string text, string targetCulture, CancellationToken cancellationToken = default);
}

