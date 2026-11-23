using System.Text.Json;
using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Components.Designer;

public partial class CmsWidgetPropertyEditor
{
    private sealed record FeatureItemModel(string Icon, string Title, string Description);
    private sealed record StatItemModel(string Value, string Label);
    private sealed record GalleryItemModel(string Url, string Caption);
    private sealed record FaqItemModel(string Question, string Answer);
    private sealed record SlideItemModel(string? ImageUrl, string? Title, string? Subtitle, string? ButtonText, string? ButtonHref);
    private sealed record TabItemModel(string Title, string Content, string? ImageUrl);
    private sealed record TestimonialItemModel(string Quote, string Author, string? Role, string? AvatarUrl);
    private sealed record PartnerItemModel(string Name, string? LogoUrl, string? LinkUrl);
    private sealed record PricingPlanModel(string Name, string Price, string? Period, string? Badge, bool Highlighted, List<string>? Features, string? ButtonText, string? ButtonHref);
    private sealed record ArticleItemModel(string Title, string? Category, string? Date, string? Summary, string? Content, string? CoverUrl);

    [Parameter] public Guid WidgetId { get; set; }
    [Parameter] public string WidgetType { get; set; } = string.Empty;
    [Parameter] public string PropsJson { get; set; } = "{}";
    [Parameter] public EventCallback<string> PropsJsonChanged { get; set; }
    [Parameter] public string StyleJson { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> StyleJsonChanged { get; set; }
    [Parameter] public EventCallback OnPickImage { get; set; }

    private Guid _loadedWidgetId;
    private string _loadedPropsJson = string.Empty;
    private string _loadedStyleJson = string.Empty;

    private string _heroBadge = string.Empty, _heroHeadline = string.Empty, _heroSub = string.Empty;
    private string _heroBtn1Text = string.Empty, _heroBtn1Href = string.Empty, _heroBtn2Text = string.Empty, _heroBtn2Href = string.Empty;

    private string _rtHeading = string.Empty, _rtBody = string.Empty, _rtImage = string.Empty;

    private string _featHeading = string.Empty, _featSub = string.Empty;
    private List<FeatureItemModel> _featItems = [];

    private List<StatItemModel> _statItems = [];

    private string _galHeading = string.Empty;
    private List<GalleryItemModel> _galItems = [];

    private string _faqHeading = string.Empty;
    private List<FaqItemModel> _faqItems = [];

    private string _contactHeading = string.Empty, _contactSub = string.Empty, _contactSubmit = string.Empty;
    private string _contactEmail = string.Empty, _contactPhone = string.Empty, _contactAddress = string.Empty, _contactHours = string.Empty;
    private string _contactAmapKey = "adc139d56406f3844c8f1cf1c6b65c41", _contactSecurityCode = string.Empty, _contactLng = "113.6401", _contactLat = "34.72468";

    private string _mapHeading = string.Empty, _mapSub = string.Empty, _mapTitle = string.Empty, _mapAddress = string.Empty;
    private string _mapLng = "113.6401", _mapLat = "34.72468", _mapZoom = "12", _mapHeight = "360";
    private string _mapAmapKey = "adc139d56406f3844c8f1cf1c6b65c41", _mapSecurityCode = string.Empty, _mapEmbedUrl = string.Empty;

    private string _footerBrand = string.Empty, _footerTagline = string.Empty, _footerCopy = string.Empty;

    private string _headTitle = string.Empty, _headSub = string.Empty, _headLevel = "h2", _headAlign = "left";
    private string _textBody = string.Empty, _textAlign = "left", _textSize = "1rem";
    private string _btnText = string.Empty, _btnHref = "#contact", _btnVariant = "fill", _btnAlign = "left";
    private bool _btnNewTab;
    private string _divHeight = "28", _divLabel = string.Empty;
    private bool _divShowLine = true;
    private string _htmlCode = string.Empty;
    private bool _htmlModalOpen;
    private string _imgUrl = string.Empty, _imgAlt = string.Empty, _imgCaption = string.Empty, _imgLink = string.Empty, _imgMaxH = "420";
    private bool _imgRounded = true, _imgShadow = true;
    private string _vidUrl = string.Empty, _vidPoster = string.Empty, _vidCaption = string.Empty, _vidMaxH = "460";
    private bool _vidControls = true, _vidAutoPlay, _vidLoop;
    private string _carHeight = "420", _carInterval = "5000";
    private bool _carAutoPlay = true;
    private List<SlideItemModel> _carSlides = [];
    private string _tabsHeading = string.Empty;
    private List<TabItemModel> _tabItems = [];
    private string _testiHeading = string.Empty;
    private List<TestimonialItemModel> _testiItems = [];
    private string _partHeading = string.Empty;
    private List<PartnerItemModel> _partItems = [];
    private string _priceHeading = string.Empty, _priceSub = string.Empty;
    private List<PricingPlanModel> _pricePlans = [];
    private string _artHeading = string.Empty, _artSub = string.Empty;
    private List<ArticleItemModel> _artItems = [];

    private string _styleBg = string.Empty, _styleFg = string.Empty, _stylePad = string.Empty, _styleMargin = string.Empty;
    private string _styleAlign = string.Empty, _styleRadius = string.Empty, _styleAnim = "none", _styleCss = string.Empty;
    private string _styleWidth = string.Empty, _styleHeight = string.Empty;

    protected override void OnParametersSet()
    {
        if (_loadedWidgetId == WidgetId && _loadedPropsJson == PropsJson && _loadedStyleJson == StyleJson)
            return;
        _loadedWidgetId = WidgetId;
        _loadedPropsJson = PropsJson;
        _loadedStyleJson = StyleJson;
        ParseAll();
    }

    private void ParseAll()
    {
        var style = CmsJson.DeserializeOrDefault(StyleJson, new CmsWidgetStyle());
        _styleBg = style.BackgroundColor ?? string.Empty;
        _styleFg = style.TextColor ?? string.Empty;
        _stylePad = style.Padding ?? string.Empty;
        _styleMargin = style.Margin ?? string.Empty;
        _styleAlign = style.TextAlign ?? string.Empty;
        _styleRadius = style.BorderRadius ?? string.Empty;
        _styleAnim = style.Animation ?? "none";
        _styleCss = style.CustomCssClass ?? string.Empty;
        _styleWidth = style.Width ?? string.Empty;
        _styleHeight = style.Height ?? string.Empty;

        using var doc = TryParse(PropsJson);
        var root = doc?.RootElement ?? default;

        switch (WidgetType)
        {
            case "cms.hero":
                _heroBadge = GetStr(root, "badge");
                _heroHeadline = GetStr(root, "headline");
                _heroSub = GetStr(root, "subheadline");
                _heroBtn1Text = GetStr(root, "primaryButtonText");
                _heroBtn1Href = GetStr(root, "primaryButtonHref");
                _heroBtn2Text = GetStr(root, "secondaryButtonText");
                _heroBtn2Href = GetStr(root, "secondaryButtonHref");
                break;
            case "cms.richtext":
                _rtHeading = GetStr(root, "heading");
                _rtBody = GetStr(root, "body");
                _rtImage = GetStr(root, "imageUrl");
                break;
            case "cms.features":
                _featHeading = GetStr(root, "heading");
                _featSub = GetStr(root, "subheading");
                _featItems = GetArray<FeatureItemModel>(root, "items");
                break;
            case "cms.stats":
                _statItems = GetArray<StatItemModel>(root, "items");
                break;
            case "cms.gallery":
                _galHeading = GetStr(root, "heading");
                _galItems = GetArray<GalleryItemModel>(root, "items");
                break;
            case "cms.faq":
                _faqHeading = GetStr(root, "heading");
                _faqItems = GetArray<FaqItemModel>(root, "items");
                break;
            case "cms.contact":
                _contactHeading = GetStr(root, "heading");
                _contactSub = GetStr(root, "subheading");
                _contactSubmit = GetStr(root, "submitText");
                _contactEmail = GetStr(root, "email");
                _contactPhone = GetStr(root, "phone");
                _contactAddress = GetStr(root, "address");
                _contactHours = GetStr(root, "hours");
                _contactAmapKey = GetStrOrDefault(root, "amapKey", "adc139d56406f3844c8f1cf1c6b65c41");
                _contactSecurityCode = GetStr(root, "securityJsCode");
                _contactLng = GetNumStr(root, "longitude", "113.6401");
                _contactLat = GetNumStr(root, "latitude", "34.72468");
                break;
            case "cms.map":
                _mapHeading = GetStr(root, "heading");
                _mapSub = GetStr(root, "subheading");
                _mapTitle = GetStr(root, "title");
                _mapAddress = GetStr(root, "address");
                _mapLng = GetNumStr(root, "longitude", "113.6401");
                _mapLat = GetNumStr(root, "latitude", "34.72468");
                _mapZoom = GetNumStr(root, "zoom", "12");
                _mapHeight = GetNumStr(root, "height", "360");
                _mapAmapKey = GetStrOrDefault(root, "amapKey", "adc139d56406f3844c8f1cf1c6b65c41");
                _mapSecurityCode = GetStr(root, "securityJsCode");
                _mapEmbedUrl = GetStr(root, "mapEmbedUrl");
                break;
            case "cms.footer":
                _footerBrand = GetStr(root, "brand");
                _footerTagline = GetStr(root, "tagline");
                _footerCopy = GetStr(root, "copyright");
                break;
            case "cms.heading":
                _headTitle = GetStr(root, "title");
                _headSub = GetStr(root, "subtitle");
                _headLevel = GetStrOrDefault(root, "level", "h2");
                _headAlign = GetStrOrDefault(root, "align", "left");
                break;
            case "cms.text":
                _textBody = GetStr(root, "text");
                _textAlign = GetStrOrDefault(root, "align", "left");
                _textSize = GetStrOrDefault(root, "fontSize", "1rem");
                break;
            case "cms.button":
                _btnText = GetStr(root, "text");
                _btnHref = GetStrOrDefault(root, "href", "#contact");
                _btnVariant = GetStrOrDefault(root, "variant", "fill");
                _btnAlign = GetStrOrDefault(root, "align", "left");
                _btnNewTab = GetBool(root, "openNewTab", false);
                break;
            case "cms.divider":
                _divHeight = GetNumStr(root, "height", "28");
                _divShowLine = GetBool(root, "showLine", true);
                _divLabel = GetStr(root, "label");
                break;
            case "cms.html":
                _htmlCode = GetStr(root, "html");
                break;
            case "cms.image":
                _imgUrl = GetStr(root, "imageUrl");
                _imgAlt = GetStr(root, "alt");
                _imgCaption = GetStr(root, "caption");
                _imgLink = GetStr(root, "linkUrl");
                _imgMaxH = GetNumStr(root, "maxHeight", "420");
                _imgRounded = GetBool(root, "rounded", true);
                _imgShadow = GetBool(root, "shadow", true);
                break;
            case "cms.video":
                _vidUrl = GetStr(root, "videoUrl");
                _vidPoster = GetStr(root, "posterUrl");
                _vidCaption = GetStr(root, "caption");
                _vidControls = GetBool(root, "controls", true);
                _vidAutoPlay = GetBool(root, "autoPlay", false);
                _vidLoop = GetBool(root, "loop", false);
                _vidMaxH = GetNumStr(root, "maxHeight", "460");
                break;
            case "cms.carousel":
                _carHeight = GetNumStr(root, "height", "420");
                _carAutoPlay = GetBool(root, "autoPlay", true);
                _carInterval = GetNumStr(root, "intervalMs", "5000");
                _carSlides = GetArray<SlideItemModel>(root, "slides");
                break;
            case "cms.tabs":
                _tabsHeading = GetStr(root, "heading");
                _tabItems = GetArray<TabItemModel>(root, "items");
                break;
            case "cms.testimonials":
                _testiHeading = GetStr(root, "heading");
                _testiItems = GetArray<TestimonialItemModel>(root, "items");
                break;
            case "cms.partners":
                _partHeading = GetStr(root, "heading");
                _partItems = GetArray<PartnerItemModel>(root, "items");
                break;
            case "cms.pricing":
                _priceHeading = GetStr(root, "heading");
                _priceSub = GetStr(root, "subheading");
                _pricePlans = GetArray<PricingPlanModel>(root, "plans");
                break;
            case "cms.articles":
                _artHeading = GetStr(root, "heading");
                _artSub = GetStr(root, "subheading");
                _artItems = GetArray<ArticleItemModel>(root, "items");
                break;
        }
    }

    private void EmitProps(object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(CmsJson.Options) { WriteIndented = true });
        _loadedPropsJson = json;
        _ = PropsJsonChanged.InvokeAsync(json);
    }

