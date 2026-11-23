using System.Text.Json;
using BlazorBoilerplate.Modules.Cms.Application.DTOs;
using BlazorBoilerplate.Modules.Cms.Domain.ValueObjects;

namespace BlazorBoilerplate.Modules.Cms.Application.AI;

/// <summary>
/// 内置行业官网模板库（手写静态模板，不经 AI/LLM）：
/// 在默认企业官网模板之外，为种子阶段提供餐饮、SaaS、教育、作品集四个示例站点。
/// 四套模板刻意采用不同的布局节奏（两栏画报 / 三栏特性墙 / 图文卡片 / 画廊侧栏），
/// 以便直观展示 CMS 的布局能力。所有 PropsJson 字段与前端各 Widget 的 Props record 一一对应。
/// </summary>
public static class CmsSiteTemplateLibrary
{
    /// <summary>一个待播种的示例站点定义。</summary>
    public sealed record SeedSite(
        string TemplateKey, string SiteKey, string Name, string Title, string Description,
        string BrandName, string IndustryZh, string IndustryEn,
        string StylePromptZh, string StylePromptEn, string PrimaryColor);

    /// <summary>种子阶段要创建的行业示例站点（默认企业官网 default 单独创建）。</summary>
    public static IReadOnlyList<SeedSite> SeedSites { get; } =
    [
        new("restaurant", "restaurant-demo", "餐饮咖啡示例站", "美味餐饮 · 官方网站",
            "餐饮咖啡行业单页官网示例（手写模板 · 两栏画报布局）", "美味餐饮", "餐饮咖啡", "Restaurant & Cafe",
            "温馨、有烟火气、突出招牌菜品与到店预订", "Warm, inviting, food-focused with reservation CTA", "#c2410c"),
        new("saas", "saas-demo", "SaaS 产品示例站", "云端产品 · 官方网站",
            "SaaS 产品单页官网示例（手写模板 · 三栏特性墙布局）", "云启科技", "企业协作 SaaS", "Enterprise SaaS",
            "现代、专业、突出核心功能与定价", "Modern, professional, feature and pricing focused", "#2563eb"),
        new("education", "education-demo", "教育培训示例站", "教育培训 · 官方网站",
            "教育培训行业单页官网示例（手写模板 · 图文卡片布局）", "启航教育", "在线教育", "Online Education",
            "专业、可信、突出课程与学员成果", "Trustworthy, professional, course and outcome focused", "#7c3aed"),
        new("portfolio", "portfolio-demo", "个人作品集示例站", "创意作品集 · 官方网站",
            "个人作品集单页官网示例（手写模板 · 画廊侧栏布局）", "林设计", "视觉设计", "Visual Design",
            "极简、有设计感、突出作品与联系", "Minimal, artistic, work and contact focused", "#0f766e")
    ];

