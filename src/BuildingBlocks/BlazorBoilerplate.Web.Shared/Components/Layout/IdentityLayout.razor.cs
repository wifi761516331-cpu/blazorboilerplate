using System.Globalization;
using Bit.BlazorUI;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Web.Shared.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlazorBoilerplate.Web.Shared.Components.Layout;

// 基类 LayoutComponentBase / 接口 IDisposable 由 .razor 的 @inherits / @implements 声明，
// 此处只放字段与方法——避免两个分部声明指定不同基类导致 CS0263。
public partial class IdentityLayout
{
    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private FrontendLocalizationService FrontendLoc { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    private bool _dark;

    private sealed record CultureOption(string Code, string Text, string? FlagCode = null);

    private static readonly CultureOption[] FallbackCultures =
    [
        new("zh-CN", "简体中文", "CN"), new("en-US", "英语（美国）", "US")
    ];

    private List<CultureOption> _cultures = [.. FallbackCultures];
    private string _culture = "zh-CN";
    private bool _langOpen;

    protected override void OnInitialized()
    {
        FrontendLoc.Changed += HandleLocalizationChanged;
    }

    protected override async Task OnInitializedAsync()
    {
        await FrontendLoc.EnsureLoadedAsync();
        _culture = FrontendLoc.CurrentCulture;
        await LoadCulturesAsync();
    }

    private async Task LoadCulturesAsync()
    {
        try
        {
            var languages = await L.GetSupportedLanguagesAsync();
            _cultures = languages is { Count: > 0 }
                ? languages.Where(l => l.IsEnabled).OrderBy(l => l.DisplayOrder)
                    .Select(l => new CultureOption(
                        l.Culture,
                        string.IsNullOrWhiteSpace(l.Name) ? CultureName(l.Culture) : l.Name,
                        string.IsNullOrWhiteSpace(l.FlagCode) ? null : l.FlagCode))
                    .ToList()
                : [.. FallbackCultures];
        }
        catch
        {
            _cultures = [.. FallbackCultures];
        }
        if (_cultures.All(c => c.Code != _culture))
            _cultures.Insert(0, new CultureOption(_culture, CultureName(_culture)));
    }

    /// <summary>语言下拉切换：写本机偏好（cookie + localStorage，仅改 culture）+ 就地重渲染，不整页刷新。</summary>
    private async Task SelectLanguage(CultureOption culture)
    {
        _langOpen = false;
        if (string.Equals(_culture, culture.Code, StringComparison.OrdinalIgnoreCase))
            return;
        _culture = culture.Code;
        try { await JS.InvokeVoidAsync("blazorPrefs.setCulture", culture.Code); }
        catch { /* 预渲染 / 脚本未就绪：仍就地切换当前线路 */ }
        await FrontendLoc.SetCultureAsync(culture.Code);
    }

    private string CurrentFlag()
    {
        var current = _cultures.FirstOrDefault(c => c.Code == _culture);
        return FlagOf(current ?? new CultureOption(_culture, CultureName(_culture))) ?? "US";
    }

    private void HandleLocalizationChanged() => _ = InvokeAsync(async () =>
    {
        _culture = FrontendLoc.CurrentCulture;
        await LoadCulturesAsync();
        StateHasChanged();
    });

    private static string CultureName(string code) => code switch
    {
        "zh-CN" => "简体中文", "zh-TW" => "繁體中文",
        "en-US" => "English (US)", "en-GB" => "English (UK)",
        _ => code
    };

    /// <summary>文化码 → ISO 3166 两字母地区码，供 BitFlag 渲染国旗（对齐 AccountMenu）。</summary>
    private static string? RegionIso2(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) return null;
        var name = culture.Trim();
        if (name.StartsWith("zh-", StringComparison.OrdinalIgnoreCase)) return "CN";
        try
        {
            return new RegionInfo(CultureInfo.GetCultureInfo(name).LCID).TwoLetterISORegionName;
        }
        catch
        {
            return null;
        }
    }

    private static string? FlagOf(CultureOption culture) => culture.FlagCode ?? RegionIso2(culture.Code);

    /// <summary>
    /// ISO2 → Extras 包自带国旗图片路径。图片由 Bit.BlazorUI.Extras 的 staticwebassets 提供
    /// （_content/Bit.BlazorUI.Extras/flags/XX-flat-16.webp），直接用 &lt;img&gt; 渲染，
    /// 避免 BitFlag 组件在本环境的加载问题。非法/缺失码兜底 US。
    /// </summary>
    private static string FlagSrc(string? iso2)
    {
        var code = (iso2 ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length != 2 || !code.All(char.IsAsciiLetterUpper))
            code = "US";
        return $"_content/Bit.BlazorUI.Extras/flags/{code}-flat-16.webp";
    }

    public void Dispose()
    {
        FrontendLoc.Changed -= HandleLocalizationChanged;
    }
}
