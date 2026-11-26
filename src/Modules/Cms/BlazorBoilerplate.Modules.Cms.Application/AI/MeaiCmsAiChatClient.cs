using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace BlazorBoilerplate.Modules.Cms.Application.AI;

/// <summary>
/// 基于 Microsoft.Extensions.AI（微软推荐的 AI 抽象层）的聊天客户端。
/// 通过 ChatClientBuilder 组装中间件管道：函数调用编排 + 日志；
/// Provider 为 OpenAI 兼容网关，换 Azure / Ollama 只需替换底层 IChatClient。
/// </summary>
public sealed class MeaiCmsAiChatClient : ICmsAiChatClient
{
    private readonly OpenAIClient _api;
    private readonly ILoggerFactory _loggerFactory;
    private readonly CmsAiOptions _options;
    private readonly string[] _candidates;
    private volatile string _activeModel;

    public MeaiCmsAiChatClient(CmsAiOptions options, ILoggerFactory loggerFactory)
    {
        _options = options;
        _loggerFactory = loggerFactory;
        _activeModel = options.GenerateModel;

        var candidates = new List<string> { options.GenerateModel };
        foreach (var f in options.FallbackModels)
            if (!string.IsNullOrWhiteSpace(f) && !candidates.Contains(f, StringComparer.Ordinal))
                candidates.Add(f);
        _candidates = [.. candidates];

        // baseUrl 已含 /v1，OpenAI SDK 自动补 /chat/completions。
        _api = new OpenAIClient(
            new ApiKeyCredential(options.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(options.BaseUrl) });
    }

    /// <summary>最近一次成功响应的模型；主模型限流降级后供前端展示真实来源。</summary>
    public string ModelName => _activeModel;

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        var response = await _api.GetOpenAIModelClient().GetModelsAsync(ct);
        var models = response.Value
            .Select(m => m.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id.Equals("auto", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return models;
    }

    public async Task<string> CompleteAsync(
        IReadOnlyList<CmsChatMessage> messages,
        string? model = null,
        CancellationToken ct = default)
    {
        var chatMessages = BuildMessages(messages);
        var chatOptions = BuildOptions();

        // 用户在下拉里显式选模型时，该请求只走这个模型（降级候选仅用于默认 auto）。
        var attempts = Attempts(model);

        Exception? lastError = null;
        foreach (var attemptModel in attempts)
        {
            try
            {
                var chat = BuildChat(attemptModel);
                using var timeoutCts = CreateTimeout(ct);
                var response = await chat.GetResponseAsync(chatMessages, chatOptions, timeoutCts.Token);
                if (!string.IsNullOrWhiteSpace(response.Text))
                {
                    _activeModel = attemptModel;
                    return response.Text;
                }
                lastError = new InvalidOperationException($"模型 {attemptModel} 返回空内容。");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lastError = new TimeoutException($"模型 {attemptModel} 请求超时（{_options.TimeoutSeconds}s）。");
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }
        throw new InvalidOperationException($"AI 全部候选模型不可用：{lastError?.Message ?? "未知错误"}");
    }

    public async Task StreamAsync(
        IReadOnlyList<CmsChatMessage> messages,
        Func<string, Task> onChunk,
        string? model = null,
        CancellationToken ct = default)
    {
        var chatMessages = BuildMessages(messages);
        var chatOptions = BuildOptions();

        foreach (var attemptModel in Attempts(model))
        {
            try
            {
                var chat = BuildChat(attemptModel);
                using var timeoutCts = CreateTimeout(ct);
                var any = false;
                await foreach (var update in chat.GetStreamingResponseAsync(chatMessages, chatOptions, timeoutCts.Token))
                {
                    var text = update.Text;
                    if (string.IsNullOrEmpty(text)) continue;
                    any = true;
                    await onChunk(text);
                }
                if (any)
                {
                    _activeModel = attemptModel;
                    return;
                }
            }
            catch (NotSupportedException)
            {
                // 网关未提供 SSE 流：交给下面的非流式兜底。
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 流式超时同样交给 CompleteAsync 兜底（它会按候选模型重试并给出可读错误）。
            }
            catch (Exception)
            {
                // 单个候选失败继续下一个；全部失败后由 CompleteAsync 抛出统一错误。
            }
        }

        // 所有候选都不支持/不可用流式时，仍把完整文本一次性交给前端，保证功能可用。
        var full = await CompleteAsync(messages, model, ct);
        await onChunk(full);
    }

    private ChatMessage[] BuildMessages(IReadOnlyList<CmsChatMessage> messages) =>
        messages
            .Select(m => new ChatMessage(
                m.Role.Equals("system", StringComparison.OrdinalIgnoreCase) ? ChatRole.System
                : m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? ChatRole.Assistant
                : ChatRole.User,
                m.Content))
            .ToArray();

    private ChatOptions BuildOptions()
    {
        var options = new ChatOptions
        {
            MaxOutputTokens = _options.MaxOutputTokens,
            Temperature = 0.7f
        };
        if (_options.UseJsonMode)
            options.ResponseFormat = ChatResponseFormat.Json;
        return options;
    }

    private IChatClient BuildChat(string model) =>
        new ChatClientBuilder(_api.GetChatClient(model).AsIChatClient())
            .UseFunctionInvocation()
            .UseLogging(_loggerFactory)
            .Build();

    private string[] Attempts(string? model)
    {
        var requested = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        return requested is null ? _candidates : [requested];
    }

    private CancellationTokenSource CreateTimeout(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(30, _options.TimeoutSeconds)));
        return cts;
    }
}