    /// <summary>
    /// 各模板的默认入场动画序列（按区块序号轮转）。四套风格各异，进一步拉开视觉差异：
    /// 餐饮温暖上浮、SaaS 科技缩放/滑入、教育翻转渐显、作品集大幅浮现。
    /// </summary>
    private static readonly Dictionary<string, string[]> Animations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["restaurant"] = ["fade-up", "zoom-in", "slide-right", "fade-up", "blur-in", "fade-in"],
        ["saas"]       = ["zoom-in", "slide-left", "scale-up", "fade-up", "slide-right", "blur-in"],
        ["education"]  = ["fade-up", "flip", "slide-down", "zoom-in", "fade-in", "blur-in"],
        ["portfolio"]  = ["slide-up-big", "zoom-in", "fade-up", "blur-in", "scale-up", "fade-in"]
    };

    /// <summary>按模板标识与语言构建完整整站草稿（标题/主题/区块），供种子服务写入站点。</summary>
    public static CmsAiSiteDraft BuildSiteDraft(string templateKey, string brand, string industry, string? stylePrompt, string? primaryColor, bool isEn)
    {
        var sections = Build(templateKey, brand, industry, stylePrompt, isEn)
            ?? throw new InvalidOperationException($"未知官网模板标识: {templateKey}");
        sections = ApplyAnimations(templateKey, sections);
        var primary = string.IsNullOrWhiteSpace(primaryColor) ? "#0f6cbd" : primaryColor.Trim();
        return new CmsAiSiteDraft(
            Title: isEn ? $"{brand} Official Website" : $"{brand} 官方网站",
            Description: $"{brand} - {industry}",
            Theme: new CmsThemeConfig(PrimaryColor: primary, AccentColor: primary),
            Sections: sections,
            ProviderNote: "由内置手写行业模板生成（种子示例站点）。");
    }

    /// <summary>为草稿各 widget 注入入场动画 StyleJson；已显式设置的 StyleJson 不被覆盖。</summary>
    private static List<CmsAiSectionDraft> ApplyAnimations(string templateKey, List<CmsAiSectionDraft> sections)
    {
        if (!Animations.TryGetValue(templateKey, out var sequence) || sequence.Length == 0)
            return sections;

        for (var s = 0; s < sections.Count; s++)
        {
            var sec = sections[s];
            var anim = sequence[s % sequence.Length];
            var styleJson = Serialize(new { animation = anim });
            sections[s] = sec with
            {
                Widgets = sec.Widgets
                    .Select(w => string.IsNullOrWhiteSpace(w.StyleJson) ? w with { StyleJson = styleJson } : w)
                    .ToList()
            };
        }
        return sections;
    }

    /// <summary>按标识构建指定语言的整站区块草稿；未知标识返回 null。</summary>
    private static List<CmsAiSectionDraft>? Build(string template, string brand, string industry, string? stylePrompt, bool isEn) =>
        (template ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "restaurant" => isEn ? RestaurantEn(brand, industry, stylePrompt) : RestaurantZh(brand, industry, stylePrompt),
            "saas"       => isEn ? SaasEn(brand, industry, stylePrompt)       : SaasZh(brand, industry, stylePrompt),
            "education"  => isEn ? EducationEn(brand, industry, stylePrompt)  : EducationZh(brand, industry, stylePrompt),
            "portfolio"  => isEn ? PortfolioEn(brand, industry, stylePrompt)  : PortfolioZh(brand, industry, stylePrompt),
            _ => null
        };

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, CmsDtoMapper.JsonOptions);

    // ============================================================
    // 餐饮 / 咖啡 —— 两栏画报风：左文右图、图文交错
    // 布局：single-column(main) / two-column(left,right)
    // ============================================================
    private static List<CmsAiSectionDraft> RestaurantZh(string brand, string industry, string? stylePrompt) =>
    [
        new("招牌首屏", "home", true, "two-column",
        [
            new("left", "cms.hero", $"{brand} 首屏", Serialize(new
            {
                badge = industry,
                headline = $"{brand} · 一口入魂的手作{industry}",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt) ? "新鲜食材、匠心工艺，为您奉上有温度的味道。" : stylePrompt.Trim(),
                primaryButtonText = "查看菜单",
                primaryButtonHref = "#menu",
                secondaryButtonText = "立即预订",
                secondaryButtonHref = "#contact"
            })),
            new("right", "cms.image", "主厨推荐图", Serialize(new
            {
                imageUrl = "https://images.unsplash.com/photo-1517248135467-4c7edcad34c4?auto=format&fit=crop&w=1400&q=80",
                alt = $"{brand} 招牌菜品",
                caption = "当日现做 · 主厨精选",
                maxHeight = 420,
                rounded = true,
                shadow = true
            }))
        ]),
        new("人气菜单", "menu", true, "single-column",
        [
            new("main", "cms.heading", "菜单标题", Serialize(new { title = "人气招牌", subtitle = "每一道都值得专程而来", level = "h2", align = "center" })),
            new("main", "cms.pricing", "套餐价格", Serialize(new
            {
                heading = "精选套餐",
                subheading = "双人、家庭与聚会，总有一款适合你",
                plans = new[]
                {
                    new { name = "双人轻食", price = "¥128", period = "/ 套", badge = "", highlighted = false, features = new[] { "前菜两份", "主菜两道", "饮品两杯" }, buttonText = "到店品尝", buttonHref = "#contact" },
                    new { name = "主厨甄选", price = "¥268", period = "/ 套", badge = "最受欢迎", highlighted = true, features = new[] { "招牌前菜", "和牛主菜", "甜品与饮品", "专属服务" }, buttonText = "立即预订", buttonHref = "#contact" },
                    new { name = "欢聚套餐", price = "¥588", period = "/ 桌", badge = "", highlighted = false, features = new[] { "4-6 人份", "招牌全家桶", "精酿饮品", "生日布置" }, buttonText = "咨询预订", buttonHref = "#contact" }
                }
            }))
        ]),
        new("顾客好评", "reviews", true, "two-column",
        [
            new("left", "cms.testimonials", "顾客评价", Serialize(new
            {
                heading = "食客说",
                items = new[]
                {
                    new { quote = "食材新鲜、摆盘精致，服务也很贴心，已经推荐给朋友了！", author = "王女士", role = "常客", avatarUrl = "https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?auto=format&fit=crop&w=300&q=80" },
                    new { quote = "环境温馨，主厨甄选套餐性价比很高，聚会首选。", author = "李先生", role = "美食爱好者", avatarUrl = "https://images.unsplash.com/photo-1560250097-0b93528c311a?auto=format&fit=crop&w=300&q=80" }
                }
            })),
            new("right", "cms.image", "门店氛围图", Serialize(new
            {
                imageUrl = "https://images.unsplash.com/photo-1552566626-52f8b828add9?auto=format&fit=crop&w=1000&q=80",
                alt = "门店用餐环境",
                caption = "在温馨的氛围里，好好吃一顿饭",
                maxHeight = 380,
                rounded = true,
                shadow = true
            }))
        ]),
        new("到店预订", "contact", true, "single-column",
        [
            new("main", "cms.contact", "预订与地址", Serialize(new
            {
                heading = $"欢迎光临 {brand}",
                subheading = "在线留下预订信息，或按下方地址直接到店。",
                submitText = "提交预订",
                phone = "400-888-6688",
                address = "市中心商业街 88 号",
                hours = "周一至周日 10:00 - 22:00",
                mapLabel = "门店位置",
                amapKey = "adc139d56406f3844c8f1cf1c6b65c41",
                longitude = 113.6401,
                latitude = 34.72468,
                zoom = 15
            }))
        ]),
        new("页脚", "footer", false, "single-column",
        [
            new("main", "cms.footer", "页脚", Serialize(new
            {
                brand,
                tagline = $"{industry} · 用好味道连接每一个人",
                copyright = $"© {DateTime.UtcNow.Year} {brand}. All rights reserved."
            }))
        ])
    ];

    private static List<CmsAiSectionDraft> RestaurantEn(string brand, string industry, string? stylePrompt) =>
    [
        new("Hero", "home", true, "two-column",
        [
            new("left", "cms.hero", $"{brand} Hero", Serialize(new
            {
                badge = industry,
                headline = $"{brand} · Handcrafted {industry} Worth Savoring",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt) ? "Fresh ingredients, crafted with care — flavors served with warmth." : stylePrompt.Trim(),
                primaryButtonText = "View Menu",
                primaryButtonHref = "#menu",
                secondaryButtonText = "Book a Table",
                secondaryButtonHref = "#contact"
            })),
            new("right", "cms.image", "Chef's Special", Serialize(new
            {
                imageUrl = "https://images.unsplash.com/photo-1517248135467-4c7edcad34c4?auto=format&fit=crop&w=1400&q=80",
                alt = $"{brand} signature dish",
                caption = "Made fresh daily · Chef's selection",
                maxHeight = 420,
                rounded = true,
                shadow = true
            }))
        ]),
        new("Popular Menu", "menu", true, "single-column",
        [
            new("main", "cms.heading", "Menu Heading", Serialize(new { title = "Popular Dishes", subtitle = "Every plate is worth the visit", level = "h2", align = "center" })),
            new("main", "cms.pricing", "Set Menus", Serialize(new
            {
                heading = "Featured Sets",
                subheading = "For two, for family, or for a celebration",
                plans = new[]
                {
                    new { name = "Light for Two", price = "$29", period = "/ set", badge = "", highlighted = false, features = new[] { "Two appetizers", "Two mains", "Two drinks" }, buttonText = "Dine In", buttonHref = "#contact" },
                    new { name = "Chef's Choice", price = "$59", period = "/ set", badge = "Most Popular", highlighted = true, features = new[] { "Signature appetizer", "Premium main", "Dessert & drink", "Dedicated service" }, buttonText = "Book Now", buttonHref = "#contact" },
                    new { name = "Group Feast", price = "$129", period = "/ table", badge = "", highlighted = false, features = new[] { "Serves 4-6", "Signature platters", "Craft drinks", "Birthday setup" }, buttonText = "Inquire", buttonHref = "#contact" }
                }
            }))
        ]),
        new("Reviews", "reviews", true, "two-column",
        [
            new("left", "cms.testimonials", "Guest Reviews", Serialize(new
            {
                heading = "What Guests Say",
                items = new[]
                {
                    new { quote = "Fresh ingredients, beautiful plating, and warm service. Already recommended it to friends!", author = "Emily W.", role = "Regular Guest", avatarUrl = "https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?auto=format&fit=crop&w=300&q=80" },
                    new { quote = "Cozy atmosphere and great value on the Chef's Choice set — our go-to place for gatherings.", author = "Michael L.", role = "Food Lover", avatarUrl = "https://images.unsplash.com/photo-1560250097-0b93528c311a?auto=format&fit=crop&w=300&q=80" }
                }
            })),
            new("right", "cms.image", "Dining Atmosphere", Serialize(new
            {
                imageUrl = "https://images.unsplash.com/photo-1552566626-52f8b828add9?auto=format&fit=crop&w=1000&q=80",
                alt = "Dining area",
                caption = "A warm place to enjoy a great meal",
                maxHeight = 380,
                rounded = true,
                shadow = true
            }))
        ]),
        new("Visit Us", "contact", true, "single-column",
        [
            new("main", "cms.contact", "Reservation & Address", Serialize(new
            {
                heading = $"Welcome to {brand}",
                subheading = "Leave a reservation request below, or visit us at the address shown.",
                submitText = "Send Reservation",
                phone = "+1 400 888 6688",
                address = "88 Market Street, Downtown",
                hours = "Daily 10:00 - 22:00",
                mapLabel = "Find Us",
                amapKey = "adc139d56406f3844c8f1cf1c6b65c41",
                longitude = 113.6401,
                latitude = 34.72468,
                zoom = 15
            }))
        ]),
        new("Footer", "footer", false, "single-column",
        [
            new("main", "cms.footer", "Site Footer", Serialize(new
            {
                brand,
                tagline = $"{industry} · Bringing people together through great food",
                copyright = $"© {DateTime.UtcNow.Year} {brand}. All rights reserved."
            }))
        ])
    ];

    // ============================================================
    // SaaS 产品 —— 三栏特性墙：首屏宽幅 + 三栏能力 + 侧栏 FAQ
    // 布局：single-column(main) / three-column(col-1,col-2,col-3) / sidebar(sidebar,main)
    // ============================================================
    private static List<CmsAiSectionDraft> SaasZh(string brand, string industry, string? stylePrompt) =>
    [
        new("首屏", "home", true, "single-column",
        [
            new("main", "cms.hero", $"{brand} 首屏", Serialize(new
            {
                badge = "新一代" + industry + "平台",
                headline = $"{brand} · 让{industry}更简单、更高效",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt) ? "开箱即用的云端方案，几分钟上线，随时按需扩展。" : stylePrompt.Trim(),
                primaryButtonText = "免费试用",
                primaryButtonHref = "#contact",
                secondaryButtonText = "了解功能",
                secondaryButtonHref = "#features"
            })),
            new("main", "cms.stats", "关键数据", Serialize(new
            {
                items = new[]
                {
                    new { value = "10,000+", label = "活跃团队" },
                    new { value = "99.99%", label = "服务可用性" },
                    new { value = "5 分钟", label = "平均上线时间" }
                }
            }))
        ]),
        new("核心功能", "features", true, "three-column",
        [
            new("col-1", "cms.richtext", "极速上手", Serialize(new { heading = "🚀 极速上手", body = "零代码配置，几分钟完成部署与上线，让想法快速落地。" })),
            new("col-2", "cms.richtext", "企业级安全", Serialize(new { heading = "🛡️ 企业级安全", body = "细粒度权限与全链路审计，数据安全无忧。" })),
            new("col-3", "cms.richtext", "数据洞察", Serialize(new { heading = "📊 数据洞察", body = "实时报表与可视化看板，决策有据可依。" }))
        ]),
        new("定价方案", "pricing", true, "single-column",
        [
            new("main", "cms.heading", "定价标题", Serialize(new { title = "简单透明的定价", subtitle = "免费开始，随业务成长平滑升级", level = "h2", align = "center" })),
            new("main", "cms.pricing", "价格卡", Serialize(new
            {
                heading = "选择适合你的方案",
                subheading = "所有方案均含核心能力，随时可升级",
                plans = new[]
                {
                    new { name = "免费版", price = "¥0", period = "/ 永久", badge = "", highlighted = false, features = new[] { "1 个工作空间", "核心功能", "社区支持" }, buttonText = "立即开始", buttonHref = "#contact" },
                    new { name = "专业版", price = "¥199", period = "/ 月", badge = "最受欢迎", highlighted = true, features = new[] { "无限工作空间", "高级分析报表", "优先工单支持" }, buttonText = "免费试用", buttonHref = "#contact" },
                    new { name = "企业版", price = "按需报价", period = "", badge = "", highlighted = false, features = new[] { "私有化部署", "专属客户成功", "SLA 保障" }, buttonText = "联系销售", buttonHref = "#contact" }
                }
            }))
        ]),
        new("常见问题", "faq", true, "sidebar",
        [
            new("sidebar", "cms.richtext", "帮助侧栏", Serialize(new { heading = "还有问题？", body = "我们的顾问随时为你解答产品、部署与计费相关问题，欢迎随时联系我们。" })),
            new("main", "cms.faq", "FAQ", Serialize(new
            {
                heading = "常见问题",
                items = new[]
                {
                    new { question = "可以先试用再付费吗？", answer = "可以，专业版提供 14 天免费试用，无需绑定支付方式。" },
                    new { question = "是否支持数据迁移？", answer = "支持，我们提供标准导入工具与一对一迁移协助。" },
                    new { question = "如何保障数据安全？", answer = "传输与存储全程加密，并支持细粒度权限与操作审计。" }
                }
            }))
        ]),
        new("免费试用", "contact", true, "single-column",
        [
            new("main", "cms.contact", "试用申请", Serialize(new
            {
                heading = $"立即体验 {brand}",
                subheading = "留下联系方式，我们会尽快开通试用账号。",
                submitText = "申请免费试用",
                email = "hello@example.com",
                phone = "400-000-1234"
            }))
        ]),
        new("页脚", "footer", false, "single-column",
        [
            new("main", "cms.footer", "页脚", Serialize(new
            {
                brand,
                tagline = $"{industry} · 让好产品触手可及",
                copyright = $"© {DateTime.UtcNow.Year} {brand}. All rights reserved."
            }))
        ])
    ];

    private static List<CmsAiSectionDraft> SaasEn(string brand, string industry, string? stylePrompt) =>
    [
        new("Hero", "home", true, "single-column",
        [
            new("main", "cms.hero", $"{brand} Hero", Serialize(new
            {
                badge = $"Next-Gen {industry} Platform",
                headline = $"{brand} · Make {industry} Simple and Fast",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt) ? "Ready-to-use cloud solution — go live in minutes and scale on demand." : stylePrompt.Trim(),
                primaryButtonText = "Start Free Trial",
                primaryButtonHref = "#contact",
                secondaryButtonText = "Explore Features",
                secondaryButtonHref = "#features"
            })),
            new("main", "cms.stats", "Key Metrics", Serialize(new
            {
                items = new[]
                {
                    new { value = "10,000+", label = "Active Teams" },
                    new { value = "99.99%", label = "Uptime" },
                    new { value = "5 min", label = "Average Setup" }
                }
            }))
        ]),
        new("Features", "features", true, "three-column",
        [
            new("col-1", "cms.richtext", "Fast to Start", Serialize(new { heading = "🚀 Fast to Start", body = "No-code setup — deploy and go live in minutes." })),
            new("col-2", "cms.richtext", "Enterprise Security", Serialize(new { heading = "🛡️ Enterprise Security", body = "Fine-grained permissions and full audit trails." })),
            new("col-3", "cms.richtext", "Actionable Insights", Serialize(new { heading = "📊 Actionable Insights", body = "Live reports and dashboards for confident decisions." }))
        ]),
        new("Pricing", "pricing", true, "single-column",
        [
            new("main", "cms.heading", "Pricing Heading", Serialize(new { title = "Simple, Transparent Pricing", subtitle = "Start free and scale smoothly as you grow", level = "h2", align = "center" })),
            new("main", "cms.pricing", "Plans", Serialize(new
            {
                heading = "Pick the right plan",
                subheading = "All plans include core features and can be upgraded anytime",
                plans = new[]
                {
                    new { name = "Free", price = "$0", period = "/ forever", badge = "", highlighted = false, features = new[] { "1 workspace", "Core features", "Community support" }, buttonText = "Get Started", buttonHref = "#contact" },
                    new { name = "Pro", price = "$29", period = "/ month", badge = "Most Popular", highlighted = true, features = new[] { "Unlimited workspaces", "Advanced analytics", "Priority support" }, buttonText = "Start Free Trial", buttonHref = "#contact" },
                    new { name = "Enterprise", price = "Custom", period = "", badge = "", highlighted = false, features = new[] { "Private deployment", "Dedicated CSM", "SLA guarantee" }, buttonText = "Contact Sales", buttonHref = "#contact" }
                }
            }))
        ]),
        new("FAQ", "faq", true, "sidebar",
        [
            new("sidebar", "cms.richtext", "Help Sidebar", Serialize(new { heading = "Still Questions?", body = "Our advisors are ready to help with product, deployment, and billing questions — get in touch anytime." })),
            new("main", "cms.faq", "FAQ", Serialize(new
            {
                heading = "Frequently Asked Questions",
                items = new[]
                {
                    new { question = "Can I try before paying?", answer = "Yes — Pro includes a 14-day free trial with no payment details required." },
                    new { question = "Do you support data migration?", answer = "Yes, with standard import tools and one-on-one migration assistance." },
                    new { question = "How is my data secured?", answer = "Encryption in transit and at rest, plus fine-grained permissions and audit logs." }
                }
            }))
        ]),
        new("Free Trial", "contact", true, "single-column",
        [
            new("main", "cms.contact", "Trial Request", Serialize(new
            {
                heading = $"Try {brand} Today",
                subheading = "Leave your details and we'll set up your trial account shortly.",
                submitText = "Request Free Trial",
                email = "hello@example.com",
                phone = "+1 400 000 1234"
            }))
        ]),
        new("Footer", "footer", false, "single-column",
        [
            new("main", "cms.footer", "Site Footer", Serialize(new
            {
                brand,
                tagline = $"{industry} · Great products, within reach",
                copyright = $"© {DateTime.UtcNow.Year} {brand}. All rights reserved."
            }))
        ])
    ];

    // ============================================================
    // 教育培训 —— 图文卡片风：首屏宽幅 + 左图右文 + 三栏课程卡
    // 布局：single-column(main) / two-column(left,right) / three-column(col-1,col-2,col-3)
    // ============================================================
    private static List<CmsAiSectionDraft> EducationZh(string brand, string industry, string? stylePrompt) =>
    [
        new("首屏", "home", true, "single-column",
        [
            new("main", "cms.hero", $"{brand} 首屏", Serialize(new
            {
                badge = "专注" + industry,
                headline = $"{brand} · 让每一次学习都有回报",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt) ? "名师授课、体系化课程，陪你从入门到精通。" : stylePrompt.Trim(),
                primaryButtonText = "浏览课程",
                primaryButtonHref = "#courses",
                secondaryButtonText = "立即咨询",
                secondaryButtonHref = "#contact"
            })),
            new("main", "cms.stats", "教学成果", Serialize(new
            {
                items = new[]
                {
                    new { value = "50,000+", label = "累计学员" },
                    new { value = "98%", label = "课程好评率" },
                    new { value = "200+", label = "金牌讲师" }
                }
            }))
        ]),
        new("课程优势", "features", true, "two-column",
        [
            new("left", "cms.image", "学习场景图", Serialize(new
            {
                imageUrl = "https://images.unsplash.com/photo-1523050854058-8df90110c9f1?auto=format&fit=crop&w=1000&q=80",
                alt = "课堂学习场景",
                caption = "沉浸式学习，边学边练",
                maxHeight = 400,
                rounded = true,
                shadow = true
            })),
            new("right", "cms.features", "优势矩阵", Serialize(new
            {
                heading = "教学特色",
                subheading = "体系化设计，让学习更高效",
                items = new[]
                {
                    new { icon = "People", title = "名师执教", description = "行业资深讲师，实战经验倾囊相授。" },
                    new { icon = "Check", title = "体系课程", description = "由浅入深的知识路径，学完即可上手。" },
                    new { icon = "Globe", title = "灵活学习", description = "直播回放随时看，通勤碎片时间也能学。" }
                }
            }))
        ]),
        new("热门课程", "courses", true, "three-column",
        [
            new("col-1", "cms.richtext", "入门课程", Serialize(new { heading = $"{industry} 入门到实战", body = "零基础系统入门，配套实战项目与作业点评。", imageUrl = "https://images.unsplash.com/photo-1516321318423-f06f85e504b3?auto=format&fit=crop&w=800&q=80" })),
            new("col-2", "cms.richtext", "进阶课程", Serialize(new { heading = "进阶实战训练营", body = "真实业务场景驱动，30 天高强度实战陪伴。", imageUrl = "https://images.unsplash.com/photo-1555066931-4365d14bab8c?auto=format&fit=crop&w=800&q=80" })),
            new("col-3", "cms.richtext", "高阶课程", Serialize(new { heading = "架构与性能优化", body = "面向资深开发者，深入架构设计与性能调优。", imageUrl = "https://images.unsplash.com/photo-1558494949-ef010cbdcc31?auto=format&fit=crop&w=800&q=80" }))
        ]),
        new("学员评价", "reviews", true, "single-column",
        [
            new("main", "cms.heading", "评价标题", Serialize(new { title = "学员怎么说", subtitle = "真实反馈，看得见的成长", level = "h2", align = "center" })),
            new("main", "cms.testimonials", "学员评价", Serialize(new
            {
                heading = "",
                items = new[]
                {
                    new { quote = "课程体系非常清晰，跟着学完顺利转行，老师答疑也很及时！", author = "张同学", role = "结业学员", avatarUrl = "https://images.unsplash.com/photo-1534528741775-53994a69daeb?auto=format&fit=crop&w=300&q=80" },
                    new { quote = "实战项目含金量高，面试时讲出来很有底气。", author = "陈同学", role = "在职提升", avatarUrl = "https://images.unsplash.com/photo-1500648767791-00dcc994a43e?auto=format&fit=crop&w=300&q=80" }
                }
            }))
        ]),
        new("报名咨询", "contact", true, "single-column",
        [
            new("main", "cms.contact", "报名表单", Serialize(new
            {
                heading = $"加入 {brand} 开启学习",
                subheading = "留下信息，课程顾问将为你规划合适的学习路径。",
                submitText = "咨询课程",
                email = "admission@example.com",
                phone = "400-100-9000"
            }))
        ]),
        new("页脚", "footer", false, "single-column",
        [
            new("main", "cms.footer", "页脚", Serialize(new
            {
                brand,
                tagline = $"{industry} · 让成长触手可及",
                copyright = $"© {DateTime.UtcNow.Year} {brand}. All rights reserved."
            }))
        ])
    ];

    private static List<CmsAiSectionDraft> EducationEn(string brand, string industry, string? stylePrompt) =>
    [
        new("Hero", "home", true, "single-column",
        [
            new("main", "cms.hero", $"{brand} Hero", Serialize(new
            {
                badge = "Focused on " + industry,
                headline = $"{brand} · Make Every Lesson Count",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt) ? "Expert-led, structured courses that take you from beginner to pro." : stylePrompt.Trim(),
                primaryButtonText = "Browse Courses",
                primaryButtonHref = "#courses",
                secondaryButtonText = "Ask an Advisor",
                secondaryButtonHref = "#contact"
            })),
            new("main", "cms.stats", "Results", Serialize(new
            {
                items = new[]
                {
                    new { value = "50,000+", label = "Students" },
                    new { value = "98%", label = "Course Rating" },
                    new { value = "200+", label = "Expert Tutors" }
                }
            }))
        ]),
        new("Why Us", "features", true, "two-column",
        [
            new("left", "cms.image", "Learning Scene", Serialize(new
            {
                imageUrl = "https://images.unsplash.com/photo-1523050854058-8df90110c9f1?auto=format&fit=crop&w=1000&q=80",
                alt = "Classroom learning",
                caption = "Immersive learning, practice as you go",
                maxHeight = 400,
                rounded = true,
                shadow = true
            })),
            new("right", "cms.features", "Advantages", Serialize(new
            {
                heading = "Teaching Highlights",
                subheading = "Structured learning that actually sticks",
                items = new[]
                {
                    new { icon = "People", title = "Expert Tutors", description = "Industry veterans share real-world experience." },
                    new { icon = "Check", title = "Structured Paths", description = "Step-by-step curriculum you can put to use right away." },
                    new { icon = "Globe", title = "Flexible Learning", description = "Replay any lesson and learn on your own schedule." }
                }
            }))
        ]),
        new("Courses", "courses", true, "three-column",
        [
            new("col-1", "cms.richtext", "Beginner Course", Serialize(new { heading = $"{industry}: From Zero to Hero", body = "A systematic start with hands-on projects and feedback.", imageUrl = "https://images.unsplash.com/photo-1516321318423-f06f85e504b3?auto=format&fit=crop&w=800&q=80" })),
            new("col-2", "cms.richtext", "Advanced Course", Serialize(new { heading = "Advanced Bootcamp", body = "30 days of real-world, project-driven training.", imageUrl = "https://images.unsplash.com/photo-1555066931-4365d14bab8c?auto=format&fit=crop&w=800&q=80" })),
            new("col-3", "cms.richtext", "Pro Course", Serialize(new { heading = "Architecture & Performance", body = "Deep dives into architecture design and performance tuning.", imageUrl = "https://images.unsplash.com/photo-1558494949-ef010cbdcc31?auto=format&fit=crop&w=800&q=80" }))
        ]),
        new("Reviews", "reviews", true, "single-column",
        [
            new("main", "cms.heading", "Reviews Heading", Serialize(new { title = "What Students Say", subtitle = "Real feedback, visible growth", level = "h2", align = "center" })),
            new("main", "cms.testimonials", "Student Reviews", Serialize(new
            {
                heading = "",
                items = new[]
                {
                    new { quote = "The curriculum is crystal clear — I switched careers after finishing, and tutors answered every question.", author = "Zhang", role = "Graduate", avatarUrl = "https://images.unsplash.com/photo-1534528741775-53994a69daeb?auto=format&fit=crop&w=300&q=80" },
                    new { quote = "The hands-on projects are genuinely valuable — I had real confidence in interviews.", author = "Chen", role = "Upskilling Learner", avatarUrl = "https://images.unsplash.com/photo-1500648767791-00dcc994a43e?auto=format&fit=crop&w=300&q=80" }
                }
            }))
        ]),
        new("Enroll", "contact", true, "single-column",
        [
            new("main", "cms.contact", "Enrollment Form", Serialize(new
            {
                heading = $"Join {brand} Today",
                subheading = "Leave your details and an advisor will plan your learning path.",
                submitText = "Ask About Courses",
                email = "admission@example.com",
                phone = "+1 400 100 9000"
            }))
        ]),
        new("Footer", "footer", false, "single-column",
        [
            new("main", "cms.footer", "Site Footer", Serialize(new
            {
                brand,
                tagline = $"{industry} · Growth within reach",
                copyright = $"© {DateTime.UtcNow.Year} {brand}. All rights reserved."
            }))
        ])
    ];

    // ============================================================
    // 个人作品集 —— 画廊侧栏风：宽幅首屏 + 三栏画廊 + 两栏关于 + 侧栏联系
    // 布局：single-column(main) / three-column(col-1,col-2,col-3) / two-column(left,right) / sidebar(sidebar,main)
    // ============================================================
    private static List<CmsAiSectionDraft> PortfolioZh(string brand, string industry, string? stylePrompt) =>
    [
        new("首屏", "home", true, "single-column",
        [
            new("main", "cms.hero", $"{brand} 首屏", Serialize(new
            {
                badge = industry,
                headline = $"你好，我是 {brand}",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt) ? "用设计与代码，把想法变成真实可用的作品。" : stylePrompt.Trim(),
                primaryButtonText = "查看作品",
                primaryButtonHref = "#works",
                secondaryButtonText = "联系我",
                secondaryButtonHref = "#contact"
            }))
        ]),
        new("精选作品", "works", true, "three-column",
        [
            new("col-1", "cms.image", "品牌视觉设计", Serialize(new { imageUrl = "https://images.unsplash.com/photo-1558655146-d09347e92766?auto=format&fit=crop&w=800&q=80", alt = "品牌视觉设计", caption = "品牌视觉设计", maxHeight = 300, rounded = true, shadow = true })),
            new("col-2", "cms.image", "移动端产品界面", Serialize(new { imageUrl = "https://images.unsplash.com/photo-1559028012-481c04fa702d?auto=format&fit=crop&w=800&q=80", alt = "移动端产品界面", caption = "移动端产品界面", maxHeight = 300, rounded = true, shadow = true })),
            new("col-3", "cms.image", "创意视觉实验", Serialize(new { imageUrl = "https://images.unsplash.com/photo-1618005182384-a83a8bd57fbe?auto=format&fit=crop&w=800&q=80", alt = "创意视觉实验", caption = "创意视觉实验", maxHeight = 300, rounded = true, shadow = true }))
        ]),
        new("关于我", "about", true, "two-column",
        [
            new("left", "cms.richtext", "个人简介", Serialize(new
            {
                heading = $"关于 {brand}",
                body = $"专注于{industry}，热爱把复杂问题拆解成简单优雅的方案。多年的项目经验让我擅长从需求到落地的完整闭环。",
                imageUrl = "https://images.unsplash.com/photo-1535713875002-d1d0cf377fde?auto=format&fit=crop&w=1000&q=80"
            })),
            new("right", "cms.features", "服务方向", Serialize(new
            {
                heading = "我能做什么",
                subheading = "从想法到上线，全程陪伴",
                items = new[]
                {
                    new { icon = "Edit", title = "视觉设计", description = "品牌识别、界面设计与视觉规范。" },
                    new { icon = "Puzzle", title = "产品开发", description = "从原型到上线的完整实现。" },
                    new { icon = "Chart", title = "体验优化", description = "用数据与洞察打磨每一次交互。" }
                }
            }))
        ]),
        new("合作评价", "reviews", true, "single-column",
        [
            new("main", "cms.testimonials", "客户评价", Serialize(new
            {
                heading = "合作伙伴说",
                items = new[]
                {
                    new { quote = "沟通顺畅、交付专业，作品远超预期，非常推荐！", author = "王总监", role = "某科技公司市场负责人", avatarUrl = "https://images.unsplash.com/photo-1560250097-0b93528c311a?auto=format&fit=crop&w=300&q=80" },
                    new { quote = "对细节的执着让人印象深刻，是一次非常愉快的合作。", author = "李女士", role = "独立品牌主理人", avatarUrl = "https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?auto=format&fit=crop&w=300&q=80" }
                }
            }))
        ]),
        new("联系我", "contact", true, "sidebar",
        [
            new("sidebar", "cms.richtext", "联系信息", Serialize(new { heading = "联系方式", body = "邮箱：hello@example.com\n所在地：上海 · 可远程协作\n通常 1 个工作日内回复。" })),
            new("main", "cms.contact", "联系表单", Serialize(new
            {
                heading = "一起做点有意思的事",
                subheading = "有项目或想法？留下信息，我会尽快回复。",
                submitText = "发送消息",
                email = "hello@example.com"
            }))
        ]),
        new("页脚", "footer", false, "single-column",
        [
            new("main", "cms.footer", "页脚", Serialize(new
            {
                brand,
                tagline = $"{industry} · 用作品说话",
                copyright = $"© {DateTime.UtcNow.Year} {brand}. All rights reserved."
            }))
        ])
    ];

    private static List<CmsAiSectionDraft> PortfolioEn(string brand, string industry, string? stylePrompt) =>
    [
        new("Hero", "home", true, "single-column",
        [
            new("main", "cms.hero", $"{brand} Hero", Serialize(new
            {
                badge = industry,
                headline = $"Hello, I'm {brand}",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt) ? "Turning ideas into real, usable work through design and code." : stylePrompt.Trim(),
                primaryButtonText = "View Work",
                primaryButtonHref = "#works",
                secondaryButtonText = "Get in Touch",
                secondaryButtonHref = "#contact"
            }))
        ]),
        new("Selected Work", "works", true, "three-column",
        [
            new("col-1", "cms.image", "Brand Visual Identity", Serialize(new { imageUrl = "https://images.unsplash.com/photo-1558655146-d09347e92766?auto=format&fit=crop&w=800&q=80", alt = "Brand Visual Identity", caption = "Brand Visual Identity", maxHeight = 300, rounded = true, shadow = true })),
            new("col-2", "cms.image", "Mobile Product UI", Serialize(new { imageUrl = "https://images.unsplash.com/photo-1559028012-481c04fa702d?auto=format&fit=crop&w=800&q=80", alt = "Mobile Product UI", caption = "Mobile Product UI", maxHeight = 300, rounded = true, shadow = true })),
            new("col-3", "cms.image", "Creative Visual Study", Serialize(new { imageUrl = "https://images.unsplash.com/photo-1618005182384-a83a8bd57fbe?auto=format&fit=crop&w=800&q=80", alt = "Creative Visual Study", caption = "Creative Visual Study", maxHeight = 300, rounded = true, shadow = true }))
        ]),
        new("About", "about", true, "two-column",
        [
            new("left", "cms.richtext", "About Me", Serialize(new
            {
                heading = $"About {brand}",
                body = $"Specializing in {industry}, I love turning complex problems into simple, elegant solutions. Years of projects have shaped my end-to-end approach from idea to delivery.",
                imageUrl = "https://images.unsplash.com/photo-1535713875002-d1d0cf377fde?auto=format&fit=crop&w=1000&q=80"
            })),
            new("right", "cms.features", "Services", Serialize(new
            {
                heading = "What I Do",
                subheading = "From idea to launch, end to end",
                items = new[]
                {
                    new { icon = "Edit", title = "Visual Design", description = "Brand identity, UI design, and visual systems." },
                    new { icon = "Puzzle", title = "Development", description = "End-to-end delivery from prototype to launch." },
                    new { icon = "Chart", title = "UX Optimization", description = "Refining every interaction with data and insight." }
                }
            }))
        ]),
        new("Reviews", "reviews", true, "single-column",
        [
            new("main", "cms.testimonials", "Client Reviews", Serialize(new
            {
                heading = "What Partners Say",
                items = new[]
                {
                    new { quote = "Smooth communication and professional delivery — the result exceeded expectations.", author = "Director Wang", role = "Marketing Lead", avatarUrl = "https://images.unsplash.com/photo-1560250097-0b93528c311a?auto=format&fit=crop&w=300&q=80" },
                    new { quote = "The attention to detail was impressive. A truly enjoyable collaboration.", author = "Ms. Li", role = "Independent Brand Owner", avatarUrl = "https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?auto=format&fit=crop&w=300&q=80" }
                }
            }))
        ]),
        new("Contact", "contact", true, "sidebar",
        [
            new("sidebar", "cms.richtext", "Contact Info", Serialize(new { heading = "Get in Touch", body = "Email: hello@example.com\nLocation: Shanghai · Open to remote\nUsually replies within 1 business day." })),
            new("main", "cms.contact", "Contact Form", Serialize(new
            {
                heading = "Let's Make Something Great",
                subheading = "Have a project or an idea? Leave a note and I'll get back to you soon.",
                submitText = "Send Message",
                email = "hello@example.com"
            }))
        ]),
        new("Footer", "footer", false, "single-column",
        [
            new("main", "cms.footer", "Site Footer", Serialize(new
            {
                brand,
                tagline = $"{industry} · Let the work speak",
                copyright = $"© {DateTime.UtcNow.Year} {brand}. All rights reserved."
            }))
        ])
    ];
}