    private void EmitHero() => EmitProps(new
    {
        badge = _heroBadge,
        headline = _heroHeadline,
        subheadline = _heroSub,
        primaryButtonText = _heroBtn1Text,
        primaryButtonHref = _heroBtn1Href,
        secondaryButtonText = _heroBtn2Text,
        secondaryButtonHref = _heroBtn2Href
    });

    private void EmitRichText() => EmitProps(new { heading = _rtHeading, body = _rtBody, imageUrl = _rtImage });
    private void EmitFeatures() => EmitProps(new { heading = _featHeading, subheading = _featSub, items = _featItems });
    private void EmitStats() => EmitProps(new { items = _statItems });
    private void EmitGallery() => EmitProps(new { heading = _galHeading, items = _galItems });
    private void EmitFaq() => EmitProps(new { heading = _faqHeading, items = _faqItems });
    private void EmitContact() => EmitProps(new
    {
        heading = _contactHeading,
        subheading = _contactSub,
        submitText = _contactSubmit,
        email = NullIfBlank(_contactEmail),
        phone = NullIfBlank(_contactPhone),
        address = NullIfBlank(_contactAddress),
        hours = NullIfBlank(_contactHours),
        amapKey = NullIfBlank(_contactAmapKey),
        securityJsCode = NullIfBlank(_contactSecurityCode),
        longitude = ParseDouble(_contactLng, 113.6401),
        latitude = ParseDouble(_contactLat, 34.72468)
    });

