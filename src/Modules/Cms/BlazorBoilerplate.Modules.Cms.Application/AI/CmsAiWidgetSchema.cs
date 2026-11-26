namespace BlazorBoilerplate.Modules.Cms.Application.AI;

/// <summary>CMS 可用微件清单（供 LLM 生成 prompt 与草稿校验共用）。与前端 CmsWidgetCatalog 保持同步：前端为渲染真相源，此处为类型/描述/示例 props。</summary>
public static class CmsAiWidgetSchema
{
    public sealed record WidgetSpec(string Type, string Name, string Description, string ExamplePropsJson);

    public static readonly WidgetSpec[] All =
    [
        new("cms.hero", "首屏横幅", "主副标题、徽章与双行动号召按钮，整站仅用一次",
            """{"badge":"行业名","headline":"品牌 · 大标题","subheadline":"一句话价值主张","primaryButtonText":"立即咨询","primaryButtonHref":"#contact","secondaryButtonText":"了解优势","secondaryButtonHref":"#features"}"""),
        new("cms.features", "核心特性网格", "3 列图标特性卡片矩阵，items 3-6 项；icon 只能用 Icons 白名单里的名称",
            """{"heading":"核心能力","subheading":"副标题","items":[{"icon":"Chart","title":"特性名","description":"一句话说明"}]}"""),
        new("cms.richtext", "图文简介", "标题、正文段落与可选配图",
            """{"heading":"关于我们","body":"2-4 句叙事段落","imageUrl":""}"""),
        new("cms.stats", "数据指标墙", "关键数据指标，items 3-4 项",
            """{"items":[{"value":"99.9%","label":"服务可用性"},{"value":"500+","label":"企业客户"}]}"""),
        new("cms.gallery", "媒体画廊", "多图展示案例/团队/产品",
            """{"heading":"精选案例","items":[]}"""),
        new("cms.faq", "常见问题", "可折叠问答列表，items 3-6 项",
            """{"heading":"常见问题","items":[{"question":"问题","answer":"回答"}]}"""),
        new("cms.contact", "联系与留资表单", "访客在线提交咨询，自动入库",
            """{"heading":"联系我们","subheading":"留下您的联系方式","submitText":"提交咨询"}"""),
        new("cms.heading", "标题", "独立标题与副标题原子组件",
            """{"title":"标题","subtitle":"副标题","level":"h2","align":"left"}"""),
        new("cms.text", "段落文本", "纯文本段落原子组件",
            """{"text":"段落内容","align":"left","fontSize":"1rem"}"""),
        new("cms.button", "行动按钮", "独立 CTA 按钮，支持锚点或外链",
            """{"text":"立即咨询","href":"#contact","variant":"fill","align":"left","openNewTab":false}"""),
        new("cms.divider", "分隔线/留白", "控制区块内部间距或视觉分隔",
            """{"height":28,"showLine":true,"label":""}"""),
        new("cms.html", "自定义 HTML", "自由嵌入 HTML 片段（谨慎使用，优先原生微件）",
            """{"html":"<div>…</div>"}"""),
        new("cms.image", "单图展示", "独立图片，支持说明与链接",
            """{"imageUrl":"https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=1200","alt":"说明","caption":"","linkUrl":"","maxHeight":420,"rounded":true,"shadow":true}"""),
        new("cms.video", "视频播放", "MP4/WebM 视频与封面",
            """{"videoUrl":"","posterUrl":"","caption":"产品演示","controls":true,"autoPlay":false,"loop":false,"maxHeight":460}"""),
        new("cms.carousel", "轮播图", "多图自动轮播横幅，slides 2-4 张",
            """{"height":420,"autoPlay":true,"intervalMs":5000,"slides":[{"imageUrl":"https://images.pexels.com/photos/3183150/pexels-photo-3183150.jpeg?auto=compress&cs=tinysrgb&w=1200","title":"轮播标题","subtitle":"轮播副标题","buttonText":"立即了解","buttonHref":"#features"}]}"""),
        new("cms.tabs", "选项卡", "多标签内容切换，items 2-4 项",
            """{"heading":"行业解决方案","items":[{"title":"标签名","content":"该标签下的描述"}]}"""),
        new("cms.pricing", "定价方案", "价格对比卡片，plans 2-3 个",
            """{"heading":"方案定价","subheading":"副标题","plans":[{"name":"基础版","price":"¥1,999","period":"/ 年","highlighted":false,"features":["特性1","特性2"],"buttonText":"了解详情","buttonHref":"#contact"}]}"""),
        new("cms.testimonials", "客户评价", "客户反馈与身份，items 2-4 条",
            """{"heading":"客户评价","items":[{"quote":"评价原文","author":"姓名","role":"职位/公司"}]}"""),
        new("cms.partners", "合作伙伴墙", "品牌标识列表，items 4-8 项",
            """{"heading":"生态合作伙伴","items":[{"name":"伙伴名"}]}"""),
        new("cms.articles", "新闻动态", "新闻/案例列表，items 2-4 条",
            """{"heading":"新闻动态","subheading":"副标题","items":[{"title":"标题","category":"分类","date":"2026-11-25","summary":"摘要","content":"正文"}]}"""),
        new("cms.map", "高德地图", "公司位置地图（key 已内置，无需提供）",
            """{"heading":"公司位置","subheading":"欢迎莅临","title":"企业总部","address":"城市+详细地址","longitude":113.6401,"latitude":34.72468,"zoom":12,"height":360}"""),
        new("cms.footer", "官网页脚", "版权、品牌简介与锚点，整站仅一个，放最后一节",
            """{"brand":"品牌名","copyright":"© 2026 All rights reserved.","tagline":"Powered by BlazorBoilerplate CMS"}""")
    ];

    /// <summary>
    /// 内部自带多列网格、需要整行宽度的微件。放进 6/8 栅格插槽会被挤压成错位布局，
    /// 因此生成阶段强制要求使用 single-column 的 main 插槽（或任何 12 栅格插槽）。
    /// </summary>
    public static readonly string[] FullWidthWidgets =
    [
        "cms.hero", "cms.features", "cms.stats", "cms.gallery", "cms.faq",
        "cms.contact", "cms.testimonials", "cms.partners", "cms.articles",
        "cms.carousel", "cms.pricing", "cms.tabs", "cms.video", "cms.footer"
    ];

    /// <summary>
    /// 特性卡片图标白名单：必须与前端 AppIcon 的内置 SVG 名称一致。
    /// AI 自由发挥写出的名字（如 Cpu / HighPerformance）渲染为空心方块。
    /// </summary>
    public static readonly string[] Icons =
    [
        "Chart", "Shield", "Check", "Globe", "Puzzle", "Grid", "People",
        "Home", "Edit", "Info", "Clock", "Mail", "Contact", "Settings",
        "Bell", "Upload", "List", "Product", "Shop", "Module"
    ];

    /// <summary>滚动入场动画类型（与前端 CmsWidgetRenderer 支持一致）。</summary>
    public static readonly string[] AnimationTypes =
    [
        "fade-up", "zoom-in", "slide-left", "scale-up", "flip",
        "bounce", "blur-in", "slide-down", "slide-up-big", "slide-right", "fade-in"
    ];
}
