using System.Text.Json;
using BlazorBoilerplate.Modules.Cms.Application.DTOs;
using BlazorBoilerplate.Modules.Cms.Domain.ValueObjects;

namespace BlazorBoilerplate.Modules.Cms.Application.AI;

/// <summary>
/// 内置行业官网模板库（手写静态模板，不经 AI/LLM）：
/// 在默认企业官网模板之外，为种子阶段提供餐饮、SaaS、教育、作品集四个示例站点。
/// 四套模板采用差异化的多栏布局节奏与丰富的微件组合（轮播、选项卡、画廊、定价、案例文章、FAQ、高德地图、留资表单等），
/// 所有 PropsJson 字段与前端各 Widget 的 Props record 一一对应。
/// </summary>
public static class CmsSiteTemplateLibrary
{
    /// <summary>一个待播种的示例站点定义。</summary>
    public sealed record SeedSite(
        string TemplateKey, string SiteKey, string Name, string Title, string Description,
        string BrandName, string IndustryZh, string IndustryEn,
        string StylePromptZh, string StylePromptEn, string PrimaryColor);

    /// <summary>所有内置只读模板的 SiteKey（默认官网 + 4 套行业示例模板）。</summary>
    public static IReadOnlySet<string> ReadOnlyTemplateKeys { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "default",
        "restaurant-demo",
        "saas-demo",
        "education-demo",
        "portfolio-demo",
        "umbraco-demo"
    };

    /// <summary>判断给定 SiteKey 是否为内置只读模板。</summary>
    public static bool IsBuiltInTemplateKey(string? siteKey) =>
        !string.IsNullOrWhiteSpace(siteKey) && ReadOnlyTemplateKeys.Contains(siteKey.Trim());

    /// <summary>种子阶段要创建的行业示例站点（默认企业官网 default 单独创建）。</summary>
    public static IReadOnlyList<SeedSite> SeedSites { get; } =
    [
        new("restaurant", "restaurant-demo", "餐饮咖啡示例站", "美味餐饮 · 官方网站",
            "餐饮咖啡行业单页官网示例（两栏画报风 · 含菜品分类、实景画廊、门店地图与在线订座）", "美味餐饮", "餐饮咖啡", "Restaurant & Cafe",
            "温馨、有烟火气、突出招牌菜品与到店预订", "Warm, inviting, food-focused with reservation CTA", "#c2410c"),
        new("saas", "saas-demo", "SaaS 产品示例站", "云端产品 · 官方网站",
            "SaaS 产品单页官网示例（三栏特性墙 · 含场景切换、生态伙伴、阶梯定价与 FAQ）", "云启科技", "企业协作 SaaS", "Enterprise SaaS",
            "现代、专业、突出核心功能与定价", "Modern, professional, feature and pricing focused", "#2563eb"),
        new("education", "education-demo", "教育培训示例站", "教育培训 · 官方网站",
            "教育培训行业单页官网示例（图文卡片与侧栏 · 含课程体系、学员成果、班型定价与试听预约）", "启航教育", "在线教育", "Online Education",
            "专业、可信、突出课程与学员成果", "Trustworthy, professional, course and outcome focused", "#7c3aed"),
        new("portfolio", "portfolio-demo", "个人作品集示例站", "创意作品集 · 官方网站",
            "个人作品集单页官网示例（画廊侧栏风 · 含三栏作品、设计复盘、服务报价与合作地图）", "林设计", "视觉设计", "Visual Design",
            "极简、有设计感、突出作品与联系", "Minimal, artistic, work and contact focused", "#0f766e"),
        new("umbraco", "umbraco-demo", "Umbraco 暖调编辑企业风", "OpenCMS · 企业级 DXP 平台",
            "复刻 Umbraco.com 暖调编辑风（#f1f0ee 暖灰画布 + #283a97 皇家蓝 + 滚动变形导航 + Logo 跑马灯 + 证言轮播）", "OpenCMS", "开源企业级 CMS", "Open-Source .NET CMS",
            "面向 AI 时代的开源企业级内容底座", "The open-source .NET CMS, ready for AI", "#283a97")
    ];

