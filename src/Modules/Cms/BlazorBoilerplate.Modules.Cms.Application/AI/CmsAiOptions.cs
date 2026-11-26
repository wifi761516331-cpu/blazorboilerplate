using System.Text.Json;

namespace BlazorBoilerplate.Modules.Cms.Application.AI;

/// <summary>
/// CMS AI 官网生成配置（CmsAi 配置节）。
/// BaseUrl 为空时视为未启用，模块回退到 DefaultCmsAiDesignerService 占位实现。
/// 配置由 CMS 模块自读 data/cms/ai.json（data/ 已 gitignore），密钥不进版本库。
/// </summary>
public sealed class CmsAiOptions
{
    public const string SectionName = "CmsAi";

    /// <summary>OpenAI 兼容网关根地址，如 http://127.0.0.1:31415/v1。</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Bearer API Key。</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>整站草稿生成模型。</summary>
    public string GenerateModel { get; init; } = "auto";

    /// <summary>主模型限流/不可用时的依次降级候选。</summary>
    public string[] FallbackModels { get; init; } = [];

    /// <summary>单次生成超时秒数。不稳定网关可能长时间无响应，默认 10 分钟。</summary>
    public int TimeoutSeconds { get; init; } = 600;

    /// <summary>整站草稿最多生成轮数：结构校验失败时带错误让模型自修复。</summary>
    public int MaxRepairRounds { get; init; } = 3;

    /// <summary>
    /// 单次生成最大输出 token。整站 JSON（多个区块 + 微件 props）很容易超过 8192，
    /// 被截断后模型输出残缺 JSON 导致解析失败；默认拉满到 128K（主流大模型上下文上限），
    /// 让网关/模型自行按能力截断，不再由客户端的 8192 硬上限造成截断。
    /// </summary>
    public int MaxOutputTokens { get; init; } = 131072;

    /// <summary>
    /// 是否发送 response_format=json_object。免费网关常见「不支持 json 模式」，
    /// 默认关闭：靠 system prompt 约束 + 客户端 ExtractJson 剥离围栏，兼容性更好。
    /// </summary>
    public bool UseJsonMode { get; init; } = false;

    public bool Enabled =>
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(GenerateModel);

    /// <summary>模块自有配置文件：{contentRoot}/data/cms/ai.json（data/ 已 gitignore）。</summary>
    public static string DefaultPath(string contentRoot) =>
        Path.Combine(contentRoot, "data", "cms", "ai.json");

    /// <summary>从模块配置文件加载；文件缺失或损坏时返回空配置（回退占位器），不阻断模块加载。</summary>
    public static CmsAiOptions LoadFromFile(string path)
    {
        if (!File.Exists(path)) return new CmsAiOptions();
        try
        {
            return JsonSerializer.Deserialize<CmsAiOptions>(File.ReadAllText(path), Json) ?? new CmsAiOptions();
        }
        catch
        {
            return new CmsAiOptions();
        }
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