    private void EmitMap() => EmitProps(new
    {
        heading = NullIfBlank(_mapHeading),
        subheading = NullIfBlank(_mapSub),
        title = NullIfBlank(_mapTitle),
        address = NullIfBlank(_mapAddress),
        longitude = ParseDouble(_mapLng, 113.6401),
        latitude = ParseDouble(_mapLat, 34.72468),
        zoom = (int)Math.Clamp(ParseDouble(_mapZoom, 12), 3, 20),
        height = (int)Math.Clamp(ParseDouble(_mapHeight, 360), 160, 640),
        amapKey = NullIfBlank(_mapAmapKey),
        securityJsCode = NullIfBlank(_mapSecurityCode),
        mapEmbedUrl = NullIfBlank(_mapEmbedUrl)
    });
    private void EmitFooter() => EmitProps(new { brand = _footerBrand, tagline = _footerTagline, copyright = _footerCopy });
    private void EmitHeading() => EmitProps(new { title = _headTitle, subtitle = NullIfBlank(_headSub), level = _headLevel, align = _headAlign });
    private void EmitText() => EmitProps(new { text = _textBody, align = _textAlign, fontSize = _textSize });
    private void EmitButton() => EmitProps(new { text = _btnText, href = _btnHref, variant = _btnVariant, align = _btnAlign, openNewTab = _btnNewTab });
    private void EmitDivider() => EmitProps(new { height = (int)Math.Clamp(ParseDouble(_divHeight, 28), 8, 120), showLine = _divShowLine, label = NullIfBlank(_divLabel) });
    private void EmitHtml() => EmitProps(new { html = _htmlCode });