    /// <summary>
    /// 各模板的默认入场动画序列（按区块序号轮转）。
    /// </summary>
    private static readonly Dictionary<string, string[]> Animations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["restaurant"] = ["fade-up", "zoom-in", "slide-right", "bounce", "blur-in", "slide-left", "flip", "scale-up", "slide-down", "slide-up-big", "fade-in"],
        ["saas"]       = ["zoom-in", "slide-left", "scale-up", "flip", "fade-up", "slide-right", "blur-in", "bounce", "slide-down", "slide-up-big", "fade-in"],
        ["education"]  = ["fade-up", "zoom-in", "flip", "slide-down", "bounce", "slide-right", "scale-up", "blur-in", "slide-up-big", "slide-left", "fade-in"],
        ["portfolio"]  = ["slide-up-big", "zoom-in", "blur-in", "scale-up", "flip", "slide-left", "bounce", "slide-down", "fade-up", "slide-right", "fade-in"],
        ["umbraco"]    = ["fade-up", "fade-in", "zoom-in", "slide-left", "scale-up", "blur-in", "slide-right", "flip", "bounce", "fade-up", "fade-in"]
    };

    /// <summary>按模板标识与语言构建完整整站草稿（标题/主题/区块），供种子服务写入站点。</summary>
    public static CmsAiSiteDraft BuildSiteDraft(string templateKey, string brand, string industry, string? stylePrompt, string? primaryColor, bool isEn)
    {
        var sections = Build(templateKey, brand, industry, stylePrompt, isEn)
            ?? throw new InvalidOperationException($"未知官网模板标识: {templateKey}");
        sections = ApplyAnimations(templateKey, sections);
        var primary = string.IsNullOrWhiteSpace(primaryColor) ? "#0f6cbd" : primaryColor.Trim();
        var theme = string.Equals(templateKey, "umbraco", StringComparison.OrdinalIgnoreCase)
            ? new CmsThemeConfig(
                PrimaryColor: "#283a97",
                AccentColor: "#f5c1bc",
                BackgroundColor: "#f1f0ee",
                SurfaceColor: "#f9f7f4",
                TextColor: "#162335",
                FontFamily: "'Lato', system-ui, -apple-system, 'Segoe UI', Arial, sans-serif",
                BorderRadius: "20px",
                StylePreset: "umbraco-editorial")
            : new CmsThemeConfig(PrimaryColor: primary, AccentColor: primary);

        return new CmsAiSiteDraft(
            Title: isEn ? $"{brand} Official Website" : $"{brand} 官方网站",
            Description: $"{brand} - {industry}",
            Theme: theme,
            Sections: sections,
            ProviderNote: "由内置手写行业模板生成（种子示例站点）。");
    }

    /// <summary>
    /// 为草稿各 widget 注入差异化入场动画 StyleJson（按区块布局与插槽位置分配方向感动画，已显式设置的不覆盖）：
    /// - two-column / sidebar：左/侧栏从左滑入（slide-right），右/主栏从右滑入（slide-left），形成对向呼应；
    /// - three-column：三列依次采用左滑、上浮/缩放、右滑的节奏；
    /// - single-column：按模板专属动画序列逐个轮转。
    /// </summary>
    private static List<CmsAiSectionDraft> ApplyAnimations(string templateKey, List<CmsAiSectionDraft> sections)
    {
        if (!Animations.TryGetValue(templateKey, out var sequence) || sequence.Length == 0)
            return sections;

        for (var s = 0; s < sections.Count; s++)
        {
            var sec = sections[s];
            var baseAnim = sequence[s % sequence.Length];
            var updatedWidgets = new List<CmsAiWidgetDraft>(sec.Widgets.Count);

            for (var wIdx = 0; wIdx < sec.Widgets.Count; wIdx++)
            {
                var w = sec.Widgets[wIdx];
                if (!string.IsNullOrWhiteSpace(w.StyleJson))
                {
                    updatedWidgets.Add(w);
                    continue;
                }

                var anim = sec.LayoutPreset switch
                {
                    "two-column" or "sidebar" => w.ZoneId is "left" or "sidebar"
                        ? "slide-right"
                        : (wIdx == 0 ? "slide-left" : sequence[(s + wIdx) % sequence.Length]),
                    "three-column" => w.ZoneId switch
                    {
                        "col-1" => "slide-right",
                        "col-2" => baseAnim is "slide-left" or "slide-right" ? "zoom-in" : baseAnim,
                        "col-3" => "slide-left",
                        _ => baseAnim
                    },
                    _ => wIdx == 0 ? baseAnim : sequence[(s + wIdx) % sequence.Length]
                };

                updatedWidgets.Add(w with { StyleJson = Serialize(new { animation = anim }) });
            }

            sections[s] = sec with { Widgets = updatedWidgets };
        }
        return sections;
    }

    private static List<CmsAiSectionDraft>? Build(string template, string brand, string industry, string? stylePrompt, bool isEn) =>
        (template ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "restaurant" => isEn ? RestaurantEn(brand, industry, stylePrompt) : RestaurantZh(brand, industry, stylePrompt),
            "saas"       => isEn ? SaasEn(brand, industry, stylePrompt)       : SaasZh(brand, industry, stylePrompt),
            "education"  => isEn ? EducationEn(brand, industry, stylePrompt)  : EducationZh(brand, industry, stylePrompt),
            "portfolio"  => isEn ? PortfolioEn(brand, industry, stylePrompt)  : PortfolioZh(brand, industry, stylePrompt),
            "umbraco"    => isEn ? UmbracoEn(brand, industry, stylePrompt)    : UmbracoZh(brand, industry, stylePrompt),
            _ => null
        };

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, CmsDtoMapper.JsonOptions);

    // ============================================================
    // 1. 餐饮 / 咖啡 —— 两栏画报风 + 轮播 + 菜品选项卡 + 画廊 + 门店地图
    // ============================================================
    private static List<CmsAiSectionDraft> RestaurantZh(string brand, string industry, string? stylePrompt) =>
    [
        new("招牌首屏", "home", true, "two-column",
        [
            new("left", "cms.hero", $"{brand} 首屏", Serialize(new
            {
                badge = $"{industry} · 城市慢生活地标",
                headline = $"{brand} · 一口入魂的手作{industry}",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt) ? "严选当季有机食材、72 小时低温慢烤与现磨精品咖啡，为您奉上有温度的味觉体验。" : stylePrompt.Trim(),
                primaryButtonText = "探索招牌菜单",
                primaryButtonHref = "#menu",
                secondaryButtonText = "立即预约席位",
                secondaryButtonHref = "#contact"
            })),
            new("left", "cms.stats", "门店口碑数据", Serialize(new
            {
                items = new[]
                {
                    new { value = "4.9★", label = "大众点评年度口碑" },
                    new { value = "100%", label = "每日凌晨现采购食材" },
                    new { value = "12 年", label = "主厨团队匠心沉淀" }
                }
            })),
            new("right", "cms.image", "主厨推荐图", Serialize(new
            {
                imageUrl = "https://images.pexels.com/photos/262978/pexels-photo-262978.jpeg?auto=compress&cs=tinysrgb&w=1200",
                alt = $"{brand} 餐厅环境与招牌菜品",
                caption = "主厨每日限量出品 · 现点现做",
                maxHeight = 460,
                rounded = true,
                shadow = true
            }))
        ]),
        new("空间视觉", "story", true, "single-column",
        [
            new("main", "cms.carousel", "门店实景轮播", Serialize(new
            {
                height = 420,
                autoPlay = true,
                intervalMs = 4500,
                slides = new[]
                {
                    new { imageUrl = "https://images.pexels.com/photos/1267320/pexels-photo-1267320.jpeg?auto=compress&cs=tinysrgb&w=1200", title = "温暖光影 · 城市会客厅", subtitle = "原木与暖调灯光交织，适合朋友小聚、商务简餐与周末早午餐", buttonText = "查看菜单", buttonHref = "#menu" },
                    new { imageUrl = "https://images.pexels.com/photos/302899/pexels-photo-302899.jpeg?auto=compress&cs=tinysrgb&w=1200", title = "埃塞俄比亚手冲系列", subtitle = "自有烘焙工坊每周新鲜烘焙，呈现明亮花果香与焦糖尾韵", buttonText = "预约席位", buttonHref = "#contact" },
                    new { imageUrl = "https://images.pexels.com/photos/769289/pexels-photo-769289.jpeg?auto=compress&cs=tinysrgb&w=1200", title = "主厨炭烤牛排与季节小食", subtitle = "果木明火锁住肉汁，搭配手工调制黑松露海盐酱汁", buttonText = "到店品鉴", buttonHref = "#contact" }
                }
            }))
        ]),
        new("人气菜单", "menu", true, "single-column",
        [
            new("main", "cms.heading", "菜单标题", Serialize(new { title = "人气招牌与赏味套餐", subtitle = "遵循四时风味变化，每一道都值得专程而来", level = "h2", align = "center" })),
            new("main", "cms.tabs", "分类菜单浏览", Serialize(new
            {
                heading = "分时段精选菜单",
                items = new[]
                {
                    new { title = "主厨招牌正餐", content = "低温慢烤安格斯战斧牛排、黑松露奶油意面、地中海香草烤春鸡与主厨时蔬浓汤，选用本地农场直供鲜蔬。", imageUrl = "https://images.pexels.com/photos/769289/pexels-photo-769289.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "精品手冲与特调", content = "耶加雪菲日晒手冲、海盐焦糖拿铁、冷萃青柠气泡美式，每杯均由 SCA 认证咖啡师现磨萃取。", imageUrl = "https://images.pexels.com/photos/302899/pexels-photo-302899.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "手工烘焙甜品", content = "法式焦糖布蕾、宇治抹茶千层、每日现烤海盐可颂与季节限定莓果塔，甜而不腻。", imageUrl = "https://images.pexels.com/photos/1854652/pexels-photo-1854652.jpeg?auto=compress&cs=tinysrgb&w=900" }
                }
            })),
            new("main", "cms.divider", "间距分隔", Serialize(new { height = 24, showLine = false, label = "" })),
            new("main", "cms.pricing", "套餐价格", Serialize(new
            {
                heading = "热销赏味套餐",
                subheading = "免服务费，提前在线预订可享主厨迎宾甜品一份",
                plans = new[]
                {
                    new { name = "午间轻食单人餐", price = "¥78", period = "/ 份", badge = "工作日推荐", highlighted = false, features = new[] { "香草烤鸡沙拉 / 意面二选一", "当日主厨例汤 1 份", "现磨美式或冰红茶 1 杯" }, buttonText = "预订此套餐", buttonHref = "#contact" },
                    new { name = "主厨双人赏味宴", price = "¥298", period = "/ 套", badge = "人气首选", highlighted = true, features = new[] { "低温慢烤牛排主菜 1 份", "黑松露菌菇意面 + 凯撒沙拉", "手作提拉米苏 + 特调饮品 2 杯" }, buttonText = "立即预订", buttonHref = "#contact" },
                    new { name = "欢聚四人分享餐", price = "¥568", period = "/ 套", badge = "聚会超值", highlighted = false, features = new[] { "精选双拼主菜 + 海鲜拼盘", "4 款开胃前菜与手工面包篮", "4 杯特调饮品或精选餐酒" }, buttonText = "预约包间", buttonHref = "#contact" }
                }
            }))
        ]),
        new("匠心风味", "craft", true, "three-column",
        [
            new("col-1", "cms.richtext", "本地有机农场", Serialize(new
            {
                heading = "🌿 农场直采鲜蔬",
                body = "与本地生态农场建立每日清晨直供通道，确保每一份沙拉与配菜都保持脆嫩原味。",
                imageUrl = "https://images.pexels.com/photos/1640777/pexels-photo-1640777.jpeg?auto=compress&cs=tinysrgb&w=900"
            })),
            new("col-2", "cms.richtext", "自有烘焙工坊", Serialize(new
            {
                heading = "☕ 小批次鲜烘豆",
                body = "每周小批次烘焙埃塞俄比亚与哥伦比亚单品豆，黄金养豆期出品，香气层次饱满。",
                imageUrl = "https://images.pexels.com/photos/302899/pexels-photo-302899.jpeg?auto=compress&cs=tinysrgb&w=900"
            })),
            new("col-3", "cms.richtext", "手作酸种面包", Serialize(new
            {
                heading = "🥐 72 小时冷发酵",
                body = "天然鲁邦菌种历经 72 小时低温慢发酵，外皮酥脆麦香浓郁，内里气孔均匀湿润。",
                imageUrl = "https://images.pexels.com/photos/376464/pexels-photo-376464.jpeg?auto=compress&cs=tinysrgb&w=900"
            }))
        ]),
        new("风味画廊", "gallery", true, "single-column",
        [
            new("main", "cms.gallery", "实景与菜品画廊", Serialize(new
            {
                heading = "舌尖与光影瞬间",
                items = new[]
                {
                    new { url = "https://images.pexels.com/photos/1640777/pexels-photo-1640777.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "主厨季节拼盘" },
                    new { url = "https://images.pexels.com/photos/1126728/pexels-photo-1126728.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "柑橘冷萃特调" },
                    new { url = "https://images.pexels.com/photos/769289/pexels-photo-769289.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "窑烤意式薄饼" },
                    new { url = "https://images.pexels.com/photos/376464/pexels-photo-376464.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "周末早午餐拼盘" },
                    new { url = "https://images.pexels.com/photos/1267320/pexels-photo-1267320.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "傍晚微醺吧台" },
                    new { url = "https://images.pexels.com/photos/262978/pexels-photo-262978.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "仪式感晚宴摆盘" }
                }
            }))
        ]),
        new("食客口碑", "reviews", true, "two-column",
        [
            new("left", "cms.testimonials", "顾客评价", Serialize(new
            {
                heading = "来自食客的真实好评",
                items = new[]
                {
                    new { quote = "菜品火候极佳，双人套餐分量很足，靠窗位置拍照非常出片，已经带朋友来过三次了！", author = "林小姐", role = "美食专栏博主", avatarUrl = "https://images.pexels.com/photos/415829/pexels-photo-415829.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "手冲咖啡的风味很干净，甜品甜度恰到好处，周末来这里看书办公特别惬意。", author = "陈先生", role = "常客会员", avatarUrl = "https://images.pexels.com/photos/614810/pexels-photo-614810.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "团队聚餐提前订了包间，服务员响应及时，还贴心准备了生日甜品牌。", author = "周女士", role = "企业行政负责人", avatarUrl = "https://images.pexels.com/photos/774909/pexels-photo-774909.jpeg?auto=compress&cs=tinysrgb&w=300" }
                }
            })),
            new("right", "cms.articles", "门店动态与探店手记", Serialize(new
            {
                heading = "主厨手记与上新",
                subheading = "点击查看季节菜单故事",
                items = new[]
                {
                    new { title = "冬季黑松露与炭烤季菜单上线", category = "新品上市", date = "2026-11-20", summary = "严选云南当季新鲜黑松露，搭配 28 天干式熟成谷饲牛排。", content = "每年 11 月至次年 2 月是黑松露风味最浓郁的季节。主厨团队以低温黄油慢煎牛排后现场刨片，香气与油脂交融，欢迎预约品鉴。" },
                    new { title = "周末手冲咖啡品鉴沙龙报名开启", category = "门店活动", date = "2026-11-15", summary = "每周六下午 14:30，与首席烘焙师一起品鉴 4 款产区单品豆。", content = "现场将演示水温、研磨度与注水节奏对风味的影响，参与者均可获赠 100g 挂耳伴手礼一份。" }
                }
            }))
        ]),
        new("主厨团队", "chefs", true, "two-column",
        [
            new("left", "cms.image", "主厨风采", Serialize(new
            {
                imageUrl = "https://images.pexels.com/photos/1267320/pexels-photo-1267320.jpeg?auto=compress&cs=tinysrgb&w=900",
                alt = "主厨团队",
                caption = "行政总厨与精品咖啡烘焙团队",
                maxHeight = 360,
                rounded = true,
                shadow = true
            })),
            new("right", "cms.richtext", "主厨理念", Serialize(new
            {
                heading = "把四季风味装进每一道菜",
                body = "行政总厨拥有 12 年法餐与现代融合料理经验，坚持“少即是多”的调味哲学，拒绝过度加工，只用海盐、现磨黑椒与天然高汤唤醒食材本味。"
            })),
            new("right", "cms.features", "门店服务特色", Serialize(new
            {
                heading = "贴心用餐体验",
                items = new[]
                {
                    new { icon = "Check", title = "过敏原标注", description = "全菜单清晰标注坚果、乳制品与麸质信息。" },
                    new { icon = "Heart", title = "宠物友好露台", description = "户外花园席位欢迎携带爱宠同行。" },
                    new { icon = "Star", title = "会员积分礼遇", description = "消费积分可兑换手冲咖啡与生日定制甜品。" },
                    new { icon = "Clock", title = "极速外带自提", description = "线上提前下单，到店免排队即取即走。" },
                    new { icon = "Shield", title = "明厨亮灶溯源", description = "全透明开放式厨房，食材批次每日公示。" },
                    new { icon = "Globe", title = "环保可降解包装", description = "外带杯具与餐盒 100% 采用可降解环保材质。" }
                }
            }))
        ]),
        new("用餐问答", "faq", true, "sidebar",
        [
            new("sidebar", "cms.html", "会员卡说明", Serialize(new
            {
                html = "<div style=\"padding:1.1rem;border-radius:10px;background:linear-gradient(135deg,rgba(194,65,12,0.12),rgba(194,65,12,0.03));border:1px solid rgba(194,65,12,0.2);\"><strong style=\"font-size:1rem;\">🎁 黑卡会员首单礼</strong><p style=\"margin:0.5rem 0 0;font-size:0.88rem;opacity:0.85;\">首次到店注册会员即赠手冲咖啡体验券 1 张，生日当月享整桌 88 折。</p></div>"
            })),
            new("sidebar", "cms.button", "会员预约按钮", Serialize(new { text = "立即预约席位", href = "#contact", variant = "fill", align = "left", openNewTab = false })),
            new("main", "cms.faq", "到店常见问答", Serialize(new
            {
                heading = "到店用餐常见问题",
                items = new[]
                {
                    new { question = "周末早午餐需要提前多久预约？", answer = "建议提前 1–2 天通过下方表单或电话预约，靠窗景观位每日保留 60% 给线上预约客人。" },
                    new { question = "是否提供素食或无麸质菜品？", answer = "提供。我们有专门的时蔬能量碗、菌菇黑松露意面及无麸质杏仁蛋糕，下单时告知服务员即可。" },
                    new { question = "门店附近方便停车吗？", answer = "门店旁地下车库提供充足车位，消费满 ¥200 可在前台领取 2 小时免费停车券。" }
                }
            }))
        ]),
        new("厨房实拍", "kitchen-video", true, "two-column",
        [
            new("left", "cms.video", "后厨工艺短片", Serialize(new
            {
                videoUrl = "https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4",
                posterUrl = "https://images.pexels.com/photos/1267320/pexels-photo-1267320.jpeg?auto=compress&cs=tinysrgb&w=900",
                caption = "每日现烤面包与手冲萃取实录",
                controls = true,
                autoPlay = false,
                loop = false,
                maxHeight = 280
            })),
            new("right", "cms.richtext", "每日出炉时刻表", Serialize(new
            {
                heading = "跟着香气出炉的时间来",
                body = "08:30 第一炉海盐可颂与酸种欧包出炉；11:30 炭烤牛排与意面开餐；14:30 精品手冲与千层下午茶供应；18:00 晚宴餐酒开启。"
            })),
            new("right", "cms.button", "查看赏味套餐", Serialize(new { text = "浏览精选套餐", href = "#menu", variant = "outline", align = "left", openNewTab = false }))
        ]),
        new("定制服务", "events", true, "three-column",
        [
            new("col-1", "cms.richtext", "私人晚宴定制", Serialize(new { heading = "🥂 私人晚宴与求婚包场", body = "可容纳 12–30 人独立花园包间，主厨可根据宾客口味定制专属 5 道式位上菜单。" })),
            new("col-2", "cms.richtext", "企业茶歇外烩", Serialize(new { heading = "🧁 商务茶歇与外烩配送", body = "为发布会、沙龙与企业年会提供现磨手冲咖啡壶与手作咸甜点拼盘上门布置。" })),
            new("col-3", "cms.richtext", "节日伴手礼盒", Serialize(new { heading = "🎁 挂耳咖啡与曲奇礼盒", body = "提供企业 Logo 烫金定制礼盒，包含 12 枚当季烘焙挂耳包与手工黄油曲奇。" }))
        ]),
        new("媒体背书", "press", true, "single-column",
        [
            new("main", "cms.partners", "榜单与媒体推荐", Serialize(new
            {
                heading = "权威美食榜单与媒体推荐",
                items = new[]
                {
                    new { name = "大众点评必吃榜" },
                    new { name = "黑珍珠入围餐厅" },
                    new { name = "TimeOut 推荐" },
                    new { name = "小红书人气打卡" },
                    new { name = "SCA 精品咖啡认证" },
                    new { name = "城市生活周刊" }
                }
            })),
            new("main", "cms.html", "品牌发展时间轴", Serialize(new
            {
                html = "<div style=\"display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:1rem;margin-top:0.5rem;\"><div style=\"padding:1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(0,0,0,0.08);\"><strong style=\"color:var(--cms-pri,#c2410c);\">2014 · 初心启程</strong><div style=\"font-size:0.88rem;opacity:0.8;margin-top:4px;\">第一家社区手工烘焙与咖啡馆在静安老洋房开业。</div></div><div style=\"padding:1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(0,0,0,0.08);\"><strong style=\"color:var(--cms-pri,#c2410c);\">2019 · 自建工坊</strong><div style=\"font-size:0.88rem;opacity:0.8;margin-top:4px;\">成立独立精品咖啡烘焙工坊与有机农场直供基地。</div></div><div style=\"padding:1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(0,0,0,0.08);\"><strong style=\"color:var(--cms-pri,#c2410c);\">2026 · 全新旗舰</strong><div style=\"font-size:0.88rem;opacity:0.8;margin-top:4px;\">升级全日制 Bistro 餐酒空间，累计服务超 50 万食客。</div></div></div>"
            }))
        ]),
        new("到店预订", "contact", true, "sidebar",
        [
            new("sidebar", "cms.richtext", "门店营业信息", Serialize(new
            {
                heading = "营业时间与门店信息",
                body = "📍 地址：上海市静安区愚园路 888 号\n🕒 营业时间：周一至周日 09:00 - 22:30\n📞 订座热线：021-8888-6688\n🚇 地铁：2/11 号线江苏路站 4 号口步行 5 分钟"
            })),
            new("sidebar", "cms.html", "包间提示", Serialize(new
            {
                html = "<div style=\"padding:1rem;border-left:4px solid var(--cms-pri,#c2410c);background:rgba(194,65,12,0.06);border-radius:8px;font-size:0.9rem;\"><strong>🎉 私人包间与团建包场</strong><div style=\"margin-top:4px;opacity:0.85;\">支持 8–30 人小型沙龙、生日派对与企业团建定制菜单，请至少提前 1 天留言预约。</div></div>"
            })),
            new("main", "cms.map", "门店导航地图", Serialize(new
            {
                heading = "到店位置导航",
                subheading = "支持高德地图一键定位门店",
                title = $"{brand} · 静安旗舰店",
                address = "上海市静安区愚园路 888 号",
                longitude = 121.4345,
                latitude = 31.2235,
                zoom = 14,
                height = 300,
                amapKey = "adc139d56406f3844c8f1cf1c6b65c41"
            })),
            new("main", "cms.contact", "在线订座表单", Serialize(new
            {
                heading = "在线预约席位",
                subheading = "填写用餐时间、人数与联系方式，门店管家将在 15 分钟内短信确认",
                submitText = "立即提交预约",
                email = "reserve@example.com",
                phone = "021-8888-6688",
                address = "上海市静安区愚园路 888 号"
            }))
        ]),
        new("页脚", "footer", false, "single-column",
        [
            new("main", "cms.footer", "页脚", Serialize(new
            {
                brand,
                tagline = $"手工烘焙 · 精选风味 · {industry}",
                copyright = $"© {DateTime.UtcNow.Year} {brand}. All rights reserved."
            }))
        ])
    ];

    private static List<CmsAiSectionDraft> RestaurantEn(string brand, string industry, string? stylePrompt) =>
    [
        new("Home", "home", true, "two-column",
        [
            new("left", "cms.hero", $"{brand} Hero", Serialize(new
            {
                badge = $"{industry} · Neighborhood Table",
                headline = $"{brand} · Handcrafted {industry}",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt) ? "Seasonal organic produce, 72-hour sourdough, and freshly roasted single-origin coffee made with warmth." : stylePrompt.Trim(),
                primaryButtonText = "Explore Menu",
                primaryButtonHref = "#menu",
                secondaryButtonText = "Book a Table",
                secondaryButtonHref = "#contact"
            })),
            new("left", "cms.stats", "Highlights", Serialize(new
            {
                items = new[]
                {
                    new { value = "4.9★", label = "Guest Rating" },
                    new { value = "100%", label = "Fresh Daily Sourcing" },
                    new { value = "12 Yrs", label = "Culinary Heritage" }
                }
            })),
            new("right", "cms.image", "Chef's Special", Serialize(new
            {
                imageUrl = "https://images.pexels.com/photos/262978/pexels-photo-262978.jpeg?auto=compress&cs=tinysrgb&w=1200",
                alt = $"{brand} Signature Dish",
                caption = "Prepared fresh to order every day",
                maxHeight = 460,
                rounded = true,
                shadow = true
            }))
        ]),
        new("Ambiance", "story", true, "single-column",
        [
            new("main", "cms.carousel", "Dining Space", Serialize(new
            {
                height = 420,
                autoPlay = true,
                intervalMs = 4500,
                slides = new[]
                {
                    new { imageUrl = "https://images.pexels.com/photos/1267320/pexels-photo-1267320.jpeg?auto=compress&cs=tinysrgb&w=1200", title = "Warm Neighborhood Dining Room", subtitle = "Designed for unhurried brunches, intimate dinners, and casual catch-ups", buttonText = "View Menu", buttonHref = "#menu" },
                    new { imageUrl = "https://images.pexels.com/photos/302899/pexels-photo-302899.jpeg?auto=compress&cs=tinysrgb&w=1200", title = "In-House Micro Roastery", subtitle = "Weekly small-batch roasts with bright floral notes and velvety crema", buttonText = "Reserve", buttonHref = "#contact" },
                    new { imageUrl = "https://images.pexels.com/photos/769289/pexels-photo-769289.jpeg?auto=compress&cs=tinysrgb&w=1200", title = "Wood-Fired Signatures", subtitle = "Seared over fruitwood embers with black truffle sea salt jus", buttonText = "Book Now", buttonHref = "#contact" }
                }
            }))
        ]),
        new("Menu", "menu", true, "single-column",
        [
            new("main", "cms.heading", "Menu Heading", Serialize(new { title = "Popular Dishes & Tasting Sets", subtitle = "Seasonal menus crafted fresh every morning", level = "h2", align = "center" })),
            new("main", "cms.tabs", "Menu Categories", Serialize(new
            {
                heading = "Browse by Category",
                items = new[]
                {
                    new { title = "Signature Mains", content = "Slow-roasted Angus tomahawk, black truffle tagliatelle, herb-roasted spring chicken, and velvety seasonal velouté.", imageUrl = "https://images.pexels.com/photos/769289/pexels-photo-769289.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "Specialty Coffee", content = "Yirgacheffe pour-over, sea-salt caramel latte, and cold-brew yuzu tonic crafted by SCA-certified baristas.", imageUrl = "https://images.pexels.com/photos/302899/pexels-photo-302899.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "Artisan Pastries", content = "French crème brûlée, matcha mille-crêpe, flaky sea-salt croissants, and seasonal berry tarts.", imageUrl = "https://images.pexels.com/photos/1854652/pexels-photo-1854652.jpeg?auto=compress&cs=tinysrgb&w=900" }
                }
            })),
            new("main", "cms.divider", "Spacer", Serialize(new { height = 24, showLine = false, label = "" })),
            new("main", "cms.pricing", "Set Menus", Serialize(new
            {
                heading = "Curated Set Menus",
                subheading = "No service charge — online bookings include a complimentary welcome dessert",
                plans = new[]
                {
                    new { name = "Weekday Lunch", price = "$15", period = "/ guest", badge = "Lunch", highlighted = false, features = new[] { "Herb chicken salad or pasta", "Soup of the day", "Americano or iced tea" }, buttonText = "Book Lunch", buttonHref = "#contact" },
                    new { name = "Chef's Tasting for Two", price = "$48", period = "/ set", badge = "Most Popular", highlighted = true, features = new[] { "Slow-roasted prime steak", "Truffle mushroom pasta + salad", "Tiramisu + 2 signature drinks" }, buttonText = "Book Now", buttonHref = "#contact" },
                    new { name = "Feast for Four", price = "$89", period = "/ set", badge = "Group Value", highlighted = false, features = new[] { "Dual main platters + seafood", "4 starters & sourdough basket", "4 craft drinks or house wines" }, buttonText = "Reserve Table", buttonHref = "#contact" }
                }
            }))
        ]),
        new("Craft", "craft", true, "three-column",
        [
            new("col-1", "cms.richtext", "Farm Direct", Serialize(new { heading = "🌿 Organic Produce", body = "Direct dawn deliveries from local partner farms keep every salad crisp and vibrant.", imageUrl = "https://images.pexels.com/photos/1640777/pexels-photo-1640777.jpeg?auto=compress&cs=tinysrgb&w=900" })),
            new("col-2", "cms.richtext", "Fresh Roasting", Serialize(new { heading = "☕ Micro-Batch Roasts", body = "Weekly small-batch roasting of single-origin beans for peak sweetness and clarity.", imageUrl = "https://images.pexels.com/photos/302899/pexels-photo-302899.jpeg?auto=compress&cs=tinysrgb&w=900" })),
            new("col-3", "cms.richtext", "Sourdough", Serialize(new { heading = "🥐 72h Fermentation", body = "Wild levain fermented for 72 hours for a blistered crust and tender, airy crumb.", imageUrl = "https://images.pexels.com/photos/376464/pexels-photo-376464.jpeg?auto=compress&cs=tinysrgb&w=900" }))
        ]),
        new("Gallery", "gallery", true, "single-column",
        [
            new("main", "cms.gallery", "Culinary Gallery", Serialize(new
            {
                heading = "Flavors & Moments",
                items = new[]
                {
                    new { url = "https://images.pexels.com/photos/1640777/pexels-photo-1640777.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Seasonal Tasting Platter" },
                    new { url = "https://images.pexels.com/photos/1126728/pexels-photo-1126728.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Citrus Cold Brew" },
                    new { url = "https://images.pexels.com/photos/769289/pexels-photo-769289.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Wood-Fired Flatbread" },
                    new { url = "https://images.pexels.com/photos/376464/pexels-photo-376464.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Weekend Brunch" },
                    new { url = "https://images.pexels.com/photos/1267320/pexels-photo-1267320.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Evening Bar" },
                    new { url = "https://images.pexels.com/photos/262978/pexels-photo-262978.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Evening Plating" }
                }
            }))
        ]),
        new("Reviews", "reviews", true, "two-column",
        [
            new("left", "cms.testimonials", "Guest Reviews", Serialize(new
            {
                heading = "What Our Guests Say",
                items = new[]
                {
                    new { quote = "Spot-on flavors, generous portions for two, and a sun-lit window table we can't get enough of.", author = "Ms. Lin", role = "Food Writer", avatarUrl = "https://images.pexels.com/photos/415829/pexels-photo-415829.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "Clean pour-overs and balanced pastries — my go-to weekend spot to read and unwind.", author = "Mr. Chen", role = "Regular Guest", avatarUrl = "https://images.pexels.com/photos/614810/pexels-photo-614810.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "Booked a private room for our team dinner — attentive service and a thoughtful birthday touch.", author = "Ms. Zhou", role = "Operations Lead", avatarUrl = "https://images.pexels.com/photos/774909/pexels-photo-774909.jpeg?auto=compress&cs=tinysrgb&w=300" }
                }
            })),
            new("right", "cms.articles", "Kitchen Notes", Serialize(new
            {
                heading = "Seasonal Stories & Events",
                subheading = "Click to read the full story",
                items = new[]
                {
                    new { title = "Winter Black Truffle & Charcoal Menu", category = "New Menu", date = "2026-11-20", summary = "Fresh Yunnan black truffles paired with 28-day dry-aged beef.", content = "Available November through February, shaved tableside over butter-basted prime cuts." },
                    new { title = "Weekend Pour-Over Tasting Salon", category = "Event", date = "2026-11-15", summary = "Every Saturday at 2:30 PM with our head roaster.", content = "Explore water temperature, grind size, and extraction rhythm across four single-origin beans." }
                }
            }))
        ]),
        new("Our Chefs", "chefs", true, "two-column",
        [
            new("left", "cms.image", "Culinary Team", Serialize(new
            {
                imageUrl = "https://images.pexels.com/photos/1267320/pexels-photo-1267320.jpeg?auto=compress&cs=tinysrgb&w=900",
                alt = "Culinary Team",
                caption = "Executive Chef & Roasting Team",
                maxHeight = 360,
                rounded = true,
                shadow = true
            })),
            new("right", "cms.richtext", "Culinary Philosophy", Serialize(new
            {
                heading = "Four Seasons on Every Plate",
                body = "With 12 years in French and contemporary fusion kitchens, our Executive Chef lets peak-season ingredients shine with sea salt, cracked pepper, and slow-simmered stocks."
            })),
            new("right", "cms.features", "Dining Perks", Serialize(new
            {
                heading = "Thoughtful Hospitality",
                items = new[]
                {
                    new { icon = "Check", title = "Allergen Clarity", description = "Clear nut, dairy, and gluten labels across every dish." },
                    new { icon = "Heart", title = "Pet-Friendly Patio", description = "Sunny garden tables welcome furry companions." },
                    new { icon = "Star", title = "Member Rewards", description = "Earn points toward pour-overs and birthday desserts." }
                }
            }))
        ]),
        new("Dining FAQ", "faq", true, "sidebar",
        [
            new("sidebar", "cms.html", "Membership Perk", Serialize(new
            {
                html = "<div style=\"padding:1.1rem;border-radius:10px;background:linear-gradient(135deg,rgba(194,65,12,0.12),rgba(194,65,12,0.03));border:1px solid rgba(194,65,12,0.2);\"><strong style=\"font-size:1rem;\">🎁 First-Visit Member Gift</strong><p style=\"margin:0.5rem 0 0;font-size:0.88rem;opacity:0.85;\">Join our table club for a complimentary pour-over and 12% off during your birthday month.</p></div>"
            })),
            new("sidebar", "cms.button", "Book Button", Serialize(new { text = "Reserve a Table", href = "#contact", variant = "fill", align = "left", openNewTab = false })),
            new("main", "cms.faq", "Guest FAQ", Serialize(new
            {
                heading = "Frequently Asked Questions",
                items = new[]
                {
                    new { question = "How far in advance should we book weekend brunch?", answer = "We recommend booking 1–2 days ahead; 60% of window tables are reserved for online bookings." },
                    new { question = "Do you offer vegan or gluten-free options?", answer = "Yes — seasonal grain bowls, truffle mushroom pasta, and flourless almond cakes are available daily." },
                    new { question = "Is parking available nearby?", answer = "Underground parking is next door, with 2 hours validated for tables over $30." }
                }
            }))
        ]),
        new("Kitchen Film", "kitchen-video", true, "two-column",
        [
            new("left", "cms.video", "Kitchen Craft Film", Serialize(new
            {
                videoUrl = "https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4",
                posterUrl = "https://images.pexels.com/photos/1267320/pexels-photo-1267320.jpeg?auto=compress&cs=tinysrgb&w=900",
                caption = "Daily Baking & Pour-Over Craft",
                controls = true,
                autoPlay = false,
                loop = false,
                maxHeight = 280
            })),
            new("right", "cms.richtext", "Daily Oven Schedule", Serialize(new
            {
                heading = "Time Your Visit with Fresh Batches",
                body = "08:30 First sourdough & croissants; 11:30 Wood-fired lunch service; 14:30 Single-origin afternoon tea; 18:00 Evening bistro & natural wine."
            })),
            new("right", "cms.button", "View Menu CTA", Serialize(new { text = "Explore Tasting Sets", href = "#menu", variant = "outline", align = "left", openNewTab = false }))
        ]),
        new("Catering", "events", true, "three-column",
        [
            new("col-1", "cms.richtext", "Private Dining", Serialize(new { heading = "🥂 Private Dinners & Celebrations", body = "Garden dining room for 12–30 guests with custom 5-course tasting menus." })),
            new("col-2", "cms.richtext", "Corporate Catering", Serialize(new { heading = "🧁 Office Tea & Event Catering", body = "Freshly brewed coffee dispensers and artisan pastry platters delivered to your venue." })),
            new("col-3", "cms.richtext", "Gift Boxes", Serialize(new { heading = "🎁 Curated Gift Sets", body = "Custom-branded drip coffee bags and butter shortbread tins for seasonal gifting." }))
        ]),
        new("Press & Awards", "press", true, "single-column",
        [
            new("main", "cms.partners", "Featured In", Serialize(new
            {
                heading = "Featured Guides & Culinary Awards",
                items = new[]
                {
                    new { name = "Must-Eat Guide" },
                    new { name = "Black Pearl Nominee" },
                    new { name = "TimeOut Pick" },
                    new { name = "City Weekend" },
                    new { name = "SCA Certified" },
                    new { name = "Local Roasters Guild" }
                }
            })),
            new("main", "cms.html", "Brand Timeline", Serialize(new
            {
                html = "<div style=\"display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:1rem;margin-top:0.5rem;\"><div style=\"padding:1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(0,0,0,0.08);\"><strong style=\"color:var(--cms-pri,#c2410c);\">2014 · First Bakery</strong><div style=\"font-size:0.88rem;opacity:0.8;margin-top:4px;\">Opened our first neighborhood sourdough &amp; coffee corner.</div></div><div style=\"padding:1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(0,0,0,0.08);\"><strong style=\"color:var(--cms-pri,#c2410c);\">2019 · Roastery</strong><div style=\"font-size:0.88rem;opacity:0.8;margin-top:4px;\">Launched our micro-roastery and direct farm-to-table sourcing.</div></div><div style=\"padding:1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(0,0,0,0.08);\"><strong style=\"color:var(--cms-pri,#c2410c);\">2026 · Flagship</strong><div style=\"font-size:0.88rem;opacity:0.8;margin-top:4px;\">Expanded into an all-day bistro serving 500K+ guests.</div></div></div>"
            }))
        ]),
        new("Visit Us", "contact", true, "sidebar",
        [
            new("sidebar", "cms.richtext", "Hours & Location", Serialize(new
            {
                heading = "Opening Hours & Location",
                body = "📍 888 Yuyuan Rd, Jing'an, Shanghai\n🕒 Mon–Sun 09:00 – 22:30\n📞 +86 21-8888-6688\n🚇 Metro Line 2/11 Jiangsu Rd Exit 4"
            })),
            new("sidebar", "cms.html", "Private Dining Note", Serialize(new
            {
                html = "<div style=\"padding:1rem;border-left:4px solid var(--cms-pri,#c2410c);background:rgba(194,65,12,0.06);border-radius:8px;font-size:0.9rem;\"><strong>🎉 Private Dining &amp; Events</strong><div style=\"margin-top:4px;opacity:0.85;\">Custom menus for 8–30 guests. Please reserve at least 1 day in advance.</div></div>"
            })),
            new("main", "cms.map", "Location Map", Serialize(new
            {
                heading = "Find Us on the Map",
                subheading = "Interactive Amap location",
                title = $"{brand} Flagship",
                address = "888 Yuyuan Rd, Jing'an, Shanghai",
                longitude = 121.4345,
                latitude = 31.2235,
                zoom = 14,
                height = 300,
                amapKey = "adc139d56406f3844c8f1cf1c6b65c41"
            })),
            new("main", "cms.contact", "Reservation Form", Serialize(new
            {
                heading = "Book a Table",
                subheading = "Share your preferred time and party size — we'll confirm within 15 minutes",
                submitText = "Request Reservation",
                email = "reserve@example.com",
                phone = "021-8888-6688",
                address = "888 Yuyuan Rd, Jing'an, Shanghai"
            }))
        ]),
        new("Footer", "footer", false, "single-column",
        [
            new("main", "cms.footer", "Site Footer", Serialize(new
            {
                brand,
                tagline = $"Freshly baked · Handcrafted flavors · {industry}",
                copyright = $"© {DateTime.UtcNow.Year} {brand}. All rights reserved."
            }))
        ])
    ];

    // ============================================================
    // 2. 企业协作 SaaS —— 三栏特性墙 + 场景选项卡 + 定价 + 案例 + 侧栏 FAQ
    // ============================================================
    private static List<CmsAiSectionDraft> SaasZh(string brand, string industry, string? stylePrompt) =>
    [
        new("首屏", "home", true, "single-column",
        [
            new("main", "cms.hero", $"{brand} 首屏", Serialize(new
            {
                badge = $"新一代 {industry}",
                headline = $"{brand}，让团队协作与业务运转快人一步",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt) ? "集项目协同、自动化工作流、实时 BI 看板与企业级权限审计于一体，助力高成长团队高效交付。" : stylePrompt.Trim(),
                primaryButtonText = "免费试用 14 天",
                primaryButtonHref = "#contact",
                secondaryButtonText = "查看方案定价",
                secondaryButtonHref = "#pricing"
            })),
            new("main", "cms.stats", "核心信任指标", Serialize(new
            {
                items = new[]
                {
                    new { value = "99.99%", label = "金融级 SLA 可用性" },
                    new { value = "12,000+", label = "活跃企业团队" },
                    new { value = "45%", label = "平均交付效率提升" },
                    new { value = "ISO27001", label = "信息安全合规认证" }
                }
            })),
            new("main", "cms.partners", "生态客户墙", Serialize(new
            {
                heading = "深受领先数字化团队信赖",
                items = new[]
                {
                    new { name = "华为云生态" },
                    new { name = "阿里云伙伴" },
                    new { name = "腾讯云" },
                    new { name = "OpenTelemetry" },
                    new { name = "PostgreSQL" },
                    new { name = "MinIO" }
                }
            }))
        ]),
        new("核心能力", "features", true, "three-column",
        [
            new("col-1", "cms.richtext", "极速上手", Serialize(new
            {
                heading = "🚀 开箱即用的敏捷空间",
                body = "内置 40+ 行业工作流模板，5 分钟完成组织架构同步与权限配置，零学习成本即可全员上线。",
                imageUrl = "https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900"
            })),
            new("col-2", "cms.richtext", "企业级安全", Serialize(new
            {
                heading = "🛡️ 零信任安全与审计",
                body = "支持 SSO 单点登录、多租户物理/逻辑隔离、字段级脱敏与全链路操作审计日志。",
                imageUrl = "https://images.pexels.com/photos/577585/pexels-photo-577585.jpeg?auto=compress&cs=tinysrgb&w=900"
            })),
            new("col-3", "cms.richtext", "智能洞察", Serialize(new
            {
                heading = "📊 实时 BI 与风险预警",
                body = "自动汇聚跨项目工时、燃尽图与交付健康度，异常阻塞自动推送企微/钉钉提醒。",
                imageUrl = "https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900"
            }))
        ]),
        new("场景方案", "solutions", true, "single-column",
        [
            new("main", "cms.tabs", "典型业务场景", Serialize(new
            {
                heading = "覆盖企业全生命周期协同场景",
                items = new[]
                {
                    new { title = "研发敏捷协同", content = "打通需求池、Sprint 迭代、Git 代码提交与自动化测试追踪，研发进度透明可控。", imageUrl = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "跨部门 OKR 对齐", content = "从公司级战略目标逐层拆解至团队与个人 KR，每周自动汇总复盘进度与置信度。", imageUrl = "https://images.pexels.com/photos/3184328/pexels-photo-3184328.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "无代码自动化审批", content = "可视化拖拽编排触发器与审批节点，合同、采购与发布变更流转效率提升 3 倍。", imageUrl = "https://images.pexels.com/photos/1181675/pexels-photo-1181675.jpeg?auto=compress&cs=tinysrgb&w=900" }
                }
            })),
            new("main", "cms.divider", "间距", Serialize(new { height = 24, showLine = false, label = "" })),
            new("main", "cms.features", "技术底座优势", Serialize(new
            {
                heading = "全栈云原生能力矩阵",
                subheading = "为高并发与私有化定制而生",
                items = new[]
                {
                    new { icon = "Globe", title = "多语言与多时区", description = "原生支持中英文切换与跨国团队时区自动转换。" },
                    new { icon = "Puzzle", title = "开放 Webhook & API", description = "提供完整 OpenAPI 与事件总线，轻松对接 ERP/CRM。" },
                    new { icon = "Shield", title = "私有化集群交付", description = "支持 Docker / Kubernetes 一键部署至企业内网。" },
                    new { icon = "Rocket", title = "毫秒级实时协同", description = "基于 WebSocket 增量同步，多人同时编辑零冲突。" },
                    new { icon = "Chart", title = "自定义效能大盘", description = "拖拽式仪表盘自由组合研发速率与缺陷趋势图。" },
                    new { icon = "Check", title = "细粒度权限矩阵", description = "支持按部门、项目、角色与字段级控制可见范围。" }
                }
            }))
        ]),
        new("产品视图", "product-views", true, "single-column",
        [
            new("main", "cms.gallery", "产品工作台实景", Serialize(new
            {
                heading = "所见即所得的工作台体验",
                items = new[]
                {
                    new { url = "https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "实时效能与燃尽图 BI 看板" },
                    new { url = "https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "多项目组合资源排期视图" },
                    new { url = "https://images.pexels.com/photos/577585/pexels-photo-577585.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "自动化规则与审计日志中心" },
                    new { url = "https://images.pexels.com/photos/3183150/pexels-photo-3183150.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "跨部门实时协同文档" }
                }
            }))
        ]),
        new("安全与架构", "security", true, "two-column",
        [
            new("left", "cms.richtext", "企业级合规架构", Serialize(new
            {
                heading = "金融级数据隔离与全链路可观测",
                body = "基于多租户隔离架构与 OpenTelemetry 链路追踪构建，支持国密传输加密、细粒度 RBAC 权限矩阵以及异地多活灾备。",
                imageUrl = "https://images.pexels.com/photos/1181675/pexels-photo-1181675.jpeg?auto=compress&cs=tinysrgb&w=900"
            })),
            new("right", "cms.html", "安全合规徽章", Serialize(new
            {
                html = "<div style=\"padding:1.25rem;border-radius:10px;border:1px solid rgba(37,99,235,0.22);background:rgba(37,99,235,0.05);\"><strong>🔐 权威安全合规保障</strong><ul style=\"margin:0.65rem 0 0;padding-left:1.2rem;line-height:1.8;\"><li>ISO/IEC 27001 信息安全管理体系认证</li><li>国家信息安全等级保护三级备案</li><li>全量操作审计日志不可篡改归档</li><li>支持企业自建 KMS 密钥托管</li></ul></div>"
            })),
            new("right", "cms.button", "下载安全白皮书", Serialize(new { text = "申请获取安全架构白皮书", href = "#contact", variant = "outline", align = "left", openNewTab = false }))
        ]),
        new("演示视频", "demo-video", true, "two-column",
        [
            new("left", "cms.video", "2分钟产品演示", Serialize(new
            {
                videoUrl = "https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4",
                posterUrl = "https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900",
                caption = "2 分钟了解云启科技全流程敏捷协同",
                controls = true,
                autoPlay = false,
                loop = false,
                maxHeight = 280
            })),
            new("right", "cms.richtext", "为什么领先团队选择切换", Serialize(new
            {
                heading = "告别工具碎片化，在一个工作台完成交付",
                body = "不再需要在需求文档、看板工具、工时表与 BI 报表之间反复切换。所有上下文自动关联，让工程师专注创造，管理者专注决策。"
            })),
            new("right", "cms.button", "立即申请试用", Serialize(new { text = "开通 14 天全功能试用", href = "#contact", variant = "fill", align = "left", openNewTab = false }))
        ]),
        new("方案对比", "comparison", true, "single-column",
        [
            new("main", "cms.heading", "代际优势对比", Serialize(new { title = "传统协同工具 vs 云启科技平台", subtitle = "从工具拼凑升级为一体化数字工作底座", level = "h2", align = "center" })),
            new("main", "cms.html", "能力对比卡片", Serialize(new
            {
                html = "<div style=\"display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:1.25rem;\"><div style=\"padding:1.25rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(0,0,0,0.08);\"><strong>传统多工具拼凑</strong><ul style=\"margin:0.65rem 0 0;padding-left:1.2rem;opacity:0.78;line-height:1.8;\"><li>数据分散在 5+ 个系统，无法联动统计</li><li>权限配置粗放，跨部门协作存在泄露风险</li><li>仅支持公有云 SaaS，难以满足私有化合规</li></ul></div><div style=\"padding:1.25rem;border-radius:10px;background:rgba(37,99,235,0.06);border:1px solid rgba(37,99,235,0.28);\"><strong style=\"color:var(--cms-pri,#2563eb);\">✨ 云启科技一体化平台</strong><ul style=\"margin:0.65rem 0 0;padding-left:1.2rem;line-height:1.8;\"><li>需求、研发、测试、OKR 与 BI 全链路打通</li><li>零信任 RBAC + 字段级脱敏 + 不可变审计</li><li>支持 K8s 私有化集群与源码级热插拔扩展</li></ul></div></div>"
            }))
        ]),
        new("集成生态", "integrations", true, "three-column",
        [
            new("col-1", "cms.richtext", "研发工具链集成", Serialize(new
            {
                heading = "🔗 Git & CI/CD 自动联动",
                body = "原生对接 GitHub、GitLab、Gitee 与 Jenkins，提交 PR 或流水线构建结果自动同步至任务卡片。"
            })),
            new("col-2", "cms.richtext", "即时通讯通知", Serialize(new
            {
                heading = "💬 企微 / 钉钉 / 飞书直连",
                body = "支持在聊天群内一键创建待办、审批流程与接收阻塞预警，无需频繁切换系统。"
            })),
            new("col-3", "cms.richtext", "企业身份与财务", Serialize(new
            {
                heading = "🏢 AD / LDAP & ERP 同步",
                body = "组织架构与离职权限实时同步，项目工时与成本核算数据一键导出至财务 ERP。"
            }))
        ]),
        new("定价方案", "pricing", true, "single-column",
        [
            new("main", "cms.heading", "定价标题", Serialize(new { title = "简单透明的订阅方案", subtitle = "免费起步，随团队规模增长随时平滑升级", level = "h2", align = "center" })),
            new("main", "cms.pricing", "价格卡", Serialize(new
            {
                heading = "选择适合您的版本",
                subheading = "年付立享 8 折优惠 · 支持对公转账与增值税专用发票",
                plans = new[]
                {
                    new { name = "初创免费版", price = "¥0", period = "/ 永久", badge = "10人以内", highlighted = false, features = new[] { "最多 10 名团队成员", "无限项目看板与任务", "基础权限与 5GB 附件存储" }, buttonText = "立即免费开启", buttonHref = "#contact" },
                    new { name = "团队专业版", price = "¥99", period = "/ 人 / 月", badge = "最受欢迎", highlighted = true, features = new[] { "不限成员与自定义工作流", "OKR 对齐与高级 BI 报表", "SSO 单点登录与 7×12 顾问支持" }, buttonText = "免费试用 14 天", buttonHref = "#contact" },
                    new { name = "企业私有化版", price = "按需定制", period = "", badge = "源码 / 内网", highlighted = false, features = new[] { "私有云 / 本地化集群部署", "99.99% SLA 与专属架构师", "定制开发与源码级扩展能力" }, buttonText = "预约架构师沟通", buttonHref = "#contact" }
                }
            }))
        ]),
        new("客户案例", "cases", true, "two-column",
        [
            new("left", "cms.articles", "客户成功案例", Serialize(new
            {
                heading = "标杆客户实践",
                subheading = "点击阅读完整数字化落地复盘",
                items = new[]
                {
                    new { title = "某新能源汽车供应链集团：800 人研发协同上线实录", category = "智能制造", date = "2026-11-18", summary = "2 周完成多基地研发任务与质量缺陷闭环打通，发布周期缩短 35%。", content = "通过模块化权限隔离与自定义看板，该集团实现了跨 4 个城市研发中心的统一迭代排期与问题追溯，审计合规一次通过。" },
                    new { title = "某头部金融科技公司：零信任私有化门户架构升级", category = "金融科技", date = "2026-11-10", summary = "全链路 OpenTelemetry 可观测性接入，核心接口 P99 延迟下降 42%。", content = "借助云启科技的 K8s 私有化发行版与统一审计底座，满足了等保三级与内部内控审计的严格要求。" }
                }
            })),
            new("right", "cms.testimonials", "客户高管证言", Serialize(new
            {
                heading = "一线管理者的真实反馈",
                items = new[]
                {
                    new { quote = "上线两周后，周会同步时间减少了一半，所有项目风险都在看板上一目了然。", author = "张总", role = "某智造集团研发 VP", avatarUrl = "https://images.pexels.com/photos/220453/pexels-photo-220453.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "私有化部署极其丝滑，还为我们保留了二次开发与模块热插拔的能力，非常省心。", author = "李总监", role = "某金融科技架构负责人", avatarUrl = "https://images.pexels.com/photos/774909/pexels-photo-774909.jpeg?auto=compress&cs=tinysrgb&w=300" }
                }
            }))
        ]),
        new("常见问题", "faq", true, "sidebar",
        [
            new("sidebar", "cms.richtext", "帮助侧栏", Serialize(new
            {
                heading = "还有选型疑问？",
                body = "我们的产品架构师可为您提供 1 对 1 业务诊断、POC 沙箱环境搭建与数据迁移评估。"
            })),
            new("sidebar", "cms.button", "预约演示按钮", Serialize(new { text = "预约 1v1 产品演示", href = "#contact", variant = "fill", align = "left", openNewTab = false })),
            new("main", "cms.faq", "FAQ", Serialize(new
            {
                heading = "常见选型与部署问题",
                items = new[]
                {
                    new { question = "支持从 Jira / 飞书 / 钉钉迁移历史数据吗？", answer = "支持。我们提供一键导入工具和结构化 CSV/JSON 迁移脚本，通常 1 个工作日内即可完成平滑切换。" },
                    new { question = "私有化部署对服务器和数据库有什么要求？", answer = "支持 SQLite、PostgreSQL、SQL Server 与 MySQL，提供标准 Docker 镜像与 Helm Chart，4C8G 起即可运行。" },
                    new { question = "试用期间数据会被保留吗？", answer = "会的。14 天免费试用期间创建的全部项目、流程与报表数据，在正式订阅后将无缝保留。" },
                    new { question = "是否支持多语言界面与独立域名访问？", answer = "支持。内置中英文双语切换，并支持将客户门户或文档中心绑定到企业自有域名。" }
                }
            }))
        ]),
        new("免费试用", "contact", true, "single-column",
        [
            new("main", "cms.contact", "试用申请", Serialize(new
            {
                heading = "申请免费试用 / 私有化评估",
                subheading = "留下您的企业信息，专属解决方案顾问将在 30 分钟内为您开通体验环境",
                submitText = "立即获取试用账号",
                email = "sales@example.com",
                phone = "400-800-9000"
            }))
        ]),
        new("页脚", "footer", false, "single-column",
        [
            new("main", "cms.footer", "页脚", Serialize(new
            {
                brand,
                tagline = $"{industry} · 安全 · 敏捷 · 可扩展",
                copyright = $"© {DateTime.UtcNow.Year} {brand}. All rights reserved."
            }))
        ])
    ];

    private static List<CmsAiSectionDraft> SaasEn(string brand, string industry, string? stylePrompt) =>
    [
        new("Home", "home", true, "single-column",
        [
            new("main", "cms.hero", $"{brand} Hero", Serialize(new
            {
                badge = $"Next-Gen {industry}",
                headline = $"{brand}, Built for Teams That Ship Fast",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt) ? "Unify project delivery, automated workflows, real-time BI dashboards, and enterprise audit trails in one modular platform." : stylePrompt.Trim(),
                primaryButtonText = "Start 14-Day Trial",
                primaryButtonHref = "#contact",
                secondaryButtonText = "View Pricing",
                secondaryButtonHref = "#pricing"
            })),
            new("main", "cms.stats", "Key Metrics", Serialize(new
            {
                items = new[]
                {
                    new { value = "99.99%", label = "Enterprise SLA" },
                    new { value = "12,000+", label = "Active Teams" },
                    new { value = "45%", label = "Faster Delivery" },
                    new { value = "ISO27001", label = "Security Certified" }
                }
            })),
            new("main", "cms.partners", "Trusted By", Serialize(new
            {
                heading = "Trusted by Modern Engineering & Product Teams",
                items = new[]
                {
                    new { name = "Huawei Cloud" },
                    new { name = "Alibaba Cloud" },
                    new { name = "Tencent Cloud" },
                    new { name = "OpenTelemetry" },
                    new { name = "PostgreSQL" },
                    new { name = "MinIO" }
                }
            }))
        ]),
        new("Features", "features", true, "three-column",
        [
            new("col-1", "cms.richtext", "Fast to Start", Serialize(new { heading = "🚀 Ready in Minutes", body = "40+ built-in workflow templates let your organization onboard and configure permissions in under 5 minutes.", imageUrl = "https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900" })),
            new("col-2", "cms.richtext", "Enterprise Security", Serialize(new { heading = "🛡️ Zero-Trust Security", body = "SSO, multi-tenant isolation, field-level masking, and immutable audit logs built in from day one.", imageUrl = "https://images.pexels.com/photos/577585/pexels-photo-577585.jpeg?auto=compress&cs=tinysrgb&w=900" })),
            new("col-3", "cms.richtext", "Actionable Insights", Serialize(new { heading = "📊 Real-Time BI", body = "Live velocity charts, workload heatmaps, and automated bottleneck alerts keep delivery on track.", imageUrl = "https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900" }))
        ]),
        new("Solutions", "solutions", true, "single-column",
        [
            new("main", "cms.tabs", "Use Cases", Serialize(new
            {
                heading = "One Platform for Every Workflow",
                items = new[]
                {
                    new { title = "Agile Engineering", content = "Connect backlogs, sprints, Git commits, and QA tracking with full end-to-end traceability.", imageUrl = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "Company-Wide OKRs", content = "Cascade strategic goals down to team key results with automated weekly check-ins.", imageUrl = "https://images.pexels.com/photos/3184328/pexels-photo-3184328.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "No-Code Approvals", content = "Drag-and-drop triggers and approval chains speed up procurement, contracts, and release sign-offs.", imageUrl = "https://images.pexels.com/photos/1181675/pexels-photo-1181675.jpeg?auto=compress&cs=tinysrgb&w=900" }
                }
            })),
            new("main", "cms.divider", "Spacer", Serialize(new { height = 24, showLine = false, label = "" })),
            new("main", "cms.features", "Platform Capabilities", Serialize(new
            {
                heading = "Cloud-Native Architecture",
                subheading = "Built for scale and custom extensibility",
                items = new[]
                {
                    new { icon = "Globe", title = "Multi-Language Ready", description = "Seamless localization and timezone-aware scheduling for global teams." },
                    new { icon = "Puzzle", title = "Open API & Webhooks", description = "Integrate cleanly with your existing ERP, CRM, and CI/CD stack." },
                    new { icon = "Shield", title = "Private Cloud Deployment", description = "Deploy via Docker or Kubernetes into your own VPC in minutes." }
                }
            }))
        ]),
        new("Product Tour", "product-views", true, "single-column",
        [
            new("main", "cms.gallery", "Workspace Previews", Serialize(new
            {
                heading = "Built for Clarity at Every Level",
                items = new[]
                {
                    new { url = "https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Real-Time Velocity & BI Dashboards" },
                    new { url = "https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Portfolio Resource Planning" },
                    new { url = "https://images.pexels.com/photos/577585/pexels-photo-577585.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Automation Rules & Audit Center" },
                    new { url = "https://images.pexels.com/photos/3183150/pexels-photo-3183150.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Live Collaborative Docs" }
                }
            }))
        ]),
        new("Security", "security", true, "two-column",
        [
            new("left", "cms.richtext", "Enterprise Compliance", Serialize(new
            {
                heading = "Zero-Trust Isolation & Full Observability",
                body = "Engineered with multi-tenant isolation, OpenTelemetry tracing, TLS 1.3 encryption, fine-grained RBAC, and active-active disaster recovery.",
                imageUrl = "https://images.pexels.com/photos/1181675/pexels-photo-1181675.jpeg?auto=compress&cs=tinysrgb&w=900"
            })),
            new("right", "cms.html", "Compliance Highlights", Serialize(new
            {
                html = "<div style=\"padding:1.25rem;border-radius:10px;border:1px solid rgba(37,99,235,0.22);background:rgba(37,99,235,0.05);\"><strong>🔐 Security &amp; Compliance</strong><ul style=\"margin:0.65rem 0 0;padding-left:1.2rem;line-height:1.8;\"><li>ISO/IEC 27001 &amp; SOC 2 Type II Ready</li><li>Immutable Audit Log Retention</li><li>Customer-Managed Encryption Keys (KMS)</li><li>Air-Gapped Private Cloud Option</li></ul></div>"
            })),
            new("right", "cms.button", "Whitepaper CTA", Serialize(new { text = "Request Security Whitepaper", href = "#contact", variant = "outline", align = "left", openNewTab = false }))
        ]),
        new("Product Video", "demo-video", true, "two-column",
        [
            new("left", "cms.video", "2-Minute Walkthrough", Serialize(new
            {
                videoUrl = "https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4",
                posterUrl = "https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900",
                caption = "2-Minute Platform Walkthrough",
                controls = true,
                autoPlay = false,
                loop = false,
                maxHeight = 280
            })),
            new("right", "cms.richtext", "Why Teams Switch", Serialize(new
            {
                heading = "Replace Tool Sprawl with One Cohesive Workspace",
                body = "Stop context-switching between docs, issue trackers, spreadsheets, and BI tools. Keep every spec, commit, and metric linked automatically."
            })),
            new("right", "cms.button", "Start Free Trial CTA", Serialize(new { text = "Start 14-Day Full Trial", href = "#contact", variant = "fill", align = "left", openNewTab = false }))
        ]),
        new("Comparison", "comparison", true, "single-column",
        [
            new("main", "cms.heading", "Platform Comparison", Serialize(new { title = "Legacy Toolchains vs Unified Platform", subtitle = "Upgrade from disconnected tools to one extensible operating layer", level = "h2", align = "center" })),
            new("main", "cms.html", "Comparison Cards", Serialize(new
            {
                html = "<div style=\"display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:1.25rem;\"><div style=\"padding:1.25rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(0,0,0,0.08);\"><strong>Disconnected Legacy Tools</strong><ul style=\"margin:0.65rem 0 0;padding-left:1.2rem;opacity:0.78;line-height:1.8;\"><li>Data siloed across 5+ apps with manual exports</li><li>Coarse permissions and fragmented audit logs</li><li>Cloud-only lock-in without private deployment</li></ul></div><div style=\"padding:1.25rem;border-radius:10px;background:rgba(37,99,235,0.06);border:1px solid rgba(37,99,235,0.28);\"><strong style=\"color:var(--cms-pri,#2563eb);\">✨ Unified Enterprise Platform</strong><ul style=\"margin:0.65rem 0 0;padding-left:1.2rem;line-height:1.8;\"><li>Specs, sprints, QA, OKRs, and BI connected natively</li><li>Zero-trust RBAC, masking, and immutable audits</li><li>Kubernetes self-hosting with hot-pluggable modules</li></ul></div></div>"
            }))
        ]),
        new("Integrations", "integrations", true, "three-column",
        [
            new("col-1", "cms.richtext", "DevOps Sync", Serialize(new
            {
                heading = "🔗 Git & CI/CD Automation",
                body = "Native hooks for GitHub, GitLab, and Jenkins keep pull requests and build statuses synced to every task."
            })),
            new("col-2", "cms.richtext", "ChatOps", Serialize(new
            {
                heading = "💬 Slack & Teams Workflows",
                body = "Create issues, approve deployments, and receive blocker alerts directly inside your team channels."
            })),
            new("col-3", "cms.richtext", "Identity & ERP", Serialize(new
            {
                heading = "🏢 Okta / AD & ERP Sync",
                body = "Automated SCIM provisioning and one-click timesheet exports for finance and compliance."
            }))
        ]),
        new("Pricing", "pricing", true, "single-column",
        [
            new("main", "cms.heading", "Pricing Heading", Serialize(new { title = "Simple, Transparent Pricing", subtitle = "Start free and scale smoothly as your organization grows", level = "h2", align = "center" })),
            new("main", "cms.pricing", "Plans", Serialize(new
            {
                heading = "Choose Your Plan",
                subheading = "Save 20% with annual billing · Enterprise invoicing supported",
                plans = new[]
                {
                    new { name = "Starter", price = "$0", period = "/ forever", badge = "Up to 10 seats", highlighted = false, features = new[] { "Up to 10 team members", "Unlimited boards & tasks", "5GB storage & basic permissions" }, buttonText = "Get Started Free", buttonHref = "#contact" },
                    new { name = "Team Pro", price = "$15", period = "/ seat / mo", badge = "Most Popular", highlighted = true, features = new[] { "Unlimited members & workflows", "OKRs & advanced BI analytics", "SSO & 7×12 priority support" }, buttonText = "Start 14-Day Trial", buttonHref = "#contact" },
                    new { name = "Enterprise", price = "Custom", period = "", badge = "On-Premises", highlighted = false, features = new[] { "Private cloud / air-gapped deploy", "99.99% SLA & dedicated architect", "Custom modules & source access" }, buttonText = "Contact Sales", buttonHref = "#contact" }
                }
            }))
        ]),
        new("Customers", "cases", true, "two-column",
        [
            new("left", "cms.articles", "Case Studies", Serialize(new
            {
                heading = "Customer Success Stories",
                subheading = "Click to read the full deployment story",
                items = new[]
                {
                    new { title = "EV Supply Chain Group: Scaling 800+ Engineers", category = "Manufacturing", date = "2026-11-18", summary = "Unified 4 R&D centers in 2 weeks and cut release cycle time by 35%.", content = "With tenant-isolated boards and automated QA gates, all four R&D hubs gained real-time visibility and passed compliance audits effortlessly." },
                    new { title = "FinTech Leader: Zero-Trust Private Portal", category = "FinTech", date = "2026-11-10", summary = "Full OpenTelemetry observability and 42% lower P99 API latency.", content = "Deployed on internal Kubernetes clusters with immutable audit logging to meet strict financial regulations." }
                }
            })),
            new("right", "cms.testimonials", "Executive Quotes", Serialize(new
            {
                heading = "Loved by Engineering Leaders",
                items = new[]
                {
                    new { quote = "Our weekly syncs dropped by half within two weeks — every delivery risk is visible on one board.", author = "VP Zhang", role = "VP of R&D, Smart Manufacturing", avatarUrl = "https://images.pexels.com/photos/220453/pexels-photo-220453.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "Private deployment was effortless, and the hot-pluggable module design gives us total control.", author = "Director Li", role = "Head of Platform, FinTech", avatarUrl = "https://images.pexels.com/photos/774909/pexels-photo-774909.jpeg?auto=compress&cs=tinysrgb&w=300" }
                }
            }))
        ]),
        new("FAQ", "faq", true, "sidebar",
        [
            new("sidebar", "cms.richtext", "Need Help?", Serialize(new { heading = "Still Have Questions?", body = "Our solutions architects are happy to provide a 1-on-1 walkthrough, POC sandbox, and migration assessment." })),
            new("sidebar", "cms.button", "Book Demo", Serialize(new { text = "Schedule 1v1 Demo", href = "#contact", variant = "fill", align = "left", openNewTab = false })),
            new("main", "cms.faq", "FAQ", Serialize(new
            {
                heading = "Frequently Asked Questions",
                items = new[]
                {
                    new { question = "Can we migrate existing data from Jira or Linear?", answer = "Yes. We provide one-click importers and structured JSON/CSV migration scripts so you can switch within a single business day." },
                    new { question = "What are the infrastructure requirements for self-hosting?", answer = "Supports PostgreSQL, SQL Server, MySQL, and SQLite via Docker or Helm Chart starting from 4 vCPU / 8GB RAM." },
                    new { question = "Do we keep our trial data when upgrading?", answer = "Absolutely. All projects, workflows, and reports created during your 14-day trial carry over seamlessly." },
                    new { question = "Are custom domains and multi-language portals supported?", answer = "Yes, built-in localization and custom domain routing are included out of the box." }
                }
            }))
        ]),
        new("Trial", "contact", true, "single-column",
        [
            new("main", "cms.contact", "Trial Form", Serialize(new
            {
                heading = "Start Your Free Trial",
                subheading = "Leave your work details and our solutions advisor will provision your sandbox within 30 minutes",
                submitText = "Request Free Trial",
                email = "sales@example.com",
                phone = "+1-800-555-0199"
            }))
        ]),
        new("Footer", "footer", false, "single-column",
        [
            new("main", "cms.footer", "Site Footer", Serialize(new
            {
                brand,
                tagline = $"{industry} · Secure · Agile · Extensible",
                copyright = $"© {DateTime.UtcNow.Year} {brand}. All rights reserved."
            }))
        ])
    ];

    // ============================================================
    // 3. 在线教育培训 —— 两栏首屏 + 三栏课程卡片 + 教学保障 + 班型定价 + 侧栏 FAQ
    // ============================================================
    private static List<CmsAiSectionDraft> EducationZh(string brand, string industry, string? stylePrompt) =>
    [
        new("首页", "home", true, "two-column",
        [
            new("left", "cms.hero", $"{brand} 首屏", Serialize(new
            {
                badge = $"{industry} · 实战派人才培养基地",
                headline = $"{brand}，让每一分努力都指向可见的成长",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt) ? "一线架构师与名企教研团队联合打造，结合真实商业项目实战、1v1 代码批改与就业内推闭环。" : stylePrompt.Trim(),
                primaryButtonText = "浏览全部课程",
                primaryButtonHref = "#courses",
                secondaryButtonText = "免费预约试听",
                secondaryButtonHref = "#contact"
            })),
            new("right", "cms.carousel", "实训风采轮播", Serialize(new
            {
                height = 380,
                autoPlay = true,
                intervalMs = 4800,
                slides = new[]
                {
                    new { imageUrl = "https://images.pexels.com/photos/1181359/pexels-photo-1181359.jpeg?auto=compress&cs=tinysrgb&w=1200", title = "小班直播与实时答疑", subtitle = "主讲 + 助教双师伴学，难题不过夜", buttonText = "预约试听", buttonHref = "#contact" },
                    new { imageUrl = "https://images.pexels.com/photos/1181359/pexels-photo-1181359.jpeg?auto=compress&cs=tinysrgb&w=1200", title = "企业级项目联合实战", subtitle = "还原高并发电商与数字化中台真实研发流程", buttonText = "查看课程", buttonHref = "#courses" }
                }
            }))
        ]),
        new("办学成果", "stats", true, "single-column",
        [
            new("main", "cms.stats", "核心成果数据", Serialize(new
            {
                items = new[]
                {
                    new { value = "96%", label = "学员就业满意度" },
                    new { value = "15 万+", label = "累计培养工程师" },
                    new { value = "120+", label = "企业级实战项目" },
                    new { value = "380+", label = "人才合作名企" }
                }
            })),
            new("main", "cms.partners", "合作企业", Serialize(new
            {
                heading = "毕业生就职与校企合作单位",
                items = new[]
                {
                    new { name = "华为生态伙伴" },
                    new { name = "字节跳动校企" },
                    new { name = "阿里云认证" },
                    new { name = "腾讯云智" },
                    new { name = "京东科技" },
                    new { name = "携程技术" }
                }
            }))
        ]),
        new("精品课程", "courses", true, "three-column",
        [
            new("col-1", "cms.richtext", "基础入门班", Serialize(new
            {
                heading = "📘 零基础全栈启蒙营",
                body = "4 周系统掌握编程思维、Web 基础与数据库核心操作，配有 20+ 随堂小项目与每日打卡批改。",
                imageUrl = "https://images.pexels.com/photos/1181671/pexels-photo-1181671.jpeg?auto=compress&cs=tinysrgb&w=900"
            })),
            new("col-2", "cms.richtext", "进阶实战营", Serialize(new
            {
                heading = "💻 企业级全栈特训营",
                body = "12 周沉浸式项目驱动学习，亲手完成多租户 SaaS、分布式缓存与容器化部署全流程。",
                imageUrl = "https://images.pexels.com/photos/577585/pexels-photo-577585.jpeg?auto=compress&cs=tinysrgb&w=900"
            })),
            new("col-3", "cms.richtext", "架构高阶班", Serialize(new
            {
                heading = "🏗️ 百万级高并发架构班",
                body = "深入 DDD 领域建模、高可用容灾、链路追踪与性能压测调优，直击资深工程师核心壁垒。",
                imageUrl = "https://images.pexels.com/photos/1181675/pexels-photo-1181675.jpeg?auto=compress&cs=tinysrgb&w=900"
            }))
        ]),
        new("教学体系", "learning", true, "single-column",
        [
            new("main", "cms.tabs", "四维伴学闭环", Serialize(new
            {
                heading = "学得会、写得出、用得上的培养体系",
                items = new[]
                {
                    new { title = "1v1 代码评审", content = "每次实战作业均由拥有 5 年以上一线经验的导师逐行 Review，指出命名、性能与架构隐患。", imageUrl = "https://images.pexels.com/photos/1181671/pexels-photo-1181671.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "商业级项目库", content = "拒绝玩具 Demo，课程项目直接对标真实企业生产标准，包含完整 CI/CD 与可观测性监控。", imageUrl = "https://images.pexels.com/photos/1181671/pexels-photo-1181671.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "模拟面试与内推", content = "结业阶段提供简历精修、大厂真题模拟面试以及合作企业绿色内推通道。", imageUrl = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=900" }
                }
            })),
            new("main", "cms.divider", "间距", Serialize(new { height = 24, showLine = false, label = "" })),
            new("main", "cms.features", "教研服务保障", Serialize(new
            {
                heading = "全程无忧学习保障",
                subheading = "让每位学员都能顺利跑完全程",
                items = new[]
                {
                    new { icon = "Check", title = "7 天无理由退费", description = "开课 7 天内不满意全额退费，零试错成本。" },
                    new { icon = "Clock", title = "3 年回放有效", description = "直播课程永久沉淀录播，随时回看温习。" },
                    new { icon = "Rocket", title = "重修免学费", description = "因工作忙碌跟不上进度，可免费申请随下期重修。" },
                    new { icon = "Edit", title = "逐行代码批改", description = "每次作业均有导师手写评审意见与重构建议。" },
                    new { icon = "Globe", title = "专属学习社群", description = "班主任督学打卡，结识同频技术伙伴。" },
                    new { icon = "Star", title = "就业内推通道", description = "结业直通 380+ 合作名企面试绿色通道。" }
                }
            }))
        ]),
        new("名师团队", "mentors", true, "three-column",
        [
            new("col-1", "cms.richtext", "首席架构讲师", Serialize(new
            {
                heading = "陈志远 · 前一线大厂首席架构师",
                body = "14 年分布式系统与云原生架构实战经验，主导过日均亿级请求交易平台重构，擅长把复杂架构讲透彻。",
                imageUrl = "https://images.pexels.com/photos/220453/pexels-photo-220453.jpeg?auto=compress&cs=tinysrgb&w=600"
            })),
            new("col-2", "cms.richtext", "全栈教研总监", Serialize(new
            {
                heading = "林舒雅 · 全栈工程教研总监",
                body = "10 年全栈开发与工程效能实践，累计指导 3,000+ 学员完成商业级项目答辩与大厂面试冲刺。",
                imageUrl = "https://images.pexels.com/photos/774909/pexels-photo-774909.jpeg?auto=compress&cs=tinysrgb&w=600"
            })),
            new("col-3", "cms.richtext", "性能调优专家", Serialize(new
            {
                heading = "周明轩 · 高并发与数据库专家",
                body = "深耕数据库内核调优、缓存一致性与可观测性体系建设，实战项目代码评审极度细致。",
                imageUrl = "https://images.pexels.com/photos/614810/pexels-photo-614810.jpeg?auto=compress&cs=tinysrgb&w=600"
            }))
        ]),
        new("实训风采", "campus", true, "single-column",
        [
            new("main", "cms.gallery", "线下实训与答辩现场", Serialize(new
            {
                heading = "沉浸式学习与黑客松现场",
                items = new[]
                {
                    new { url = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "分组项目黑客松协作" },
                    new { url = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "周末架构专题沙龙" },
                    new { url = "https://images.pexels.com/photos/3183150/pexels-photo-3183150.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "结业项目路演答辩" }
                }
            }))
        ]),
        new("学员口碑", "reviews", true, "two-column",
        [
            new("left", "cms.testimonials", "学员反馈", Serialize(new
            {
                heading = "来自毕业学员的真实蜕变",
                items = new[]
                {
                    new { quote = "从传统运维转全栈，三个月完整敲完两个实战项目，面试时直接展示在线作品集，顺利拿到高薪 Offer！", author = "赵同学", role = "全栈特训营 18 期学员", avatarUrl = "https://images.pexels.com/photos/614810/pexels-photo-614810.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "导师的 Code Review 太值了，帮我改掉了很多坏习惯，现在带团队做架构评审也更有底气。", author = "刘工程师", role = "架构高阶班学员", avatarUrl = "https://images.pexels.com/photos/415829/pexels-photo-415829.jpeg?auto=compress&cs=tinysrgb&w=300" }
                }
            })),
            new("right", "cms.articles", "喜报与公开课", Serialize(new
            {
                heading = "就业喜报与教研动态",
                subheading = "点击查看详情",
                items = new[]
                {
                    new { title = "11 月就业喜报：全栈营平均薪资涨幅 42%", category = "学员喜报", date = "2026-11-19", summary = "本期共 68 名毕业学员顺利入职互联网与智造头部企业。", content = "通过扎实的项目作品集与模拟面试特训，92% 的学员在结业 4 周内收到 2 个以上面试 Offer。" },
                    new { title = "免费公开课：DDD 模块化架构实战拆解", category = "公开课", date = "2026-11-25", summary = "本周四晚 20:00 直播，现场手把手拆解模块化系统设计。", content = "欢迎预约领取课程预习资料与源码仓库权限。" }
                }
            }))
        ]),
        new("试听片段", "preview-video", true, "two-column",
        [
            new("left", "cms.video", "名师公开课试听", Serialize(new
            {
                videoUrl = "https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4",
                posterUrl = "https://images.pexels.com/photos/1181359/pexels-photo-1181359.jpeg?auto=compress&cs=tinysrgb&w=900",
                caption = "《模块化架构与高并发实战》试听片段",
                controls = true,
                autoPlay = false,
                loop = false,
                maxHeight = 280
            })),
            new("right", "cms.richtext", "沉浸式手把手教学", Serialize(new
            {
                heading = "拒绝照本宣科，从第一行代码实战起",
                body = "每节课均采用“真实业务痛点引入 → 架构图推演 → 现场编码实现 → 压测对比分析”四步教学法，确保听得懂、写得出。"
            })),
            new("right", "cms.button", "领取完整试听", Serialize(new { text = "免费领取 3 节完整试听", href = "#contact", variant = "fill", align = "left", openNewTab = false }))
        ]),
        new("校区位置", "campus-map", true, "two-column",
        [
            new("left", "cms.richtext", "线下实训基地", Serialize(new
            {
                heading = "上海徐汇线下实训与自习中心",
                body = "📍 地址：上海市徐汇区漕河泾开发区宜山路 900 号\n🕒 开放时间：每日 09:00 - 21:30（含周末答疑角与模拟面试室）\n🚇 地铁：9 号线漕河泾开发区站 2 号口直达"
            })),
            new("right", "cms.map", "实训基地地图", Serialize(new
            {
                heading = "实训基地位置导航",
                title = $"{brand} · 上海实训中心",
                address = "上海市徐汇区宜山路 900 号",
                longitude = 121.4025,
                latitude = 31.1705,
                zoom = 14,
                height = 280,
                amapKey = "adc139d56406f3844c8f1cf1c6b65c41"
            }))
        ]),
        new("学习路径", "roadmap", true, "single-column",
        [
            new("main", "cms.heading", "四阶段成长路线图", Serialize(new { title = "从入门到架构师的四阶段成长路径", subtitle = "每周都有清晰里程碑与可验收成果", level = "h2", align = "center" })),
            new("main", "cms.html", "路线图卡片", Serialize(new
            {
                html = "<div style=\"display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:1rem;\"><div style=\"padding:1.1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(124,58,237,0.2);\"><strong style=\"color:var(--cms-pri,#7c3aed);\">阶段 01 · 筑基</strong><div style=\"font-size:0.88rem;opacity:0.82;margin-top:4px;\">夯实语言核心、数据结构与 SQL 查询优化，完成 20+ 随堂实验。</div></div><div style=\"padding:1.1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(124,58,237,0.2);\"><strong style=\"color:var(--cms-pri,#7c3aed);\">阶段 02 · 实战</strong><div style=\"font-size:0.88rem;opacity:0.82;margin-top:4px;\">独立开发多租户 SaaS 业务模块，掌握缓存、异步队列与单元测试。</div></div><div style=\"padding:1.1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(124,58,237,0.2);\"><strong style=\"color:var(--cms-pri,#7c3aed);\">阶段 03 · 架构</strong><div style=\"font-size:0.88rem;opacity:0.82;margin-top:4px;\">DDD 模块化拆分、Docker/K8s 部署与 OpenTelemetry 全链路排障。</div></div><div style=\"padding:1.1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(124,58,237,0.2);\"><strong style=\"color:var(--cms-pri,#7c3aed);\">阶段 04 · 冲刺</strong><div style=\"font-size:0.88rem;opacity:0.82;margin-top:4px;\">大厂真题模拟面试、作品集路演答辩与合作名企绿色内推。</div></div></div>"
            })),
            new("main", "cms.button", "领取完整大纲", Serialize(new { text = "免费领取完整课程大纲", href = "#contact", variant = "fill", align = "center", openNewTab = false }))
        ]),
        new("班型学费", "tuition", true, "single-column",
        [
            new("main", "cms.pricing", "班型与学费说明", Serialize(new
            {
                heading = "透明班型选择",
                subheading = "支持花呗/信用卡分期免息，在读学生凭证享 85 折助学金",
                plans = new[]
                {
                    new { name = "录播自学班", price = "¥1,699", period = "/ 期", badge = "自主节奏", highlighted = false, features = new[] { "全套高清录播与讲义源码", "学员交流群日常答疑", "2 年内课程内容免费更新" }, buttonText = "咨询报名", buttonHref = "#contact" },
                    new { name = "就业陪跑特训营", price = "¥4,980", period = "/ 期", badge = "口碑主推", highlighted = true, features = new[] { "小班直播 + 1v1 逐行代码批改", "3 个商业级项目实战与简历精修", "模拟面试 + 380+ 企业内推通道" }, buttonText = "抢占本期席位", buttonHref = "#contact" },
                    new { name = "1v1 导师定制班", price = "¥9,800", period = "/ 期", badge = "VIP 专属", highlighted = false, features = new[] { "定制个人学习与晋升路线图", "每周专属架构师 1v1 辅导", "跳槽谈薪与职业规划全程陪伴" }, buttonText = "预约导师评估", buttonHref = "#contact" }
                }
            }))
        ]),
        new("入学答疑", "faq", true, "sidebar",
        [
            new("sidebar", "cms.richtext", "教务咨询", Serialize(new
            {
                heading = "不知道选哪门课？",
                body = "提交您的当前技术栈与学习目标，课程规划师将为您免费定制专属学习路线并赠送 3 节试听课。"
            })),
            new("sidebar", "cms.html", "奖学金说明", Serialize(new
            {
                html = "<div style=\"padding:1rem;border-left:4px solid var(--cms-pri,#7c3aed);background:rgba(124,58,237,0.06);border-radius:8px;font-size:0.9rem;\"><strong>🎓 结业全勤奖学金</strong><div style=\"margin-top:4px;opacity:0.85;\">按时完成全部项目作业并通过结业答辩，最高可获 ¥800 现金奖学金返还。</div></div>"
            })),
            new("main", "cms.faq", "入学常见问题", Serialize(new
            {
                heading = "报名与学习常见问答",
                items = new[]
                {
                    new { question = "在职人员每天只有 1-2 小时能跟上吗？", answer = "完全可以。课程专门针对在职人群设计，工作日安排短视频+小练习，周末进行集中直播与项目串讲。" },
                    new { question = "零基础需要提前准备什么？", answer = "报名后教务老师会立即发放《前置预习手册》，带您在一周内配置好开发环境并熟悉基础语法。" },
                    new { question = "如何保证项目不是千篇一律的模板？", answer = "导师会根据您的行业背景分配不同业务模块，并指导您在架构设计中做出个性化亮点，避免简历同质化。" }
                }
            }))
        ]),
        new("预约试听", "contact", true, "single-column",
        [
            new("main", "cms.contact", "预约试听表单", Serialize(new
            {
                heading = "免费预约试听课与学习规划",
                subheading = "留下联系方式，立即领取最新课程大纲与 3 节精选实战试听视频",
                submitText = "立即领取试听课",
                email = "admissions@example.com",
                phone = "400-666-8899"
            }))
        ]),
        new("页脚", "footer", false, "single-column",
        [
            new("main", "cms.footer", "页脚", Serialize(new
            {
                brand,
                tagline = $"{industry} · 学以致用 · 成就职业跃迁",
                copyright = $"© {DateTime.UtcNow.Year} {brand}. All rights reserved."
            }))
        ])
    ];

    private static List<CmsAiSectionDraft> EducationEn(string brand, string industry, string? stylePrompt) =>
    [
        new("Home", "home", true, "two-column",
        [
            new("left", "cms.hero", $"{brand} Hero", Serialize(new
            {
                badge = $"{industry} · Career-Ready Bootcamps",
                headline = $"{brand}, Where Effort Turns Into Mastery",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt) ? "Co-built with senior architects: real production projects, 1-on-1 code reviews, and direct hiring referrals." : stylePrompt.Trim(),
                primaryButtonText = "Browse Courses",
                primaryButtonHref = "#courses",
                secondaryButtonText = "Book Free Trial",
                secondaryButtonHref = "#contact"
            })),
            new("right", "cms.carousel", "Classroom Highlights", Serialize(new
            {
                height = 380,
                autoPlay = true,
                intervalMs = 4800,
                slides = new[]
                {
                    new { imageUrl = "https://images.pexels.com/photos/1181359/pexels-photo-1181359.jpeg?auto=compress&cs=tinysrgb&w=1200", title = "Live Cohort Workshops", subtitle = "Lead instructors and TAs answer every question in real time", buttonText = "Book Trial", buttonHref = "#contact" },
                    new { imageUrl = "https://images.pexels.com/photos/1181359/pexels-photo-1181359.jpeg?auto=compress&cs=tinysrgb&w=1200", title = "Production Capstone Projects", subtitle = "Build multi-tenant SaaS and distributed systems from scratch", buttonText = "View Tracks", buttonHref = "#courses" }
                }
            }))
        ]),
        new("Outcomes", "stats", true, "single-column",
        [
            new("main", "cms.stats", "Student Outcomes", Serialize(new
            {
                items = new[]
                {
                    new { value = "96%", label = "Placement Satisfaction" },
                    new { value = "150K+", label = "Alumni Engineers" },
                    new { value = "120+", label = "Real-World Projects" },
                    new { value = "380+", label = "Hiring Partners" }
                }
            })),
            new("main", "cms.partners", "Hiring Partners", Serialize(new
            {
                heading = "Where Our Graduates Work",
                items = new[]
                {
                    new { name = "Huawei Cloud" },
                    new { name = "ByteDance" },
                    new { name = "Alibaba Cloud" },
                    new { name = "Tencent" },
                    new { name = "JD Tech" },
                    new { name = "Trip.com" }
                }
            }))
        ]),
        new("Courses", "courses", true, "three-column",
        [
            new("col-1", "cms.richtext", "Foundation", Serialize(new { heading = "📘 Foundations Track", body = "4 weeks covering programming fundamentals, web basics, and SQL with 20+ guided mini-labs.", imageUrl = "https://images.pexels.com/photos/1181671/pexels-photo-1181671.jpeg?auto=compress&cs=tinysrgb&w=900" })),
            new("col-2", "cms.richtext", "Full-Stack Bootcamp", Serialize(new { heading = "💻 Full-Stack Bootcamp", body = "12 weeks of project-driven training building multi-tenant SaaS apps and CI/CD pipelines.", imageUrl = "https://images.pexels.com/photos/577585/pexels-photo-577585.jpeg?auto=compress&cs=tinysrgb&w=900" })),
            new("col-3", "cms.richtext", "Architecture Pro", Serialize(new { heading = "🏗️ Architecture & Scale", body = "Master DDD, high availability, distributed tracing, and load-testing at production scale.", imageUrl = "https://images.pexels.com/photos/1181675/pexels-photo-1181675.jpeg?auto=compress&cs=tinysrgb&w=900" }))
        ]),
        new("Method", "learning", true, "single-column",
        [
            new("main", "cms.tabs", "Learning Loop", Serialize(new
            {
                heading = "A Curriculum Built for Retention & Hiring",
                items = new[]
                {
                    new { title = "1-on-1 Code Reviews", content = "Senior mentors review every pull request line by line for naming, security, and performance.", imageUrl = "https://images.pexels.com/photos/1181671/pexels-photo-1181671.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "Commercial Codebases", content = "No toy demos — build systems complete with observability, caching, and automated tests.", imageUrl = "https://images.pexels.com/photos/1181671/pexels-photo-1181671.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "Mock Interviews", content = "Resume clinics, live system design mocks, and direct referrals to 380+ partner companies.", imageUrl = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=900" }
                }
            })),
            new("main", "cms.divider", "Spacer", Serialize(new { height = 24, showLine = false, label = "" })),
            new("main", "cms.features", "Guarantees", Serialize(new
            {
                heading = "Student-First Guarantees",
                subheading = "Learn with zero risk",
                items = new[]
                {
                    new { icon = "Check", title = "7-Day Full Refund", description = "Not a fit within the first week? Get a full refund with zero hassle." },
                    new { icon = "Clock", title = "3-Year Replay Access", description = "Revisit every live session recording and updated material anytime." },
                    new { icon = "Rocket", title = "Free Cohort Retake", description = "Busy at work? Join the next cohort at no extra tuition cost." }
                }
            }))
        ]),
        new("Mentors", "mentors", true, "three-column",
        [
            new("col-1", "cms.richtext", "Principal Architect", Serialize(new
            {
                heading = "David Chen · Principal Architect",
                body = "14 years building distributed cloud platforms handling 100M+ daily requests, known for crystal-clear system breakdowns.",
                imageUrl = "https://images.pexels.com/photos/220453/pexels-photo-220453.jpeg?auto=compress&cs=tinysrgb&w=600"
            })),
            new("col-2", "cms.richtext", "Curriculum Lead", Serialize(new
            {
                heading = "Elena Lin · Full-Stack Director",
                body = "10 years in full-stack engineering; has mentored 3,000+ students through capstone defenses and top tech interviews.",
                imageUrl = "https://images.pexels.com/photos/774909/pexels-photo-774909.jpeg?auto=compress&cs=tinysrgb&w=600"
            })),
            new("col-3", "cms.richtext", "Performance Lead", Serialize(new
            {
                heading = "Marcus Zhou · Data & Scale Expert",
                body = "Specializes in SQL internals, caching strategies, and OpenTelemetry observability with meticulous PR feedback.",
                imageUrl = "https://images.pexels.com/photos/614810/pexels-photo-614810.jpeg?auto=compress&cs=tinysrgb&w=600"
            }))
        ]),
        new("Campus", "campus", true, "single-column",
        [
            new("main", "cms.gallery", "Workshops & Demo Days", Serialize(new
            {
                heading = "Hackathons & Capstone Demo Days",
                items = new[]
                {
                    new { url = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Team Hackathon Sprints" },
                    new { url = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Weekend Architecture Salons" },
                    new { url = "https://images.pexels.com/photos/3183150/pexels-photo-3183150.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Graduation Demo Day" }
                }
            }))
        ]),
        new("Reviews", "reviews", true, "two-column",
        [
            new("left", "cms.testimonials", "Student Reviews", Serialize(new
            {
                heading = "Real Student Transformations",
                items = new[]
                {
                    new { quote = "Transitioned from ops to full-stack in 3 months. Showing my live capstone in interviews landed me an offer right away.", author = "Alex Z.", role = "Cohort 18 Graduate", avatarUrl = "https://images.pexels.com/photos/614810/pexels-photo-614810.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "The line-by-line code reviews broke my bad habits and gave me confidence leading design reviews at work.", author = "Sarah L.", role = "Architecture Track", avatarUrl = "https://images.pexels.com/photos/415829/pexels-photo-415829.jpeg?auto=compress&cs=tinysrgb&w=300" }
                }
            })),
            new("right", "cms.articles", "News & Events", Serialize(new
            {
                heading = "Placement Reports & Open Classes",
                subheading = "Click to read more",
                items = new[]
                {
                    new { title = "November Placement Report: +42% Avg Salary Bump", category = "Outcomes", date = "2026-11-19", summary = "68 graduates joined top tech and smart-manufacturing firms.", content = "92% of graduates received 2+ interview offers within 4 weeks of completing their capstone defense." },
                    new { title = "Free Open Workshop: Modular DDD in Practice", category = "Workshop", date = "2026-11-25", summary = "Live this Thursday at 8:00 PM — step-by-step architecture breakdown.", content = "Reserve your seat to receive the prep guide and starter repository." }
                }
            }))
        ]),
        new("Sample Class", "preview-video", true, "two-column",
        [
            new("left", "cms.video", "Sample Lesson Clip", Serialize(new
            {
                videoUrl = "https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4",
                posterUrl = "https://images.pexels.com/photos/1181359/pexels-photo-1181359.jpeg?auto=compress&cs=tinysrgb&w=900",
                caption = "Modular Architecture & High-Concurrency Preview",
                controls = true,
                autoPlay = false,
                loop = false,
                maxHeight = 280
            })),
            new("right", "cms.richtext", "Hands-On Teaching", Serialize(new
            {
                heading = "Learn by Building Real Systems from Day One",
                body = "Every lesson follows our 4-step loop: real production bottleneck → architecture diagram → live coding → benchmark comparison."
            })),
            new("right", "cms.button", "Unlock Trial Lessons", Serialize(new { text = "Unlock 3 Free Full Lessons", href = "#contact", variant = "fill", align = "left", openNewTab = false }))
        ]),
        new("Campus Map", "campus-map", true, "two-column",
        [
            new("left", "cms.richtext", "Training Center", Serialize(new
            {
                heading = "Shanghai Offline Study & Hackathon Hub",
                body = "📍 900 Yishan Rd, Xuhui, Shanghai\n🕒 Open Daily 09:00 – 21:30 (Weekend mentor office hours & mock interview rooms)\n🚇 Metro Line 9 Caohejing Hi-Tech Park Exit 2"
            })),
            new("right", "cms.map", "Campus Map", Serialize(new
            {
                heading = "Find Our Training Center",
                title = $"{brand} Shanghai Hub",
                address = "900 Yishan Rd, Xuhui, Shanghai",
                longitude = 121.4025,
                latitude = 31.1705,
                zoom = 14,
                height = 280,
                amapKey = "adc139d56406f3844c8f1cf1c6b65c41"
            }))
        ]),
        new("Roadmap", "roadmap", true, "single-column",
        [
            new("main", "cms.heading", "4-Stage Learning Roadmap", Serialize(new { title = "A 4-Stage Path from Basics to Architect", subtitle = "Clear weekly milestones and tangible portfolio deliverables", level = "h2", align = "center" })),
            new("main", "cms.html", "Roadmap Cards", Serialize(new
            {
                html = "<div style=\"display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:1rem;\"><div style=\"padding:1.1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(124,58,237,0.2);\"><strong style=\"color:var(--cms-pri,#7c3aed);\">Stage 01 · Foundations</strong><div style=\"font-size:0.88rem;opacity:0.82;margin-top:4px;\">Core language mastery, data structures, and SQL tuning across 20+ labs.</div></div><div style=\"padding:1.1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(124,58,237,0.2);\"><strong style=\"color:var(--cms-pri,#7c3aed);\">Stage 02 · Capstones</strong><div style=\"font-size:0.88rem;opacity:0.82;margin-top:4px;\">Build multi-tenant SaaS modules with caching, queues, and automated tests.</div></div><div style=\"padding:1.1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(124,58,237,0.2);\"><strong style=\"color:var(--cms-pri,#7c3aed);\">Stage 03 · Scale</strong><div style=\"font-size:0.88rem;opacity:0.82;margin-top:4px;\">Modular DDD, Kubernetes deployment, and OpenTelemetry diagnostics.</div></div><div style=\"padding:1.1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(124,58,237,0.2);\"><strong style=\"color:var(--cms-pri,#7c3aed);\">Stage 04 · Hiring</strong><div style=\"font-size:0.88rem;opacity:0.82;margin-top:4px;\">Live mock interviews, capstone demo day, and direct partner referrals.</div></div></div>"
            })),
            new("main", "cms.button", "Get Syllabus CTA", Serialize(new { text = "Download Full Syllabus", href = "#contact", variant = "fill", align = "center", openNewTab = false }))
        ]),
        new("Tuition", "tuition", true, "single-column",
        [
            new("main", "cms.pricing", "Tuition Plans", Serialize(new
            {
                heading = "Transparent Tuition Options",
                subheading = "0% APR installments available · 15% student scholarship",
                plans = new[]
                {
                    new { name = "Self-Paced", price = "$249", period = "/ track", badge = "Flexible", highlighted = false, features = new[] { "All HD recordings & source code", "Community Q&A forum", "2 years of curriculum updates" }, buttonText = "Enroll Now", buttonHref = "#contact" },
                    new { name = "Career Bootcamp", price = "$699", period = "/ track", badge = "Most Popular", highlighted = true, features = new[] { "Live classes + 1-on-1 code reviews", "3 production capstones + resume polish", "Mock interviews & hiring referrals" }, buttonText = "Claim Your Seat", buttonHref = "#contact" },
                    new { name = "1-on-1 Mentorship", price = "$1,399", period = "/ track", badge = "VIP", highlighted = false, features = new[] { "Custom career & learning roadmap", "Weekly 1-on-1 principal architect calls", "Offer negotiation & career coaching" }, buttonText = "Book Assessment", buttonHref = "#contact" }
                }
            }))
        ]),
        new("FAQ", "faq", true, "sidebar",
        [
            new("sidebar", "cms.richtext", "Admissions", Serialize(new { heading = "Not Sure Where to Start?", body = "Share your background and goals — our advisor will map out a custom plan and send 3 free trial lessons." })),
            new("sidebar", "cms.html", "Scholarship", Serialize(new
            {
                html = "<div style=\"padding:1rem;border-left:4px solid var(--cms-pri,#7c3aed);background:rgba(124,58,237,0.06);border-radius:8px;font-size:0.9rem;\"><strong>🎓 Completion Scholarship</strong><div style=\"margin-top:4px;opacity:0.85;\">Complete all labs and pass your final defense to earn up to $120 cashback.</div></div>"
            })),
            new("main", "cms.faq", "FAQ", Serialize(new
            {
                heading = "Admissions & Learning FAQ",
                items = new[]
                {
                    new { question = "Can I keep up with only 1–2 hours a day alongside a full-time job?", answer = "Yes. Weekday modules are bite-sized, while live workshops and project sprints take place on weekends." },
                    new { question = "What do I need to prepare if I'm a beginner?", answer = "Right after enrolling, you'll receive our Prep Guide to set up your environment and basics in one week." },
                    new { question = "How do you ensure capstone projects stand out on resumes?", answer = "Mentors assign domain variations and guide you through unique architectural highlights." }
                }
            }))
        ]),
        new("Enroll", "contact", true, "single-column",
        [
            new("main", "cms.contact", "Enrollment Form", Serialize(new
            {
                heading = "Book a Free Trial Lesson",
                subheading = "Leave your details to unlock the syllabus and 3 sample project walkthroughs",
                submitText = "Get Free Trial",
                email = "admissions@example.com",
                phone = "+1-800-555-0188"
            }))
        ]),
        new("Footer", "footer", false, "single-column",
        [
            new("main", "cms.footer", "Site Footer", Serialize(new
            {
                brand,
                tagline = $"{industry} · Learn by Building · Accelerate Your Career",
                copyright = $"© {DateTime.UtcNow.Year} {brand}. All rights reserved."
            }))
        ])
    ];

    // ============================================================
    // 4. 个人/工作室作品集 —— 极简设计感 + 三栏作品 + 画廊 + 复盘文章 + 报价 + 合作地图
    // ============================================================
    private static List<CmsAiSectionDraft> PortfolioZh(string brand, string industry, string? stylePrompt) =>
    [
        new("首屏", "home", true, "single-column",
        [
            new("main", "cms.hero", $"{brand} 首屏", Serialize(new
            {
                badge = $"{industry} · 独立设计工作室",
                headline = $"你好，我是 {brand}",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt) ? "专注品牌视觉识别、数字产品界面与前端工程落地，用克制而有力的设计语言讲好产品故事。" : stylePrompt.Trim(),
                primaryButtonText = "浏览精选作品",
                primaryButtonHref = "#works",
                secondaryButtonText = "发起项目合作",
                secondaryButtonHref = "#contact"
            })),
            new("main", "cms.stats", "履历概览", Serialize(new
            {
                items = new[]
                {
                    new { value = "8 年", label = "商业设计经验" },
                    new { value = "60+", label = "已上线落地项目" },
                    new { value = "12 项", label = "国内外设计奖项" }
                }
            }))
        ]),
        new("精选作品", "works", true, "three-column",
        [
            new("col-1", "cms.image", "品牌视觉设计", Serialize(new { imageUrl = "https://images.pexels.com/photos/196644/pexels-photo-196644.jpeg?auto=compress&cs=tinysrgb&w=900", alt = "品牌视觉设计", caption = "01 / 新消费品牌全案视觉系统", maxHeight = 320, rounded = true, shadow = true })),
            new("col-2", "cms.image", "移动端产品界面", Serialize(new { imageUrl = "https://images.pexels.com/photos/326503/pexels-photo-326503.jpeg?auto=compress&cs=tinysrgb&w=900", alt = "移动端产品界面", caption = "02 / 金融科技移动 App 体验重构", maxHeight = 320, rounded = true, shadow = true })),
            new("col-3", "cms.image", "创意视觉实验", Serialize(new { imageUrl = "https://images.pexels.com/photos/1779487/pexels-photo-1779487.jpeg?auto=compress&cs=tinysrgb&w=900", alt = "创意视觉实验", caption = "03 / 3D 材质与数字艺术展视觉", maxHeight = 320, rounded = true, shadow = true }))
        ]),
        new("作品画廊", "gallery", true, "single-column",
        [
            new("main", "cms.gallery", "更多精选片段", Serialize(new
            {
                heading = "细节与界面探索",
                items = new[]
                {
                    new { url = "https://images.pexels.com/photos/196644/pexels-photo-196644.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "SaaS 设计系统规范" },
                    new { url = "https://images.pexels.com/photos/326503/pexels-photo-326503.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "交互动效原型" },
                    new { url = "https://images.pexels.com/photos/1779487/pexels-photo-1779487.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "响应式官网落地" },
                    new { url = "https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "极简排版与栅格" },
                    new { url = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "品牌工作坊共创" },
                    new { url = "https://images.pexels.com/photos/577585/pexels-photo-577585.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "前端组件库工程化" }
                }
            }))
        ]),
        new("关于我", "about", true, "two-column",
        [
            new("left", "cms.richtext", "个人简介", Serialize(new
            {
                heading = $"关于 {brand}",
                body = $"深耕{industry}与交互工程 8 年，曾服务多家上市科技公司与早期独角兽团队。我相信好的设计既要有美学张力，更要能高保真地交付上线，真正驱动业务转化。",
                imageUrl = "https://images.pexels.com/photos/220453/pexels-photo-220453.jpeg?auto=compress&cs=tinysrgb&w=900"
            })),
            new("right", "cms.features", "服务方向", Serialize(new
            {
                heading = "我能为你提供",
                subheading = "从创意构想到代码交付的一站式支持",
                items = new[]
                {
                    new { icon = "Edit", title = "品牌与视觉系统", description = "Logo、字体色彩规范与品牌手册定义。" },
                    new { icon = "Puzzle", title = "设计系统与开发", description = "Figma 组件库到 Blazor / Web 高保真还原。" },
                    new { icon = "Chart", title = "转化率体验优化", description = "基于热图与漏斗重构核心着陆页。" }
                }
            })),
            new("right", "cms.tabs", "合作流程", Serialize(new
            {
                heading = "标准合作节奏",
                items = new[]
                {
                    new { title = "01 需求诊断", content = "1 小时深度访谈，梳理品牌调性、核心受众与上线时间节点。" },
                    new { title = "02 概念与原型", content = "输出两套视觉情绪板与高保真交互原型，快速对齐方向。" },
                    new { title = "03 交付与走查", content = "交付完整设计资产或生产级前端代码，并全程跟进视觉走查。" }
                }
            }))
        ]),
        new("思考与评价", "reviews", true, "single-column",
        [
            new("main", "cms.articles", "案例复盘与设计笔记", Serialize(new
            {
                heading = "案例复盘与专栏文章",
                subheading = "点击卡片阅读完整设计思考",
                items = new[]
                {
                    new { title = "如何用 12 栅格与微件体系搭建高转化企业官网", category = "设计复盘", date = "2026-11-16", summary = "拆解从信息架构、视觉节奏到首屏行动号召的设计决策。", content = "优秀着陆页的关键在于清晰的视觉层级与克制的留白。通过双栏与三栏节奏交替，可以显著降低长页面的阅读疲劳，提高留资转化率。" },
                    new { title = "Design Token 实战：让暗色模式与品牌换肤零成本", category = "工程实践", date = "2026-11-08", summary = "一套 CSS 变量体系如何同时服务 Figma 与前端组件库。", content = "将主色、表面色、圆角与字体抽离为语义化 Token，使得品牌定制只需几秒即可全局生效。" }
                }
            })),
            new("main", "cms.divider", "间距", Serialize(new { height = 24, showLine = false, label = "" })),
            new("main", "cms.testimonials", "客户评价", Serialize(new
            {
                heading = "合作伙伴评价",
                items = new[]
                {
                    new { quote = "沟通非常顺畅，交付极其专业，最终官网上线效果远超我们董事会预期。", author = "王总监", role = "某科技品牌市场负责人", avatarUrl = "https://images.pexels.com/photos/220453/pexels-photo-220453.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "细节打磨非常到位，不仅给出了美观的视觉稿，连前端实现细节都帮我们考虑周全。", author = "李女士", role = "独立消费品牌创始人", avatarUrl = "https://images.pexels.com/photos/774909/pexels-photo-774909.jpeg?auto=compress&cs=tinysrgb&w=300" }
                }
            }))
        ]),
        new("专业能力", "stack", true, "three-column",
        [
            new("col-1", "cms.richtext", "视觉与品牌规范", Serialize(new
            {
                heading = "🎨 品牌与 UI 规范",
                body = "Figma 组件化架构、排版栅格系统与语义化 Design Token，兼顾品牌辨识度与团队协作效率。"
            })),
            new("col-2", "cms.richtext", "前端高保真落地", Serialize(new
            {
                heading = "⚡ 生产级前端工程",
                body = "精通现代 CSS Grid、Blazor 与响应式交互，确保设计稿 1:1 高保真上线与流畅性能。"
            })),
            new("col-3", "cms.richtext", "动效与三维视觉", Serialize(new
            {
                heading = "✨ 微交互与三维表现",
                body = "克制而高级的滚动触发动效、微交互反馈与 3D 主视觉渲染，显著提升品牌质感。"
            }))
        ]),
        new("服务报价", "pricing", true, "single-column",
        [
            new("main", "cms.pricing", "合作模式与参考报价", Serialize(new
            {
                heading = "灵活的项目合作方式",
                subheading = "均包含源文件交付与上线后 30 天微调支持",
                plans = new[]
                {
                    new { name = "体验诊断与规范", price = "¥6,800", period = "/ 次", badge = "快速启动", highlighted = false, features = new[] { "现有网站/App UX 走查报告", "品牌色彩与字体 Token 梳理", "3 个核心页面重设计" }, buttonText = "预约诊断", buttonHref = "#contact" },
                    new { name = "官网全案设计与落地", price = "¥18,000", period = "/ 项", badge = "最常选择", highlighted = true, features = new[] { "信息架构与全套高保真 UI", "响应式多语言交互动效落地", "上线走查与设计资产全量交付" }, buttonText = "咨询档期", buttonHref = "#contact" },
                    new { name = "月度设计顾问", price = "¥12,000", period = "/ 月", badge = "长期陪伴", highlighted = false, features = new[] { "每周固定工时优先响应", "产品迭代 UI 与运营海报支持", "参与团队设计评审与规范建设" }, buttonText = "洽谈长期合作", buttonHref = "#contact" }
                }
            }))
        ]),
        new("动态集锦", "showreel", true, "two-column",
        [
            new("left", "cms.video", "年度动态设计 Showreel", Serialize(new
            {
                videoUrl = "https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4",
                posterUrl = "https://images.pexels.com/photos/1779487/pexels-photo-1779487.jpeg?auto=compress&cs=tinysrgb&w=900",
                caption = "2026 交互动效与品牌视觉 Showreel",
                controls = true,
                autoPlay = false,
                loop = false,
                maxHeight = 280
            })),
            new("right", "cms.richtext", "动效与交互哲学", Serialize(new
            {
                heading = "用有呼吸感的动效建立品牌记忆",
                body = "动效不只是装饰，而是引导视线、传达空间关系与提升操作反馈的重要语言。每一帧缓动曲线都经过真实设备帧率打磨。"
            })),
            new("right", "cms.button", "查看合作报价", Serialize(new { text = "查看项目合作报价", href = "#pricing", variant = "outline", align = "left", openNewTab = false }))
        ]),
        new("获奖解读", "awards", true, "three-column",
        [
            new("col-1", "cms.richtext", "Red Dot 获奖作品", Serialize(new { heading = "🏆 Red Dot · 品牌设计奖", body = "为某新能源出行品牌重构车载 HMI 视觉语言与数字官网，兼顾驾驶安全与未来美学。" })),
            new("col-2", "cms.richtext", "iF 设计奖项目", Serialize(new { heading = "🎖️ iF Design · 体验设计奖", body = "为全球跨境供应链 SaaS 打造零学习成本的多端统一设计系统，用户留存率提升 28%。" })),
            new("col-3", "cms.richtext", "Awwwards 佳作", Serialize(new { heading = "🌟 Awwwards · Site of the Day", body = "采用 12 栅格弹性排版与滚动触发动效打造的艺术展览数字空间。" }))
        ]),
        new("合作品牌", "clients", true, "single-column",
        [
            new("main", "cms.partners", "服务过的品牌", Serialize(new
            {
                heading = "曾服务与合作过的品牌及团队",
                items = new[]
                {
                    new { name = "Red Dot Winner" },
                    new { name = "iF Design" },
                    new { name = "Awwwards" },
                    new { name = "字节跳动设计" },
                    new { name = "蔚来汽车" },
                    new { name = "大疆创新" }
                }
            }))
        ]),
        new("合作答疑", "faq", true, "two-column",
        [
            new("left", "cms.richtext", "设计合作说明", Serialize(new
            {
                heading = "透明高效的远程与驻场协作",
                body = "所有项目均签署正式合同并分阶段验收交付（概念定稿 / 全套 UI / 上线走查），每周固定同步进展，确保项目节奏可控。"
            })),
            new("left", "cms.button", "查看报价按钮", Serialize(new { text = "立即预约下月档期", href = "#contact", variant = "fill", align = "left", openNewTab = false })),
            new("right", "cms.faq", "合作常见问题", Serialize(new
            {
                heading = "项目合作常见问答",
                items = new[]
                {
                    new { question = "一般整站设计与开发需要多久？", answer = "企业官网全案通常为 2–3 周，复杂 SaaS 控制台设计系统为 4–6 周。" },
                    new { question = "交付物包含哪些内容？", answer = "包含完整的 Figma 源文件、可复用组件库、切图与字体规范，如含前端开发则一并交付源码。" }
                }
            }))
        ]),
        new("联系我", "contact", true, "sidebar",
        [
            new("sidebar", "cms.richtext", "联系信息", Serialize(new
            {
                heading = "联系方式与档期",
                body = "📧 邮箱：hello@example.com\n📍 坐标：上海静安 · 支持全国及海外远程协作\n📅 当前可预约：下月档期余 2 席，通常 1 个工作日内回复。"
            })),
            new("sidebar", "cms.map", "工作室位置", Serialize(new
            {
                heading = "线下见面地点",
                title = $"{brand} Studio",
                address = "上海市静安区南京西路 1266 号",
                longitude = 121.4532,
                latitude = 31.2286,
                zoom = 14,
                height = 240,
                amapKey = "adc139d56406f3844c8f1cf1c6b65c41"
            })),
            new("main", "cms.contact", "联系表单", Serialize(new
            {
                heading = "一起做点有意思的事",
                subheading = "简单描述您的项目背景、目标上线时间与预算范围，我会尽快回复",
                submitText = "发送合作意向",
                email = "hello@example.com",
                address = "上海市静安区南京西路 1266 号"
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
                badge = $"{industry} · Independent Studio",
                headline = $"Hello, I'm {brand}",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt) ? "Crafting restrained, expressive brand identities, product interfaces, and production-ready web experiences." : stylePrompt.Trim(),
                primaryButtonText = "View Selected Work",
                primaryButtonHref = "#works",
                secondaryButtonText = "Start a Project",
                secondaryButtonHref = "#contact"
            })),
            new("main", "cms.stats", "Track Record", Serialize(new
            {
                items = new[]
                {
                    new { value = "8 Yrs", label = "Design Experience" },
                    new { value = "60+", label = "Shipped Projects" },
                    new { value = "12", label = "Design Awards" }
                }
            }))
        ]),
        new("Selected Work", "works", true, "three-column",
        [
            new("col-1", "cms.image", "Brand Visual Identity", Serialize(new { imageUrl = "https://images.pexels.com/photos/196644/pexels-photo-196644.jpeg?auto=compress&cs=tinysrgb&w=900", alt = "Brand Visual Identity", caption = "01 / Consumer Brand Identity System", maxHeight = 320, rounded = true, shadow = true })),
            new("col-2", "cms.image", "Mobile Product UI", Serialize(new { imageUrl = "https://images.pexels.com/photos/326503/pexels-photo-326503.jpeg?auto=compress&cs=tinysrgb&w=900", alt = "Mobile Product UI", caption = "02 / FinTech Mobile App Redesign", maxHeight = 320, rounded = true, shadow = true })),
            new("col-3", "cms.image", "Creative Visual Study", Serialize(new { imageUrl = "https://images.pexels.com/photos/1779487/pexels-photo-1779487.jpeg?auto=compress&cs=tinysrgb&w=900", alt = "Creative Visual Study", caption = "03 / 3D Material & Exhibition Key Visual", maxHeight = 320, rounded = true, shadow = true }))
        ]),
        new("Gallery", "gallery", true, "single-column",
        [
            new("main", "cms.gallery", "Interface Explorations", Serialize(new
            {
                heading = "Craft & Interface Details",
                items = new[]
                {
                    new { url = "https://images.pexels.com/photos/196644/pexels-photo-196644.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "SaaS Design System" },
                    new { url = "https://images.pexels.com/photos/326503/pexels-photo-326503.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Interactive Motion Prototypes" },
                    new { url = "https://images.pexels.com/photos/1779487/pexels-photo-1779487.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Responsive Web Launch" },
                    new { url = "https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Editorial Grid & Typography" }
                }
            }))
        ]),
        new("About", "about", true, "two-column",
        [
            new("left", "cms.richtext", "About Me", Serialize(new
            {
                heading = $"About {brand}",
                body = $"With 8 years in {industry} and frontend craft, I partner with ambitious founders and product teams to turn complex ideas into clear, high-converting digital products.",
                imageUrl = "https://images.pexels.com/photos/220453/pexels-photo-220453.jpeg?auto=compress&cs=tinysrgb&w=900"
            })),
            new("right", "cms.features", "Services", Serialize(new
            {
                heading = "What I Do",
                subheading = "From concept to production launch",
                items = new[]
                {
                    new { icon = "Edit", title = "Brand & Visual Systems", description = "Logos, typography scales, and cohesive brand guidelines." },
                    new { icon = "Puzzle", title = "Design Systems & Code", description = "From Figma component libraries to responsive web builds." },
                    new { icon = "Chart", title = "Conversion UX", description = "Refining landing pages and funnels with real user insights." }
                }
            })),
            new("right", "cms.tabs", "Process", Serialize(new
            {
                heading = "How We Work Together",
                items = new[]
                {
                    new { title = "01 Discovery", content = "60-minute deep dive into your brand positioning, audience, and launch timeline." },
                    new { title = "02 Concepts", content = "Two visual directions and interactive prototypes to align early." },
                    new { title = "03 Delivery", content = "Production assets or clean frontend code with thorough QA sign-off." }
                }
            }))
        ]),
        new("Notes & Reviews", "reviews", true, "single-column",
        [
            new("main", "cms.articles", "Case Studies & Notes", Serialize(new
            {
                heading = "Case Studies & Design Notes",
                subheading = "Click any card to read the full breakdown",
                items = new[]
                {
                    new { title = "Building High-Converting Landing Pages with 12-Col Grids", category = "Case Study", date = "2026-11-16", summary = "How layout rhythm, whitespace, and clear CTAs lift lead conversion.", content = "Alternating two-column and three-column sections reduces reading fatigue and guides attention naturally toward the primary action." },
                    new { title = "Design Tokens in Practice: Zero-Cost Brand Theming", category = "Engineering", date = "2026-11-08", summary = "Sharing one CSS variable contract between Figma and production UI.", content = "Semantic tokens for primary, surface, radius, and typography allow instant rebranding without touching component markup." }
                }
            })),
            new("main", "cms.divider", "Spacer", Serialize(new { height = 24, showLine = false, label = "" })),
            new("main", "cms.testimonials", "Client Reviews", Serialize(new
            {
                heading = "What Partners Say",
                items = new[]
                {
                    new { quote = "Smooth communication and deeply professional delivery — the final site exceeded our board's expectations.", author = "Director Wang", role = "Marketing Lead", avatarUrl = "https://images.pexels.com/photos/220453/pexels-photo-220453.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "Impressive attention to detail from visual polish all the way to frontend implementation.", author = "Ms. Li", role = "Independent Brand Founder", avatarUrl = "https://images.pexels.com/photos/774909/pexels-photo-774909.jpeg?auto=compress&cs=tinysrgb&w=300" }
                }
            }))
        ]),
        new("Capabilities", "stack", true, "three-column",
        [
            new("col-1", "cms.richtext", "Visual & UI Craft", Serialize(new
            {
                heading = "🎨 Visual & Brand Craft",
                body = "Figma component architecture, editorial typography, and scalable design tokens built for multi-brand consistency."
            })),
            new("col-2", "cms.richtext", "Frontend Engineering", Serialize(new
            {
                heading = "⚡ Production Code",
                body = "Semantic HTML, modern CSS Grid, Blazor & TypeScript — delivering pixel-accurate, accessible web experiences."
            })),
            new("col-3", "cms.richtext", "Motion & 3D", Serialize(new
            {
                heading = "✨ Motion & Storytelling",
                body = "Purposeful micro-interactions, scroll-driven reveals, and 3D key visuals that elevate brand perception."
            }))
        ]),
        new("Pricing", "pricing", true, "single-column",
        [
            new("main", "cms.pricing", "Engagement Models", Serialize(new
            {
                heading = "Flexible Engagement Options",
                subheading = "Includes full source files and 30 days of post-launch polish",
                plans = new[]
                {
                    new { name = "UX Audit & Tokens", price = "$950", period = "/ sprint", badge = "Quick Start", highlighted = false, features = new[] { "Full UX & conversion audit", "Color & typography token system", "Redesign of 3 key screens" }, buttonText = "Book Audit", buttonHref = "#contact" },
                    new { name = "Full Website Design", price = "$2,600", period = "/ project", badge = "Most Popular", highlighted = true, features = new[] { "IA + full high-fidelity UI", "Responsive & multi-language polish", "QA walkthrough & asset handoff" }, buttonText = "Check Availability", buttonHref = "#contact" },
                    new { name = "Monthly Retainer", price = "$1,800", period = "/ month", badge = "Ongoing", highlighted = false, features = new[] { "Dedicated weekly design hours", "Product UI & campaign assets", "Design critiques & system upkeep" }, buttonText = "Discuss Retainer", buttonHref = "#contact" }
                }
            }))
        ]),
        new("Showreel", "showreel", true, "two-column",
        [
            new("left", "cms.video", "2026 Motion Showreel", Serialize(new
            {
                videoUrl = "https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4",
                posterUrl = "https://images.pexels.com/photos/1779487/pexels-photo-1779487.jpeg?auto=compress&cs=tinysrgb&w=900",
                caption = "2026 Interactive & Motion Showreel",
                controls = true,
                autoPlay = false,
                loop = false,
                maxHeight = 280
            })),
            new("right", "cms.richtext", "Motion Philosophy", Serialize(new
            {
                heading = "Purposeful Motion That Guides Attention",
                body = "Motion isn't decoration — it clarifies hierarchy, spatial context, and feedback. Every easing curve is tuned on real devices for 60fps fluidity."
            })),
            new("right", "cms.button", "View Pricing CTA", Serialize(new { text = "Explore Engagement Models", href = "#pricing", variant = "outline", align = "left", openNewTab = false }))
        ]),
        new("Awards", "awards", true, "three-column",
        [
            new("col-1", "cms.richtext", "Red Dot Award", Serialize(new { heading = "🏆 Red Dot · Brand Design", body = "Redesigned the HMI visual language and digital flagship for an EV mobility brand." })),
            new("col-2", "cms.richtext", "iF Design Award", Serialize(new { heading = "🎖️ iF Design · UX System", body = "Unified a cross-platform SaaS design system for global logistics, lifting retention by 28%." })),
            new("col-3", "cms.richtext", "Awwwards SOTD", Serialize(new { heading = "🌟 Awwwards · Site of the Day", body = "Editorial 12-column exhibition experience powered by scroll-driven reveals." }))
        ]),
        new("Clients", "clients", true, "single-column",
        [
            new("main", "cms.partners", "Awards & Clients", Serialize(new
            {
                heading = "Selected Brands & Recognition",
                items = new[]
                {
                    new { name = "Red Dot Winner" },
                    new { name = "iF Design" },
                    new { name = "Awwwards" },
                    new { name = "ByteDance" },
                    new { name = "NIO" },
                    new { name = "DJI" }
                }
            }))
        ]),
        new("FAQ", "faq", true, "two-column",
        [
            new("left", "cms.richtext", "Working Together", Serialize(new
            {
                heading = "Clear Milestones, Zero Surprises",
                body = "Every engagement runs on milestone sign-offs (Concepts / Full UI / Launch QA) with weekly async updates and Figma reviews."
            })),
            new("left", "cms.button", "Book Slot CTA", Serialize(new { text = "Reserve Next Month's Slot", href = "#contact", variant = "fill", align = "left", openNewTab = false })),
            new("right", "cms.faq", "Project FAQ", Serialize(new
            {
                heading = "Collaboration FAQ",
                items = new[]
                {
                    new { question = "How long does a full website project take?", answer = "Typically 2–3 weeks for marketing sites and 4–6 weeks for SaaS design systems." },
                    new { question = "What deliverables are included?", answer = "Full Figma source files, component tokens, export assets, and clean production code if scoped." }
                }
            }))
        ]),
        new("Contact", "contact", true, "sidebar",
        [
            new("sidebar", "cms.richtext", "Contact Info", Serialize(new
            {
                heading = "Get in Touch",
                body = "📧 Email: hello@example.com\n📍 Shanghai · Open to global remote\n📅 Booking next month (2 slots left) · Replies within 1 business day."
            })),
            new("sidebar", "cms.map", "Studio Location", Serialize(new
            {
                heading = "Studio Address",
                title = $"{brand} Studio",
                address = "1266 Nanjing West Rd, Jing'an, Shanghai",
                longitude = 121.4532,
                latitude = 31.2286,
                zoom = 14,
                height = 240,
                amapKey = "adc139d56406f3844c8f1cf1c6b65c41"
            })),
            new("main", "cms.contact", "Contact Form", Serialize(new
            {
                heading = "Let's Make Something Great",
                subheading = "Share your project scope, target launch date, and budget — I'll get back to you soon",
                submitText = "Send Inquiry",
                email = "hello@example.com",
                address = "1266 Nanjing West Rd, Jing'an, Shanghai"
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

    // ============================================================
    // 5. Umbraco 暖调编辑企业风 (umbraco-editorial)
    //    暖灰底 #f1f0ee + 皇家蓝 #283a97 + 大图 Hero + Logo 跑马灯 + 证言轮播 + 深蓝转化横幅
    // ============================================================
    private static List<CmsAiSectionDraft> UmbracoZh(string brand, string industry, string? stylePrompt) =>
    [
        new("首页", "home", true, "single-column",
        [
            new("main", "cms.html", "顶部公告胶囊条", Serialize(new
            {
                html = "<div style=\"display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:0.75rem;background:#283a97;color:#ffffff;padding:0.65rem 1.35rem;border-radius:999px;font-size:0.8125rem;margin-bottom:1.25rem;\"><span><strong>2026 冬季发布会</strong> · 探索 AI 原生与可组合企业级内容架构</span><a href=\"#contact\" style=\"color:#f5c1bc;text-decoration:none;font-weight:600;\">立即预约席位 →</a></div>"
            })),
            new("main", "cms.hero", $"{brand} 摄影首屏", Serialize(new
            {
                badge = "2026 WINTER RELEASE · READY FOR AI",
                headline = $"{brand}：面向 AI 时代的开源 .NET CMS",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt)
                    ? "兼具开发者的极致自由度与运营团队的友好编辑体验，构建可扩展的企业级数字体验底座。"
                    : stylePrompt.Trim(),
                primaryButtonText = "预约架构演示",
                primaryButtonHref = "#contact",
                backgroundImageUrl = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=1200",
                variant = "editorial"
            }))
        ]),
        new("信任背书", "logos", true, "single-column",
        [
            new("main", "cms.partners", "全球客户跑马灯", Serialize(new
            {
                heading = "",
                variant = "marquee",
                items = new[]
                {
                    new { name = "Mercedes-Benz" },
                    new { name = "Carlsberg" },
                    new { name = "Fujitsu" },
                    new { name = "Renault" },
                    new { name = "Volvo" },
                    new { name = "Heineken" },
                    new { name = "Suzuki" },
                    new { name = "Domino's" }
                }
            }))
        ]),
        new("AI 能力", "ai-platform", true, "single-column",
        [
            new("main", "cms.html", "AI 非对称标题", Serialize(new
            {
                html = "<div style=\"display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:1.5rem;align-items:end;margin-bottom:1.75rem;\"><div style=\"font-size:0.75rem;font-weight:700;letter-spacing:0.12em;text-transform:uppercase;color:#283a97;\">OPEN DXP &amp; AI</div><h2 style=\"margin:0;font-size:clamp(2rem,3.8vw,3.5rem);font-weight:400;line-height:1.12;color:#283a97;\">AI 能力融入每一次内容创作</h2></div>"
            })),
            new("main", "cms.html", "AI 三栏 1:1 白色方卡", Serialize(new
            {
                html = "<div class=\"cms-umb-grid-3\"><a href=\"#contact\" class=\"cms-umb-square-card\"><div style=\"font-size:0.75rem;font-weight:700;letter-spacing:0.08em;color:#283a97;opacity:0.7;\">01 / MCP NATIVE</div><div><h3 style=\"margin:0 0 0.75rem;font-size:1.5rem;font-weight:400;color:#283a97;\">MCP 原生协议接入</h3><p style=\"margin:0;opacity:0.78;line-height:1.6;\">让大模型直接理解您的内容模型与区块规范，安全执行可视化编排与校验。</p></div><span class=\"cms-umb-circle-arrow\">↗</span></a><a href=\"#contact\" class=\"cms-umb-square-card\"><div style=\"font-size:0.75rem;font-weight:700;letter-spacing:0.08em;color:#283a97;opacity:0.7;\">02 / COPILOT</div><div><h3 style=\"margin:0 0 0.75rem;font-size:1.5rem;font-weight:400;color:#283a97;\">智能文案与多语生成</h3><p style=\"margin:0;opacity:0.78;line-height:1.6;\">一键完成行业官网初稿搭建、摘要提炼与跨语种本地化同步翻译。</p></div><span class=\"cms-umb-circle-arrow\">↗</span></a><a href=\"#contact\" class=\"cms-umb-square-card\"><div style=\"font-size:0.75rem;font-weight:700;letter-spacing:0.08em;color:#283a97;opacity:0.7;\">03 / COMPOSABLE</div><div><h3 style=\"margin:0 0 0.75rem;font-size:1.5rem;font-weight:400;color:#283a97;\">可组合开放集成</h3><p style=\"margin:0;opacity:0.78;line-height:1.6;\">无缝对接 OpenAI、DeepSeek、Gemini 与企业私有知识库 RAG 检索。</p></div><span class=\"cms-umb-circle-arrow\">↗</span></a></div>"
            })),
            new("main", "cms.html", "全宽案例横幅卡", Serialize(new
            {
                html = "<div style=\"border-radius:22px;padding:3rem 2.5rem;background:linear-gradient(100deg,rgba(22,35,53,0.92),rgba(40,58,151,0.75)),url('https://images.pexels.com/photos/3183150/pexels-photo-3183150.jpeg?auto=compress&cs=tinysrgb&w=1200') center/cover;color:#f9f7f4;display:flex;flex-wrap:wrap;justify-content:space-between;align-items:center;gap:1.5rem;\"><div style=\"max-width:620px;\"><div style=\"font-size:0.75rem;letter-spacing:0.1em;opacity:0.75;margin-bottom:0.5rem;\">ARCHITECTURE CASE STUDY</div><h3 style=\"margin:0 0 0.75rem;font-size:clamp(1.5rem,2.8vw,2.25rem);font-weight:400;\">企业级可组合 AI 架构实战配方</h3><p style=\"margin:0;opacity:0.86;\">了解头部数字团队如何在 2 周内将智能内容工作流接入现有业务系统。</p></div><a href=\"#contact\" style=\"padding:0.75rem 1.5rem;border-radius:999px;background:#f9f7f4;color:#283a97;text-decoration:none;font-weight:600;\">查看架构案例 ↗</a></div>"
            }))
        ]),
        new("企业痛点", "challenges", true, "single-column",
        [
            new("main", "cms.heading", "破解企业内容管理难题", Serialize(new
            {
                title = "为企业破解复杂内容协同难题",
                subtitle = "兼具编辑自主权、全球多站点治理与全链路合规保障",
                level = "h2",
                align = "center"
            })),
            new("main", "cms.html", "三栏摄影遮罩方卡", Serialize(new
            {
                html = "<div class=\"cms-umb-grid-3\"><a href=\"#contact\" class=\"cms-umb-photo-card\" style=\"background-image:linear-gradient(180deg,rgba(22,35,53,0.12) 0%,rgba(22,35,53,0.88) 100%),url('https://images.pexels.com/photos/1181359/pexels-photo-1181359.jpeg?auto=compress&cs=tinysrgb&w=900');\"><h3 style=\"margin:0 0 0.5rem;font-size:1.5rem;font-weight:400;\">告别僵化模板，运营自主发版</h3><p style=\"margin:0;opacity:0.86;font-size:0.95rem;\">所见即所得块级编排与多语言快照，市场团队独立上线活动页。</p></a><a href=\"#contact\" class=\"cms-umb-photo-card\" style=\"background-image:linear-gradient(180deg,rgba(22,35,53,0.12) 0%,rgba(22,35,53,0.88) 100%),url('https://images.pexels.com/photos/3184328/pexels-photo-3184328.jpeg?auto=compress&cs=tinysrgb&w=900');\"><h3 style=\"margin:0 0 0.5rem;font-size:1.5rem;font-weight:400;\">一套底座管控全球品牌矩阵</h3><p style=\"margin:0;opacity:0.86;font-size:0.95rem;\">独立域名路由、多租户隔离与细粒度权限，统一集团区域门户。</p></a><a href=\"#contact\" class=\"cms-umb-photo-card\" style=\"background-image:linear-gradient(180deg,rgba(22,35,53,0.12) 0%,rgba(22,35,53,0.88) 100%),url('https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900');\"><h3 style=\"margin:0 0 0.5rem;font-size:1.5rem;font-weight:400;\">企业级性能与合规审计保障</h3><p style=\"margin:0;opacity:0.86;font-size:0.95rem;\">不可变发布快照 + 混合缓存 + OpenTelemetry 追踪，稳定如一。</p></a></div>"
            }))
        ]),
        new("视频案例", "video-case", true, "single-column",
        [
            new("main", "cms.heading", "全球品牌信赖之选", Serialize(new
            {
                title = "与全球领先品牌共同打造数字体验",
                subtitle = "观看客户数字化平台负责人分享架构选型与上线成效",
                level = "h2",
                align = "center"
            })),
            new("main", "cms.video", "客户案例视频", Serialize(new
            {
                videoUrl = "https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4",
                posterUrl = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=1200",
                caption = "Case Study · 某跨国制造集团多语门户重构纪实",
                controls = true,
                autoPlay = false,
                loop = false,
                maxHeight = 420
            }))
        ]),
        new("极速实施", "speed", true, "single-column",
        [
            new("main", "cms.heading", "行业领先的实施交付速度", Serialize(new
            {
                title = "业内最快的企业级实施交付速度",
                subtitle = "告别长达半年的单体平台改造，通过热插拔模块与标准契约，数周即可完成首个站点上线",
                level = "h2",
                align = "center"
            })),
            new("main", "cms.button", "1分钟了解按钮", Serialize(new
            {
                text = "1 分钟了解实施路径",
                href = "#contact",
                variant = "fill",
                align = "center",
                openNewTab = false
            }))
        ]),
        new("客户证言", "testimonials", true, "single-column",
        [
            new("main", "cms.testimonials", "决策者证言轮播", Serialize(new
            {
                eyebrow = "FROM CTOS TO CMOS",
                heading = "为什么技术与市场决策者都选择我们",
                variant = "carousel",
                items = new[]
                {
                    new { quote = "模块化架构让我们的研发与运营彻底解耦，市场团队现在能在几小时内推出全新活动页。", author = "陈志远", role = "某智造集团 CTO", avatarUrl = "https://images.pexels.com/photos/220453/pexels-photo-220453.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "多语言无刷新切换和不可变发布快照非常稳健，大促期间零故障支撑全球流量。", author = "林舒雅", role = "某跨境品牌 CMO", avatarUrl = "https://images.pexels.com/photos/774909/pexels-photo-774909.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "源码清晰、契约规范，我们的团队只用两天就完成了自定义业务微件与内部 ERP 打通。", author = "周明轩", role = "某金融科技平台架构师", avatarUrl = "https://images.pexels.com/photos/614810/pexels-photo-614810.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "在私有化合规和安全审计方面表现出色，完全满足集团等保三级要求。", author = "王总监", role = "某能源企业信息中心主任", avatarUrl = "https://images.pexels.com/photos/415829/pexels-photo-415829.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "这是我们用过最友好的内容管理工作台，新同事上手不到半小时就能独立排版。", author = "赵经理", role = "某零售集团数字运营负责人", avatarUrl = "https://images.pexels.com/photos/774909/pexels-photo-774909.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "从传统静态站迁移过来后，官网留资转化率在一个季度内提升了 38%。", author = "刘先生", role = "某 SaaS 创业公司创始人", avatarUrl = "https://images.pexels.com/photos/220453/pexels-photo-220453.jpeg?auto=compress&cs=tinysrgb&w=300" }
                }
            }))
        ]),
        new("深蓝转化", "cta-banner", true, "single-column",
        [
            new("main", "cms.html", "深蓝波浪转化横幅", Serialize(new
            {
                html = "<div style=\"position:relative;overflow:hidden;border-radius:22px;padding:clamp(3rem,6vw,5rem) 2rem;background:#283a97;color:#f9f7f4;text-align:center;\"><svg viewBox=\"0 0 1200 320\" preserveAspectRatio=\"none\" aria-hidden=\"true\" style=\"position:absolute;inset:0;width:100%;height:100%;opacity:0.22;pointer-events:none;\"><path d=\"M0,192 C320,90 520,290 880,170 C1040,115 1140,145 1200,180 L1200,320 L0,320 Z\" fill=\"#1b264f\"/><path d=\"M0,250 C280,180 620,320 1200,210 L1200,320 L0,320 Z\" fill=\"#f5c1bc\" fill-opacity=\"0.35\"/></svg><div style=\"position:relative;z-index:1;\"><h2 style=\"margin:0 0 1rem;font-size:clamp(2rem,4vw,3.5rem);font-weight:400;line-height:1.12;color:#f9f7f4;\">想了解它如何赋能您的企业业务？</h2><p style=\"margin:0 auto 2rem;max-width:580px;font-size:1.1rem;opacity:0.86;\">与我们的解决方案架构师进行 30 分钟探索沟通，获取定制化演示与架构评估。</p><a href=\"#paths\" style=\"display:inline-flex;align-items:center;gap:0.75rem;padding:0.45rem 0.45rem 0.45rem 1.4rem;border-radius:999px;background:#f9f7f4;color:#283a97;text-decoration:none;font-weight:600;\"><span>预约探索沟通</span><span style=\"width:2rem;height:2rem;border-radius:50%;background:#f5c1bc;color:#283a97;display:inline-grid;place-items:center;\">↗</span></a></div></div>"
            }))
        ]),
        new("G2 口碑", "g2-proof", true, "two-column",
        [
            new("left", "cms.html", "G2 认证徽章墙", Serialize(new
            {
                html = "<div style=\"display:grid;grid-template-columns:repeat(2,minmax(130px,1fr));gap:1rem;\"><div style=\"background:#ffffff;border-radius:16px;padding:1.25rem;border:1px solid rgba(40,58,151,0.12);text-align:center;\"><div style=\"font-size:0.7rem;font-weight:700;color:#283a97;letter-spacing:0.08em;\">G2 FALL 2026</div><strong style=\"display:block;margin-top:0.35rem;color:#162335;\">Leader 领导者</strong></div><div style=\"background:#ffffff;border-radius:16px;padding:1.25rem;border:1px solid rgba(40,58,151,0.12);text-align:center;\"><div style=\"font-size:0.7rem;font-weight:700;color:#283a97;letter-spacing:0.08em;\">G2 FALL 2026</div><strong style=\"display:block;margin-top:0.35rem;color:#162335;\">最佳易用性</strong></div><div style=\"background:#ffffff;border-radius:16px;padding:1.25rem;border:1px solid rgba(40,58,151,0.12);text-align:center;\"><div style=\"font-size:0.7rem;font-weight:700;color:#283a97;letter-spacing:0.08em;\">G2 FALL 2026</div><strong style=\"display:block;margin-top:0.35rem;color:#162335;\">最佳实施成效</strong></div><div style=\"background:#ffffff;border-radius:16px;padding:1.25rem;border:1px solid rgba(40,58,151,0.12);text-align:center;\"><div style=\"font-size:0.7rem;font-weight:700;color:#283a97;letter-spacing:0.08em;\">G2 FALL 2026</div><strong style=\"display:block;margin-top:0.35rem;color:#162335;\">最易合作平台</strong></div></div>"
            })),
            new("right", "cms.richtext", "G2 用户口碑说明", Serialize(new
            {
                heading = "真实用户评价胜过千言万语",
                body = "连续多年入选企业级 CMS 领导者象限，在易用性、交付速度与开放扩展维度获得一线工程师与运营团队的一致高分。"
            })),
            new("right", "cms.button", "查看对比报告", Serialize(new { text = "查看产品对比详情", href = "#contact", variant = "outline", align = "left", openNewTab = false }))
        ]),
        new("角色路径", "paths", true, "single-column",
        [
            new("main", "cms.heading", "选择您的下一步路径", Serialize(new
            {
                title = "选择您的下一步探索路径",
                subtitle = "无论您是开发者、市场运营还是合作伙伴，都能找到专属入口",
                level = "h2",
                align = "center"
            })),
            new("main", "cms.html", "三栏深蓝路径卡片", Serialize(new
            {
                html = "<div style=\"display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:1.25rem;\"><a href=\"#contact\" style=\"text-decoration:none;background:#283a97;color:#f9f7f4;border-radius:20px;padding:2rem;display:flex;flex-direction:column;justify-content:space-between;min-height:220px;\"><span style=\"width:2.5rem;height:2.5rem;border-radius:50%;background:#f9f7f4;color:#283a97;display:grid;place-items:center;font-weight:700;\">&lt;/&gt;</span><div><h3 style=\"margin:1.5rem 0 0.5rem;font-weight:400;font-size:1.5rem;\">开发者 Developer</h3><p style=\"margin:0;opacity:0.82;font-size:0.95rem;\">探索开源 SDK、热插拔模块契约与多数据库迁移能力。</p></div><span style=\"align-self:flex-end;width:2rem;height:2rem;border-radius:50%;border:1px solid rgba(249,247,244,0.35);display:grid;place-items:center;\">↗</span></a><a href=\"#contact\" style=\"text-decoration:none;background:#283a97;color:#f9f7f4;border-radius:20px;padding:2rem;display:flex;flex-direction:column;justify-content:space-between;min-height:220px;\"><span style=\"width:2.5rem;height:2.5rem;border-radius:50%;background:#f9f7f4;color:#283a97;display:grid;place-items:center;font-weight:700;\">✦</span><div><h3 style=\"margin:1.5rem 0 0.5rem;font-weight:400;font-size:1.5rem;\">市场运营 Marketer</h3><p style=\"margin:0;opacity:0.82;font-size:0.95rem;\">体验可视化拖拽建站、多语种即时切换与留资转化追踪。</p></div><span style=\"align-self:flex-end;width:2rem;height:2rem;border-radius:50%;border:1px solid rgba(249,247,244,0.35);display:grid;place-items:center;\">↗</span></a><a href=\"#contact\" style=\"text-decoration:none;background:#283a97;color:#f9f7f4;border-radius:20px;padding:2rem;display:flex;flex-direction:column;justify-content:space-between;min-height:220px;\"><span style=\"width:2.5rem;height:2.5rem;border-radius:50%;background:#f9f7f4;color:#283a97;display:grid;place-items:center;font-weight:700;\">◈</span><div><h3 style=\"margin:1.5rem 0 0.5rem;font-weight:400;font-size:1.5rem;\">生态伙伴 Agency</h3><p style=\"margin:0;opacity:0.82;font-size:0.95rem;\">基于标准化多站点平台，为客户更快交付定制化数字门户。</p></div><span style=\"align-self:flex-end;width:2rem;height:2rem;border-radius:50%;border:1px solid rgba(249,247,244,0.35);display:grid;place-items:center;\">↗</span></a></div>"
            })),
            new("main", "cms.contact", "预约留资表单", Serialize(new
            {
                heading = "开启您的企业级数字体验之旅",
                subheading = "填写以下信息，立即获取演示环境与架构白皮书",
                submitText = "预约探索沟通",
                email = "enterprise@example.com",
                phone = "400-900-2026"
            }))
        ]),
        new("页脚", "footer", false, "single-column",
        [
            new("main", "cms.html", "四栏 Sitemap 页脚", Serialize(new
            {
                html = "<div style=\"display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:2rem;color:#f9f7f4;padding-bottom:1.5rem;border-bottom:1px solid rgba(186,188,213,0.2);\"><div><div style=\"font-size:0.75rem;font-weight:700;letter-spacing:0.1em;color:#babcd5;margin-bottom:0.75rem;\">PLATFORM &amp; HOSTING</div><div style=\"display:flex;flex-direction:column;gap:0.45rem;font-size:0.9rem;\"><a href=\"#ai-platform\" style=\"color:#f9f7f4;text-decoration:none;\">OpenCMS 核心</a><a href=\"#challenges\" style=\"color:#f9f7f4;text-decoration:none;\">多站点云托管</a><a href=\"#speed\" style=\"color:#f9f7f4;text-decoration:none;\">极速实施服务</a></div></div><div><div style=\"font-size:0.75rem;font-weight:700;letter-spacing:0.1em;color:#babcd5;margin-bottom:0.75rem;\">ENTERPRISE</div><div style=\"display:flex;flex-direction:column;gap:0.45rem;font-size:0.9rem;\"><a href=\"#g2-proof\" style=\"color:#f9f7f4;text-decoration:none;\">G2 客户评价</a><a href=\"#testimonials\" style=\"color:#f9f7f4;text-decoration:none;\">决策者证言</a><a href=\"#paths\" style=\"color:#f9f7f4;text-decoration:none;\">安全合规保障</a></div></div><div><div style=\"font-size:0.75rem;font-weight:700;letter-spacing:0.1em;color:#babcd5;margin-bottom:0.75rem;\">DEVELOP</div><div style=\"display:flex;flex-direction:column;gap:0.45rem;font-size:0.9rem;\"><a href=\"#ai-platform\" style=\"color:#f9f7f4;text-decoration:none;\">MCP &amp; AI 文档</a><a href=\"#paths\" style=\"color:#f9f7f4;text-decoration:none;\">模块开发指南</a><a href=\"#video-case\" style=\"color:#f9f7f4;text-decoration:none;\">架构案例库</a></div></div><div><div style=\"font-size:0.75rem;font-weight:700;letter-spacing:0.1em;color:#babcd5;margin-bottom:0.75rem;\">CONNECT</div><div style=\"display:flex;flex-direction:column;gap:0.45rem;font-size:0.9rem;\"><a href=\"#paths\" style=\"color:#f9f7f4;text-decoration:none;\">预约探索沟通</a><a href=\"#paths\" style=\"color:#f9f7f4;text-decoration:none;\">合作伙伴计划</a><a href=\"#home\" style=\"color:#f9f7f4;text-decoration:none;\">返回顶部 ↑</a></div></div></div>"
            })),
            new("main", "cms.footer", "Umbraco 风格页脚", Serialize(new
            {
                brand,
                tagline = $"{industry} · Friendly, Composable & Ready for AI",
                copyright = $"© {DateTime.UtcNow.Year} {brand}. All rights reserved."
            }))
        ])
    ];

    private static List<CmsAiSectionDraft> UmbracoEn(string brand, string industry, string? stylePrompt) =>
    [
        new("Home", "home", true, "single-column",
        [
            new("main", "cms.html", "Top Announcement Pill", Serialize(new
            {
                html = "<div style=\"display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:0.75rem;background:#283a97;color:#ffffff;padding:0.65rem 1.35rem;border-radius:999px;font-size:0.8125rem;margin-bottom:1.25rem;\"><span><strong>Sign up for Winter Keynote</strong> · Feb 2, 16:30 CET · AI-Native Composable CMS</span><a href=\"#paths\" style=\"color:#f5c1bc;text-decoration:none;font-weight:600;\">Save the date →</a></div>"
            })),
            new("main", "cms.hero", $"{brand} Editorial Hero", Serialize(new
            {
                badge = "2026 WINTER RELEASE · READY FOR AI",
                headline = $"{brand}: the open-source .NET CMS, ready for AI",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt)
                    ? "Combine developer freedom with an intuitive editorial workspace to deliver composable digital experiences at scale."
                    : stylePrompt.Trim(),
                primaryButtonText = "Book a Discovery Call",
                primaryButtonHref = "#paths",
                backgroundImageUrl = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=1200",
                variant = "editorial"
            }))
        ]),
        new("Customers", "logos", true, "single-column",
        [
            new("main", "cms.partners", "Customer Logo Marquee", Serialize(new
            {
                heading = "",
                variant = "marquee",
                items = new[]
                {
                    new { name = "Mercedes-Benz" },
                    new { name = "Carlsberg" },
                    new { name = "Fujitsu" },
                    new { name = "Renault" },
                    new { name = "Volvo" },
                    new { name = "Heineken" },
                    new { name = "Suzuki" },
                    new { name = "Domino's" }
                }
            }))
        ]),
        new("AI Platform", "ai-platform", true, "single-column",
        [
            new("main", "cms.html", "AI Split Heading", Serialize(new
            {
                html = "<div style=\"display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:1.5rem;align-items:end;margin-bottom:1.75rem;\"><div style=\"font-size:0.75rem;font-weight:700;letter-spacing:0.12em;text-transform:uppercase;color:#283a97;\">OPEN DXP &amp; AI</div><h2 style=\"margin:0;font-size:clamp(2rem,3.8vw,3.5rem);font-weight:400;line-height:1.12;color:#283a97;\">AI Everywhere Your Content Lives</h2></div>"
            })),
            new("main", "cms.html", "AI 1:1 Square Cards", Serialize(new
            {
                html = "<div class=\"cms-umb-grid-3\"><a href=\"#paths\" class=\"cms-umb-square-card\"><div style=\"font-size:0.75rem;font-weight:700;letter-spacing:0.08em;color:#283a97;opacity:0.7;\">01 / MCP NATIVE</div><div><h3 style=\"margin:0 0 0.75rem;font-size:1.5rem;font-weight:400;color:#283a97;\">MCP Native Readiness</h3><p style=\"margin:0;opacity:0.78;line-height:1.6;\">Let AI agents understand your schemas and compose sections safely with strict contracts.</p></div><span class=\"cms-umb-circle-arrow\">↗</span></a><a href=\"#paths\" class=\"cms-umb-square-card\"><div style=\"font-size:0.75rem;font-weight:700;letter-spacing:0.08em;color:#283a97;opacity:0.7;\">02 / COPILOT</div><div><h3 style=\"margin:0 0 0.75rem;font-size:1.5rem;font-weight:400;color:#283a97;\">Smart Copy &amp; Localization</h3><p style=\"margin:0;opacity:0.78;line-height:1.6;\">Draft full landing pages, summarize articles, and translate seamlessly across locales.</p></div><span class=\"cms-umb-circle-arrow\">↗</span></a><a href=\"#paths\" class=\"cms-umb-square-card\"><div style=\"font-size:0.75rem;font-weight:700;letter-spacing:0.08em;color:#283a97;opacity:0.7;\">03 / COMPOSABLE</div><div><h3 style=\"margin:0 0 0.75rem;font-size:1.5rem;font-weight:400;color:#283a97;\">Composable Integrations</h3><p style=\"margin:0;opacity:0.78;line-height:1.6;\">Connect OpenAI, Gemini, Azure, and private enterprise RAG knowledge bases.</p></div><span class=\"cms-umb-circle-arrow\">↗</span></a></div>"
            })),
            new("main", "cms.html", "AI Case Study Banner", Serialize(new
            {
                html = "<div style=\"border-radius:22px;padding:3rem 2.5rem;background:linear-gradient(100deg,rgba(22,35,53,0.92),rgba(40,58,151,0.75)),url('https://images.pexels.com/photos/3183150/pexels-photo-3183150.jpeg?auto=compress&cs=tinysrgb&w=1200') center/cover;color:#f9f7f4;display:flex;flex-wrap:wrap;justify-content:space-between;align-items:center;gap:1.5rem;\"><div style=\"max-width:620px;\"><div style=\"font-size:0.75rem;letter-spacing:0.1em;opacity:0.75;margin-bottom:0.5rem;\">ARCHITECTURE CASE STUDY</div><h3 style=\"margin:0 0 0.75rem;font-size:clamp(1.5rem,2.8vw,2.25rem);font-weight:400;\">The Architectural 'Recipe' for AI on Your CMS</h3><p style=\"margin:0;opacity:0.86;\">See how modern digital teams ship AI-assisted publishing workflows in under two weeks.</p></div><a href=\"#paths\" style=\"padding:0.75rem 1.5rem;border-radius:999px;background:#f9f7f4;color:#283a97;text-decoration:none;font-weight:600;\">See case study ↗</a></div>"
            }))
        ]),
        new("Challenges", "challenges", true, "single-column",
        [
            new("main", "cms.heading", "Solving Content Challenges", Serialize(new
            {
                title = "Solving Content Challenges for Enterprises",
                subtitle = "Editorial autonomy, global multi-site governance, and built-in observability",
                level = "h2",
                align = "center"
            })),
            new("main", "cms.html", "3-Col Photographic Overlay Cards", Serialize(new
            {
                html = "<div class=\"cms-umb-grid-3\"><a href=\"#paths\" class=\"cms-umb-photo-card\" style=\"background-image:linear-gradient(180deg,rgba(22,35,53,0.12) 0%,rgba(22,35,53,0.88) 100%),url('https://images.pexels.com/photos/1181359/pexels-photo-1181359.jpeg?auto=compress&cs=tinysrgb&w=900');\"><h3 style=\"margin:0 0 0.5rem;font-size:1.5rem;font-weight:400;\">Empower Editors Without Bottlenecks</h3><p style=\"margin:0;opacity:0.86;font-size:0.95rem;\">Visual block composition and multi-language snapshots let marketers launch independently.</p></a><a href=\"#paths\" class=\"cms-umb-photo-card\" style=\"background-image:linear-gradient(180deg,rgba(22,35,53,0.12) 0%,rgba(22,35,53,0.88) 100%),url('https://images.pexels.com/photos/3184328/pexels-photo-3184328.jpeg?auto=compress&cs=tinysrgb&w=900');\"><h3 style=\"margin:0 0 0.5rem;font-size:1.5rem;font-weight:400;\">Manage Global Brands from One Core</h3><p style=\"margin:0;opacity:0.86;font-size:0.95rem;\">Custom domain routing, tenant isolation, and granular RBAC keep portals unified.</p></a><a href=\"#paths\" class=\"cms-umb-photo-card\" style=\"background-image:linear-gradient(180deg,rgba(22,35,53,0.12) 0%,rgba(22,35,53,0.88) 100%),url('https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900');\"><h3 style=\"margin:0 0 0.5rem;font-size:1.5rem;font-weight:400;\">Built-In Performance &amp; Observability</h3><p style=\"margin:0;opacity:0.86;font-size:0.95rem;\">Immutable snapshots, hybrid caching, and OpenTelemetry tracing ensure reliability.</p></a></div>"
            }))
        ]),
        new("Video Story", "video-case", true, "single-column",
        [
            new("main", "cms.heading", "Global Brands Trust Us", Serialize(new
            {
                title = "Join the Global Brands Who Trust Our Platform",
                subtitle = "Hear how digital platform leads modernize multi-brand delivery",
                level = "h2",
                align = "center"
            })),
            new("main", "cms.video", "Customer Case Video", Serialize(new
            {
                videoUrl = "https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4",
                posterUrl = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=1200",
                caption = "Case Study · Global Manufacturer Multi-Region Portal",
                controls = true,
                autoPlay = false,
                loop = false,
                maxHeight = 420
            }))
        ]),
        new("Fast Delivery", "speed", true, "single-column",
        [
            new("main", "cms.heading", "Market's Fastest Implementation", Serialize(new
            {
                title = "Market's Fastest Implementation",
                subtitle = "Skip 6-month monolith rollouts — ship your first composable portal in weeks with hot-pluggable modules",
                level = "h2",
                align = "center"
            })),
            new("main", "cms.button", "1-Minute Overview", Serialize(new
            {
                text = "Understand how in just 1 minute",
                href = "#paths",
                variant = "fill",
                align = "center",
                openNewTab = false
            }))
        ]),
        new("Testimonials", "testimonials", true, "single-column",
        [
            new("main", "cms.testimonials", "Decision Makers Carousel", Serialize(new
            {
                eyebrow = "FROM CTOS TO CMOS",
                heading = "Why Decision Makers Like You Choose Our CMS",
                variant = "carousel",
                items = new[]
                {
                    new { quote = "Modular architecture decoupled engineering from marketing — our editors now launch campaign pages in hours.", author = "David Chen", role = "CTO, SmartTech Group", avatarUrl = "https://images.pexels.com/photos/220453/pexels-photo-220453.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "Instant culture switching and immutable snapshots kept our global storefronts rock-solid during peak traffic.", author = "Elena Lin", role = "CMO, Global Retail", avatarUrl = "https://images.pexels.com/photos/774909/pexels-photo-774909.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "Clean contracts and SDKs let our engineers wire custom widgets into our ERP in just two days.", author = "Marcus Zhou", role = "Principal Architect, FinTech", avatarUrl = "https://images.pexels.com/photos/614810/pexels-photo-614810.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "Self-hosted deployment and full audit trails met every enterprise compliance requirement out of the box.", author = "Director Wang", role = "Head of IT Governance", avatarUrl = "https://images.pexels.com/photos/415829/pexels-photo-415829.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "The most intuitive editing workspace our content team has ever used — onboarding takes under 30 minutes.", author = "Sarah Zhao", role = "Digital Operations Lead", avatarUrl = "https://images.pexels.com/photos/774909/pexels-photo-774909.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "Lead conversion jumped 38% in one quarter after moving our flagship site to this composable setup.", author = "Alex Liu", role = "Founder, CloudSaaS", avatarUrl = "https://images.pexels.com/photos/220453/pexels-photo-220453.jpeg?auto=compress&cs=tinysrgb&w=300" }
                }
            }))
        ]),
        new("Discovery CTA", "cta-banner", true, "single-column",
        [
            new("main", "cms.html", "Royal Blue CTA Banner", Serialize(new
            {
                html = "<div style=\"position:relative;overflow:hidden;border-radius:22px;padding:clamp(3rem,6vw,5rem) 2rem;background:#283a97;color:#f9f7f4;text-align:center;\"><svg viewBox=\"0 0 1200 320\" preserveAspectRatio=\"none\" aria-hidden=\"true\" style=\"position:absolute;inset:0;width:100%;height:100%;opacity:0.22;pointer-events:none;\"><path d=\"M0,192 C320,90 520,290 880,170 C1040,115 1140,145 1200,180 L1200,320 L0,320 Z\" fill=\"#1b264f\"/><path d=\"M0,250 C280,180 620,320 1200,210 L1200,320 L0,320 Z\" fill=\"#f5c1bc\" fill-opacity=\"0.35\"/></svg><div style=\"position:relative;z-index:1;\"><h2 style=\"margin:0 0 1rem;font-size:clamp(2rem,4vw,3.5rem);font-weight:400;line-height:1.12;color:#f9f7f4;\">Curious How It Could Work for Your Business?</h2><p style=\"margin:0 auto 2rem;max-width:580px;font-size:1.1rem;opacity:0.86;\">Book a 30-minute discovery call with our solutions architects for a tailored walkthrough.</p><a href=\"#paths\" style=\"display:inline-flex;align-items:center;gap:0.75rem;padding:0.45rem 0.45rem 0.45rem 1.4rem;border-radius:999px;background:#f9f7f4;color:#283a97;text-decoration:none;font-weight:600;\"><span>Start with a Discovery Call</span><span style=\"width:2rem;height:2rem;border-radius:50%;background:#f5c1bc;color:#283a97;display:inline-grid;place-items:center;\">↗</span></a></div></div>"
            }))
        ]),
        new("G2 Proof", "g2-proof", true, "two-column",
        [
            new("left", "cms.html", "G2 Award Badges", Serialize(new
            {
                html = "<div style=\"display:grid;grid-template-columns:repeat(2,minmax(130px,1fr));gap:1rem;\"><div style=\"background:#ffffff;border-radius:16px;padding:1.25rem;border:1px solid rgba(40,58,151,0.12);text-align:center;\"><div style=\"font-size:0.7rem;font-weight:700;color:#283a97;letter-spacing:0.08em;\">G2 FALL 2026</div><strong style=\"display:block;margin-top:0.35rem;color:#162335;\">Leader</strong></div><div style=\"background:#ffffff;border-radius:16px;padding:1.25rem;border:1px solid rgba(40,58,151,0.12);text-align:center;\"><div style=\"font-size:0.7rem;font-weight:700;color:#283a97;letter-spacing:0.08em;\">G2 FALL 2026</div><strong style=\"display:block;margin-top:0.35rem;color:#162335;\">Best Usability</strong></div><div style=\"background:#ffffff;border-radius:16px;padding:1.25rem;border:1px solid rgba(40,58,151,0.12);text-align:center;\"><div style=\"font-size:0.7rem;font-weight:700;color:#283a97;letter-spacing:0.08em;\">G2 FALL 2026</div><strong style=\"display:block;margin-top:0.35rem;color:#162335;\">Best Results</strong></div><div style=\"background:#ffffff;border-radius:16px;padding:1.25rem;border:1px solid rgba(40,58,151,0.12);text-align:center;\"><div style=\"font-size:0.7rem;font-weight:700;color:#283a97;letter-spacing:0.08em;\">G2 FALL 2026</div><strong style=\"display:block;margin-top:0.35rem;color:#162335;\">Easiest To Do Business With</strong></div></div>"
            })),
            new("right", "cms.richtext", "Our Users Say It Best", Serialize(new
            {
                heading = "Our Users Say It Best",
                body = "Recognized by real enterprise teams on G2 across usability, implementation speed, and composable extensibility."
            })),
            new("right", "cms.button", "Compare CTA", Serialize(new { text = "See how we compare", href = "#paths", variant = "outline", align = "left", openNewTab = false }))
        ]),
        new("Choose Path", "paths", true, "single-column",
        [
            new("main", "cms.heading", "Choose Your Path Forward", Serialize(new
            {
                title = "Choose Your Path Forward",
                subtitle = "Whether you're building, marketing, or delivering for clients, start here",
                level = "h2",
                align = "center"
            })),
            new("main", "cms.html", "Royal Blue Path Cards", Serialize(new
            {
                html = "<div style=\"display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:1.25rem;\"><a href=\"#paths\" style=\"text-decoration:none;background:#283a97;color:#f9f7f4;border-radius:20px;padding:2rem;display:flex;flex-direction:column;justify-content:space-between;min-height:220px;\"><span style=\"width:2.5rem;height:2.5rem;border-radius:50%;background:#f9f7f4;color:#283a97;display:grid;place-items:center;font-weight:700;\">&lt;/&gt;</span><div><h3 style=\"margin:1.5rem 0 0.5rem;font-weight:400;font-size:1.5rem;\">Developer</h3><p style=\"margin:0;opacity:0.82;font-size:0.95rem;\">Explore open SDKs, hot-pluggable module contracts, and multi-DB support.</p></div><span style=\"align-self:flex-end;width:2rem;height:2rem;border-radius:50%;border:1px solid rgba(249,247,244,0.35);display:grid;place-items:center;\">↗</span></a><a href=\"#paths\" style=\"text-decoration:none;background:#283a97;color:#f9f7f4;border-radius:20px;padding:2rem;display:flex;flex-direction:column;justify-content:space-between;min-height:220px;\"><span style=\"width:2.5rem;height:2.5rem;border-radius:50%;background:#f9f7f4;color:#283a97;display:grid;place-items:center;font-weight:700;\">✦</span><div><h3 style=\"margin:1.5rem 0 0.5rem;font-weight:400;font-size:1.5rem;\">Marketer</h3><p style=\"margin:0;opacity:0.82;font-size:0.95rem;\">Try visual drag-and-drop editing, multi-language sites, and lead capture.</p></div><span style=\"align-self:flex-end;width:2rem;height:2rem;border-radius:50%;border:1px solid rgba(249,247,244,0.35);display:grid;place-items:center;\">↗</span></a><a href=\"#paths\" style=\"text-decoration:none;background:#283a97;color:#f9f7f4;border-radius:20px;padding:2rem;display:flex;flex-direction:column;justify-content:space-between;min-height:220px;\"><span style=\"width:2.5rem;height:2.5rem;border-radius:50%;background:#f9f7f4;color:#283a97;display:grid;place-items:center;font-weight:700;\">◈</span><div><h3 style=\"margin:1.5rem 0 0.5rem;font-weight:400;font-size:1.5rem;\">Agency</h3><p style=\"margin:0;opacity:0.82;font-size:0.95rem;\">Deliver client portals faster on a standardized, extensible foundation.</p></div><span style=\"align-self:flex-end;width:2rem;height:2rem;border-radius:50%;border:1px solid rgba(249,247,244,0.35);display:grid;place-items:center;\">↗</span></a></div>"
            })),
            new("main", "cms.contact", "Discovery Form", Serialize(new
            {
                heading = "Start Your Composable Digital Journey",
                subheading = "Leave your details to access a live sandbox and architecture guide",
                submitText = "Book a Discovery Call",
                email = "enterprise@example.com",
                phone = "+1-800-555-2026"
            }))
        ]),
        new("Footer", "footer", false, "single-column",
        [
            new("main", "cms.html", "4-Column Sitemap Footer", Serialize(new
            {
                html = "<div style=\"display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:2rem;color:#f9f7f4;padding-bottom:1.5rem;border-bottom:1px solid rgba(186,188,213,0.2);\"><div><div style=\"font-size:0.75rem;font-weight:700;letter-spacing:0.1em;color:#babcd5;margin-bottom:0.75rem;\">PLATFORM &amp; HOSTING</div><div style=\"display:flex;flex-direction:column;gap:0.45rem;font-size:0.9rem;\"><a href=\"#ai-platform\" style=\"color:#f9f7f4;text-decoration:none;\">OpenCMS Core</a><a href=\"#challenges\" style=\"color:#f9f7f4;text-decoration:none;\">Cloud Hosting</a><a href=\"#speed\" style=\"color:#f9f7f4;text-decoration:none;\">Fast Delivery</a></div></div><div><div style=\"font-size:0.75rem;font-weight:700;letter-spacing:0.1em;color:#babcd5;margin-bottom:0.75rem;\">ENTERPRISE</div><div style=\"display:flex;flex-direction:column;gap:0.45rem;font-size:0.9rem;\"><a href=\"#g2-proof\" style=\"color:#f9f7f4;text-decoration:none;\">G2 Reviews</a><a href=\"#testimonials\" style=\"color:#f9f7f4;text-decoration:none;\">Decision Makers</a><a href=\"#paths\" style=\"color:#f9f7f4;text-decoration:none;\">Security &amp; Audit</a></div></div><div><div style=\"font-size:0.75rem;font-weight:700;letter-spacing:0.1em;color:#babcd5;margin-bottom:0.75rem;\">DEVELOP</div><div style=\"display:flex;flex-direction:column;gap:0.45rem;font-size:0.9rem;\"><a href=\"#ai-platform\" style=\"color:#f9f7f4;text-decoration:none;\">MCP &amp; AI Docs</a><a href=\"#paths\" style=\"color:#f9f7f4;text-decoration:none;\">Module SDK</a><a href=\"#video-case\" style=\"color:#f9f7f4;text-decoration:none;\">Case Studies</a></div></div><div><div style=\"font-size:0.75rem;font-weight:700;letter-spacing:0.1em;color:#babcd5;margin-bottom:0.75rem;\">CONNECT</div><div style=\"display:flex;flex-direction:column;gap:0.45rem;font-size:0.9rem;\"><a href=\"#paths\" style=\"color:#f9f7f4;text-decoration:none;\">Book a Call</a><a href=\"#paths\" style=\"color:#f9f7f4;text-decoration:none;\">Partners</a><a href=\"#home\" style=\"color:#f9f7f4;text-decoration:none;\">Back to Top ↑</a></div></div></div>"
            })),
            new("main", "cms.footer", "Editorial Footer", Serialize(new
            {
                brand,
                tagline = $"{industry} · Friendly, Composable & Ready for AI",
                copyright = $"© {DateTime.UtcNow.Year} {brand}. All rights reserved."
            }))
        ])
    ];
}
