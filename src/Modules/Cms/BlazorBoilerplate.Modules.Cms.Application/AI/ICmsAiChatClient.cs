namespace BlazorBoilerplate.Modules.Cms.Application.AI;

/// <summary>CMS 与具体 AI SDK 之间的消息（放在 Application，宿主与模块 ALC 共用类型身份）。</summary>
public sealed record CmsChatMessage(string Role, string Content);

/// <summary>
/// CMS 与具体 AI SDK 之间的最小契约。实际实现基于 Microsoft.Extensions.AI.IChatClient，
/// 模块运行时不感知具体 Provider；换 Azure / Ollama 只需替换实现，不改调用方。
/// </summary>
public interface ICmsAiChatClient
{
    /// <summary>发送完整对话，返回模型原始文本（不做 JSON 提取）。model 为空时使用配置默认模型。</summary>
    Task<string> CompleteAsync(
        IReadOnlyList<CmsChatMessage> messages,
        string? model = null,
        CancellationToken ct = default);

    /// <summary>流式生成：每收到一段模型输出回调一次。网关不支持流式时退化为一次性回调完整文本。</summary>
    Task StreamAsync(
        IReadOnlyList<CmsChatMessage> messages,
        Func<string, Task> onChunk,
        string? model = null,
        CancellationToken ct = default);

    /// <summary>拉取网关可用模型列表（保留 auto 在最前，供前端下拉）。</summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);

    /// <summary>当前生效的模型名（写入 ProviderNote 供前端展示）。</summary>
    string ModelName { get; }
}
