using BlazorBoilerplate.Application.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorBoilerplate.ApiHost.Controllers;

/// <summary>
/// 平台多语言公共接口：提供可用语言列表与合并后的翻译词典（自动回退中间语言 en-US）。
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/localization")]
public sealed class LocalizationController(ILocalizationService localization) : ControllerBase
{
    [HttpGet("languages")]
    public Task<IReadOnlyList<LanguageInfo>> GetLanguages(CancellationToken cancellationToken) =>
        localization.GetSupportedLanguagesAsync(cancellationToken);

    [HttpGet("resources")]
    public async Task<IReadOnlyDictionary<string, string>> GetResources(
        [FromQuery] string? culture,
        CancellationToken cancellationToken)
    {
        var target = string.IsNullOrWhiteSpace(culture) ? localization.CurrentCulture : culture.Trim();
        return await localization.GetAllStringsAsync(target, cancellationToken);
    }
}

