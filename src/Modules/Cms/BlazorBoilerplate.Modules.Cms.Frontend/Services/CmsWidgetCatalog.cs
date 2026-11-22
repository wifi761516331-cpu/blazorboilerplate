using BlazorBoilerplate.Modules.Cms.Frontend.Components.Widgets;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Services;

public sealed record CmsWidgetDefinition(
    string Type,
    string Name,
    string Category,
    string Icon,
    string Description,
    Type ComponentType,
    string DefaultPropsJson);

/// <summary>CMS 官网微件注册中心：统一管理可用组件类型、默认属性与渲染组件映射。</summary>
public static class CmsWidgetCatalog
{
    public static IReadOnlyList<CmsWidgetDefinition> All { get; } =
    [
        new(
            "cms.hero",
            "首屏横幅 (Hero)",
            "首屏与营销",
            "Sunny",
            "主副标题、徽章与双行动号召按钮",
            typeof(CmsHeroWidget),
            """{"badge":"官方推荐","headline":"构建面向未来的数字化平台","subheadline":"高颜值、模块化、可扩展的企业官网解决方案。","primaryButtonText":"立即咨询","primaryButtonHref":"#contact","secondaryButtonText":"了解优势","secondaryButtonHref":"#features"}"""),
        new(
            "cms.features",
            "核心特性网格",
            "内容展示",
            "Grid",
            "3 列图标特性卡片矩阵",
            typeof(CmsFeaturesWidget),
            """{"heading":"核心能力","subheading":"一站式满足企业展示与业务拓展需求","items":[{"icon":"Rocket","title":"极速上线","description":"可视化拖拽编排，所见即所得发布。"},{"icon":"Shield","title":"安全可靠","description":"企业级权限隔离与全链路审计保障。"},{"icon":"Globe","title":"独立域名","description":"支持绑定企业自有域名直接访问。"}]}"""),
        new(
            "cms.richtext",
            "图文简介",
            "内容展示",
            "Document",
            "标题、正文段落与可选配图",
            typeof(CmsRichTextWidget),
            """{"heading":"关于我们","body":"专注技术创新与客户成功，为企业提供稳定可靠的数字基础设施。","imageUrl":""}"""),
        new(
            "cms.stats",
            "数据指标墙",
            "数据与信任",
            "ChartBar",
            "关键数据指标展示卡片",
            typeof(CmsStatsWidget),
            """{"items":[{"value":"99.9%","label":"服务可用性"},{"value":"500+","label":"企业客户"},{"value":"24/7","label":"技术支持"}]}"""),
        new(
            "cms.gallery",
            "媒体画廊",
            "媒体与案例",
            "Photo",
            "多图展示案例、团队或产品风采（支持从附件库选图）",
            typeof(CmsGalleryWidget),
            """{"heading":"精选案例","items":[]}"""),
        new(
            "cms.faq",
            "常见问题 (FAQ)",
            "数据与信任",
            "Info",
            "可折叠的常见问题问答列表",
            typeof(CmsFaqWidget),
            """{"heading":"常见问题","items":[{"question":"支持独立域名访问吗？","answer":"支持，通过域名路由模块即可将企业域名直接指向官网。"},{"question":"如何上传和管理图片？","answer":"直接复用平台附件管理模块，支持本地与 S3/MinIO 存储。"}]}"""),
        new(
            "cms.contact",
            "联系与留资表单",
            "转化与表单",
            "Mail",
            "访客在线提交咨询需求，自动入库并记录活动日志",
            typeof(CmsContactWidget),
            """{"heading":"联系我们","subheading":"留下您的联系方式，我们的顾问将尽快与您沟通。","submitText":"提交咨询"}"""),
        new(
            "cms.heading",
            "标题 (Heading)",
            "基础原子",
            "Edit",
            "独立标题与副标题原子组件（支持 H1/H2/H3 与对齐）",
            typeof(CmsHeadingWidget),
            """{"title":"企业数字化新体验","subtitle":"以模块化架构构建高性能门户","level":"h2","align":"left"}"""),
        new(
            "cms.text",
            "段落文本",
            "基础原子",
            "FileText",
            "纯文本段落原子组件，可自由放入任意分栏插槽",
            typeof(CmsTextWidget),
            """{"text":"支持通过可视化设计器自由编排，快速搭建符合企业品牌调性的内容区块。","align":"left","fontSize":"1rem"}"""),
        new(
            "cms.button",
            "行动按钮 (CTA)",
            "基础原子",
            "ArrowRight",
            "支持锚点跳转或外部链接的独立行动按钮",
            typeof(CmsButtonWidget),
            """{"text":"立即咨询","href":"#contact","variant":"fill","align":"left","openNewTab":false}"""),
        new(
            "cms.divider",
            "分隔线 / 留白",
            "基础原子",
            "Minus",
            "用于控制区块内部上下间距或视觉分隔",
            typeof(CmsDividerWidget),
            """{"height":28,"showLine":true,"label":""}"""),
        new(
            "cms.html",
            "自定义 HTML",
            "基础原子",
            "Code",
            "自由嵌入自定义 HTML 片段与样式布局",
            typeof(CmsHtmlWidget),
            """{"html":"<div style=\"padding:1rem;border:1px dashed #cbd5e1;border-radius:8px;\">自定义 HTML 内容区</div>"}"""),
        new(
            "cms.image",
            "单图展示",
            "媒体展示",
            "Photo",
            "独立图片组件，支持附件库选图、圆角阴影与超链接",
            typeof(CmsImageWidget),
            """{"imageUrl":"","alt":"展示图片","caption":"","linkUrl":"","maxHeight":420,"rounded":true,"shadow":true}"""),
        new(
            "cms.video",
            "视频播放",
            "媒体展示",
            "Video",
            "支持 MP4/WebM 视频播放与封面图设置",
            typeof(CmsVideoWidget),
            """{"videoUrl":"","posterUrl":"","caption":"产品演示视频","controls":true,"autoPlay":false,"loop":false,"maxHeight":460}"""),
        new(
            "cms.carousel",
            "轮播图 (Carousel)",
            "媒体展示",
            "Photo",
            "多图自动轮播与图文切换横幅",
            typeof(CmsCarouselWidget),
            """{"height":420,"autoPlay":true,"intervalMs":5000,"slides":[{"title":"新一代企业数字化门户","subtitle":"模块化热插拔 · 全链路可观测 · 多语言自适应","buttonText":"立即了解","buttonHref":"#features"},{"title":"极速交付与全栈可定制","subtitle":"可视化拖拽设计，所见即所得发布上线","buttonText":"联系我们","buttonHref":"#contact"}]}"""),
        new(
            "cms.tabs",
            "选项卡 (Tabs)",
            "交互与排版",
            "Grid",
            "多标签页内容切换，适合产品矩阵或方案对比",
            typeof(CmsTabsWidget),
            """{"heading":"行业解决方案","items":[{"title":"智能制造","content":"打通生产排程、设备联网与质量追溯全流程。"},{"title":"数字政务","content":"高安全合规架构，支持多租户与细粒度权限管控。"},{"title":"现代零售","content":"全渠道会员与订单协同，支撑高并发促销场景。"}]}"""),
        new(
            "cms.pricing",
            "定价方案",
            "商业与信任",
            "Chart",
            "产品版本与价格对比卡片，支持主推高亮",
            typeof(CmsPricingWidget),
            """{"heading":"灵活的方案定价","subheading":"按企业规模按需选择，随时平滑升级","plans":[{"name":"基础版","price":"¥1,999","period":"/ 年","highlighted":false,"features":["单站点独立部署","内置核心微件库","基础工单支持"],"buttonText":"了解详情","buttonHref":"#contact"},{"name":"企业版","price":"¥5,999","period":"/ 年","badge":"最受欢迎","highlighted":true,"features":["多站点与独立域名","AI 官网生成与高德地图","7×24 专属技术顾问"],"buttonText":"立即咨询","buttonHref":"#contact"},{"name":"旗舰定制版","price":"按需报价","period":"","highlighted":false,"features":["私有化集群与源码授权","专属模块深度定制","SLA 99.99% 保障"],"buttonText":"预约沟通","buttonHref":"#contact"}]}"""),
        new(
            "cms.testimonials",
            "客户评价",
            "商业与信任",
            "Contact",
            "展示客户真实反馈、头像与职位信息",
            typeof(CmsTestimonialsWidget),
            """{"heading":"客户信赖与评价","items":[{"quote":"上线效率非常高，可视化设计器让运营团队也能独立维护官网页面。","author":"张明","role":"某智能制造集团 CTO"},{"quote":"多语言和热插拔模块非常贴合我们的集团业务，省去了大量重复开发。","author":"李娜","role":"某数字科技公司产品总监"}]}"""),
        new(
            "cms.partners",
            "合作伙伴 Logo 墙",
            "商业与信任",
            "Globe",
            "展示生态合作伙伴与典型客户品牌标识",
            typeof(CmsPartnersWidget),
            """{"heading":"生态合作伙伴","items":[{"name":"华为云生态"},{"name":"阿里云伙伴"},{"name":"腾讯云"},{"name":"OpenTelemetry"},{"name":"PostgreSQL"},{"name":"MinIO"}]}"""),
        new(
            "cms.articles",
            "新闻动态 / 案例列表",
            "商业与信任",
            "FileText",
            "展示公司新闻、产品动态或客户案例，点击可弹出阅读全文",
            typeof(CmsArticlesWidget),
            """{"heading":"新闻动态与案例","subheading":"了解我们的最新产品发布与行业实践","items":[{"title":"BlazorBoilerplate CMS 正式支持可视化拖拽建站","category":"产品发布","date":"2026-11-22","summary":"内置 20+ 精细组件、高德地图与 AI 生成能力，助力企业快速搭建官网。","content":"内置 20+ 精细组件、高德地图与 AI 生成能力，助力企业快速搭建官网，并支持多语言一键切换与实时预览发布。"},{"title":"某智能制造集团数字化门户落地案例","category":"客户案例","date":"2026-11-18","summary":"基于模块化热插拔架构，2 周完成集团官网与多语言业务门户上线。","content":"基于模块化热插拔架构，2 周完成集团官网与多语言业务门户上线，统一了品牌形象与客户留资转化流程。"}]}"""),
        new(
            "cms.map",
            "高德地图",
            "位置与导览",
            "Globe",
            "展示公司地理位置与高德地图标记（支持配置高德 JS API Key）",
            typeof(CmsMapWidget),
            """{"heading":"公司位置","subheading":"欢迎莅临参观交流","title":"企业总部","address":"上海市浦东新区张江高科技园区","longitude":113.6401,"latitude":34.72468,"zoom":12,"height":360,"amapKey":"adc139d56406f3844c8f1cf1c6b65c41"}"""),
        new(
            "cms.footer",
            "官网页脚",
            "结构",
            "Grid",
            "版权信息、品牌简介与单页快速锚点",
            typeof(CmsFooterWidget),
            """{"brand":"企业官网","copyright":"© 2026 All rights reserved.","tagline":"Powered by BlazorBoilerplate CMS"}""")
    ];

    public static CmsWidgetDefinition? Find(string widgetType) =>
        All.FirstOrDefault(w => string.Equals(w.Type, widgetType, StringComparison.OrdinalIgnoreCase));

    public static Type Resolve(string widgetType) =>
        Find(widgetType)?.ComponentType ?? typeof(CmsRichTextWidget);
}