    private void FormatHtmlCode()
    {
        if (string.IsNullOrWhiteSpace(_htmlCode))
            return;

        // 轻量 HTML 换行缩进整理，方便用户阅读与修改压缩后的模板片段
        var normalized = System.Text.RegularExpressions.Regex.Replace(_htmlCode.Trim(), @">\s*<", ">\n<");
        var lines = normalized.Split('\n');
        var sb = new System.Text.StringBuilder();
        var indent = 0;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("</", StringComparison.Ordinal))
                indent = Math.Max(0, indent - 1);
            sb.Append(' ', indent * 2).AppendLine(line);
            if (line.StartsWith('<') && !line.StartsWith("</", StringComparison.Ordinal) && !line.StartsWith("<!", StringComparison.Ordinal)
                && !line.EndsWith("/>", StringComparison.Ordinal) && !line.Contains("</", StringComparison.Ordinal))
            {
                indent++;
            }
        }
        _htmlCode = sb.ToString().TrimEnd();
        EmitHtml();
    }
    private void EmitImage() => EmitProps(new { imageUrl = NullIfBlank(_imgUrl), alt = NullIfBlank(_imgAlt), caption = NullIfBlank(_imgCaption), linkUrl = NullIfBlank(_imgLink), maxHeight = (int)Math.Clamp(ParseDouble(_imgMaxH, 420), 60, 1200), rounded = _imgRounded, shadow = _imgShadow });
    private void EmitVideo() => EmitProps(new { videoUrl = NullIfBlank(_vidUrl), posterUrl = NullIfBlank(_vidPoster), caption = NullIfBlank(_vidCaption), controls = _vidControls, autoPlay = _vidAutoPlay, loop = _vidLoop, maxHeight = (int)Math.Clamp(ParseDouble(_vidMaxH, 460), 180, 720) });
    private void EmitCarousel() => EmitProps(new { height = (int)Math.Clamp(ParseDouble(_carHeight, 420), 220, 680), autoPlay = _carAutoPlay, intervalMs = (int)Math.Clamp(ParseDouble(_carInterval, 5000), 2000, 15000), slides = _carSlides });
    private void EmitTabs() => EmitProps(new { heading = NullIfBlank(_tabsHeading), items = _tabItems });
    private void EmitTestimonials() => EmitProps(new { heading = NullIfBlank(_testiHeading), items = _testiItems });
    private void EmitPartners() => EmitProps(new { heading = NullIfBlank(_partHeading), items = _partItems });
    private void EmitPricing() => EmitProps(new { heading = NullIfBlank(_priceHeading), subheading = NullIfBlank(_priceSub), plans = _pricePlans });
    private void EmitArticles() => EmitProps(new { heading = NullIfBlank(_artHeading), subheading = NullIfBlank(_artSub), items = _artItems });

    private void ResetDimensions()
    {
        _styleWidth = string.Empty;
        _styleHeight = string.Empty;
        EmitStyle();
    }

    private void EmitStyle()
    {
        var style = new CmsWidgetStyle(
            BackgroundColor: NullIfBlank(_styleBg),
            TextColor: NullIfBlank(_styleFg),
            Padding: NullIfBlank(_stylePad),
            Margin: NullIfBlank(_styleMargin),
            TextAlign: NullIfBlank(_styleAlign),
            BorderRadius: NullIfBlank(_styleRadius),
            CustomCssClass: NullIfBlank(_styleCss),
            Animation: string.Equals(_styleAnim, "none", StringComparison.OrdinalIgnoreCase) ? null : NullIfBlank(_styleAnim),
            Width: NullIfBlank(_styleWidth),
            Height: NullIfBlank(_styleHeight));
        var json = JsonSerializer.Serialize(style, new JsonSerializerOptions(CmsJson.Options) { WriteIndented = true });
        _loadedStyleJson = json;
        _ = StyleJsonChanged.InvokeAsync(json);
    }

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string NormalizeHex(string? raw, string fallback)
    {
        var v = raw?.Trim();
        if (string.IsNullOrWhiteSpace(v) || !v.StartsWith('#')) return fallback;
        if (v.Length == 7) return v;
        if (v.Length == 4) return $"#{v[1]}{v[1]}{v[2]}{v[2]}{v[3]}{v[3]}";
        return fallback;
    }

    private static JsonDocument? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json); }
        catch { return null; }
    }

    private static string GetStr(JsonElement root, string prop)
    {
        if (root.ValueKind != JsonValueKind.Object) return string.Empty;
        foreach (var p in root.EnumerateObject())
        {
            if (string.Equals(p.Name, prop, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                return p.Value.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static string GetStrOrDefault(JsonElement root, string prop, string fallback)
    {
        var val = GetStr(root, prop);
        return string.IsNullOrWhiteSpace(val) ? fallback : val;
    }

    private static string GetNumStr(JsonElement root, string prop, string fallback)
    {
        if (root.ValueKind != JsonValueKind.Object) return fallback;
        foreach (var p in root.EnumerateObject())
        {
            if (string.Equals(p.Name, prop, StringComparison.OrdinalIgnoreCase))
            {
                if (p.Value.ValueKind == JsonValueKind.Number)
                    return p.Value.GetRawText();
                if (p.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.Value.GetString()))
                    return p.Value.GetString()!;
            }
        }
        return fallback;
    }

    private static double ParseDouble(string? raw, double fallback) =>
        double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var val)
            ? val
            : fallback;

    private static bool GetBool(JsonElement root, string prop, bool fallback)
    {
        if (root.ValueKind != JsonValueKind.Object) return fallback;
        foreach (var p in root.EnumerateObject())
        {
            if (string.Equals(p.Name, prop, StringComparison.OrdinalIgnoreCase) && (p.Value.ValueKind == JsonValueKind.True || p.Value.ValueKind == JsonValueKind.False))
                return p.Value.GetBoolean();
        }
        return fallback;
    }

    private static List<T> GetArray<T>(JsonElement root, string prop)
    {
        if (root.ValueKind != JsonValueKind.Object) return [];
        foreach (var p in root.EnumerateObject())
        {
            if (string.Equals(p.Name, prop, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array)
            {
                try { return JsonSerializer.Deserialize<List<T>>(p.Value.GetRawText(), CmsJson.Options) ?? []; }
                catch { return []; }
            }
        }
        return [];
    }
}
