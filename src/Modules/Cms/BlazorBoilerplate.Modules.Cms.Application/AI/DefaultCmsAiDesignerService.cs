using System.Text.Json;
using BlazorBoilerplate.Modules.Cms.Application.DTOs;
using BlazorBoilerplate.Modules.Cms.Domain.ValueObjects;

namespace BlazorBoilerplate.Modules.Cms.Application.AI;

/// <summary>
/// 默认 AI 官网设计器实现（Schema 驱动的模板化占位器）：
/// 支持按请求语言（zh-CN / en-US）生成含页脚在内的完整单页官网结构。
/// </summary>
public sealed class DefaultCmsAiDesignerService : ICmsAiDesignerService
{
    public Task<CmsAiSiteDraft> GenerateSiteAsync(CmsAiGenerateSiteRequest request, CancellationToken ct = default)
    {
        var isEn = request.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        var brand = string.IsNullOrWhiteSpace(request.BrandName) ? (isEn ? "Official Site" : "企业官网") : request.BrandName.Trim();
        var industry = string.IsNullOrWhiteSpace(request.Industry) ? (isEn ? "Digital Platform" : "科技与数字化") : request.Industry.Trim();
        var primary = string.IsNullOrWhiteSpace(request.PrimaryColor) ? "#0f6cbd" : request.PrimaryColor.Trim();
        var theme = new CmsThemeConfig(PrimaryColor: primary, AccentColor: primary);

        var sections = isEn
            ? BuildEnglishSections(brand, industry, request.StylePrompt)
            : BuildChineseSections(brand, industry, request.StylePrompt);

        return Task.FromResult(new CmsAiSiteDraft(
            Title: isEn ? $"{brand} Official Website" : $"{brand} 官方网站",
            Description: $"{brand} - {industry}",
            Theme: theme,
            Sections: sections,
            ProviderNote: "当前由默认 Schema 占位生成；接入 LLM Provider 后可替换 ICmsAiDesignerService 实现。"));
    }

    private static List<CmsAiSectionDraft> BuildChineseSections(string brand, string industry, string? stylePrompt) =>
    [
        new("首页", "home", true, "single-column",
        [
            new("main", "cms.hero", $"{brand} 首屏横幅", JsonSerializer.Serialize(new
            {
                badge = industry,
                headline = $"{brand} · 面向未来的{industry}解决方案",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt)
                    ? "以专业、可靠与高颜值体验助力业务持续增长。"
                    : stylePrompt.Trim(),
                primaryButtonText = "立即咨询",
                primaryButtonHref = "#contact",
                secondaryButtonText = "了解优势",
                secondaryButtonHref = "#features"
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.carousel", "产品视觉轮播", JsonSerializer.Serialize(new
            {
                height = 420,
                autoPlay = true,
                intervalMs = 5000,
                slides = new[]
                {
                    new { imageUrl = "https://images.unsplash.com/photo-1497366216548-37526070297c?auto=format&fit=crop&w=1400&q=80", title = $"{brand} 数字化工作空间", subtitle = "现代化、模块化的企业门户与业务协同平台", buttonText = "探索方案", buttonHref = "#features" },
                    new { imageUrl = "https://images.unsplash.com/photo-1551434678-e076c223a692?auto=format&fit=crop&w=1400&q=80", title = "高效研发与敏捷交付", subtitle = "所见即所得可视化设计，助力品牌快速上线", buttonText = "联系我们", buttonHref = "#contact" }
                }
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("核心能力", "features", true, "single-column",
        [
            new("main", "cms.heading", "能力总览标题", JsonSerializer.Serialize(new
            {
                title = $"为什么选择 {brand}",
                subtitle = $"深耕{industry}，提供端到端可信赖服务",
                level = "h2",
                align = "center"
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.text", "能力导语段落", JsonSerializer.Serialize(new
            {
                text = "通过热插拔模块化架构与细粒度微件体系，企业可以在同一底座上灵活组合门户页面、数字业务与客户触点。",
                align = "center",
                fontSize = "1rem"
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.button", "方案咨询按钮", JsonSerializer.Serialize(new
            {
                text = "预约产品演示",
                href = "#contact",
                variant = "fill",
                align = "center",
                openNewTab = false
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.features", "核心能力矩阵", JsonSerializer.Serialize(new
            {
                heading = "核心技术优势",
                subheading = "兼顾高性能、高安全与极致的扩展能力",
                items = new[]
                {
                    new { icon = "Rocket", title = "敏捷交付", description = "模块化架构快速响应业务演进，缩短上线周期。" },
                    new { icon = "Shield", title = "安全合规", description = "全链路权限隔离与审计追踪，保障企业数据资产。" },
                    new { icon = "Check", title = "智能体验", description = "兼顾设计美感与交互流畅度，全面适配桌面与移动端。" }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.tabs", "行业解决方案", JsonSerializer.Serialize(new
            {
                heading = "行业解决方案",
                items = new[]
                {
                    new { title = "智能制造", content = "打通生产排程、设备联网与质量追溯全流程，提升产线协同效率。", imageUrl = "https://images.unsplash.com/photo-1581091226825-a6a2a5aee158?auto=format&fit=crop&w=900&q=80" },
                    new { title = "数字政务", content = "高安全合规架构，支持多租户隔离与细粒度权限审计管控。", imageUrl = "https://images.unsplash.com/photo-1486406146926-c627a92ad1ab?auto=format&fit=crop&w=900&q=80" },
                    new { title = "现代零售", content = "全渠道会员与订单协同，轻松支撑大促高并发业务场景。", imageUrl = "https://images.unsplash.com/photo-1556742049-0cfed4f6a45d?auto=format&fit=crop&w=900&q=80" }
                }
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("关于我们", "about", true, "two-column",
        [
            new("left", "cms.richtext", "品牌故事", JsonSerializer.Serialize(new
            {
                heading = $"关于 {brand}",
                body = $"{brand}专注于{industry}领域的技术创新与客户成功，服务众多行业领先企业。",
                imageUrl = "https://images.unsplash.com/photo-1522071820081-009f0129c71c?auto=format&fit=crop&w=1000&q=80"
            }, CmsDtoMapper.JsonOptions)),
            new("left", "cms.html", "自定义亮点块", JsonSerializer.Serialize(new
            {
                html = "<div style=\"padding:1rem 1.25rem;border-left:4px solid var(--cms-pri,#0f6cbd);background:rgba(15,108,189,0.06);border-radius:8px;\"><strong>7×24 专属技术支持</strong><div style=\"opacity:.8;font-size:.9rem;margin-top:4px;\">支持私有化部署、源码交付与模块按需扩展。</div></div>"
            }, CmsDtoMapper.JsonOptions)),
            new("right", "cms.stats", "核心指标", JsonSerializer.Serialize(new
            {
                items = new[]
                {
                    new { value = "99.9%", label = "服务可用性" },
                    new { value = "500+", label = "合作客户" },
                    new { value = "24/7", label = "专业支持" }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("right", "cms.video", "企业宣传视频", JsonSerializer.Serialize(new
            {
                videoUrl = "https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4",
                posterUrl = "https://images.unsplash.com/photo-1519389950473-47ba0277781c?auto=format&fit=crop&w=1000&q=80",
                caption = "平台能力与可视化搭建演示",
                controls = true,
                autoPlay = false,
                loop = false,
                maxHeight = 260
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("案例与生态", "showcase", true, "single-column",
        [
            new("main", "cms.image", "办公环境展示", JsonSerializer.Serialize(new
            {
                imageUrl = "https://images.unsplash.com/photo-1497215728101-856f4ea42174?auto=format&fit=crop&w=1200&q=80",
                alt = $"{brand} 研发中心",
                caption = $"{brand} 研发与客户成功中心",
                maxHeight = 360,
                rounded = true,
                shadow = true
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.gallery", "精选项目画廊", JsonSerializer.Serialize(new
            {
                heading = "项目与团队风采",
                items = new[]
                {
                    new { url = "https://images.unsplash.com/photo-1460925895917-afdab827c52f?auto=format&fit=crop&w=800&q=80", caption = "数据分析驾驶舱" },
                    new { url = "https://images.unsplash.com/photo-1531482615713-2afd69097998?auto=format&fit=crop&w=800&q=80", caption = "架构师团队协作" },
                    new { url = "https://images.unsplash.com/photo-1504384308090-c894fdcc538d?auto=format&fit=crop&w=800&q=80", caption = "敏捷交付现场" }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.pricing", "服务与版本定价", JsonSerializer.Serialize(new
            {
                heading = "灵活的方案定价",
                subheading = "按企业规模按需选择，随时平滑升级",
                plans = new[]
                {
                    new { name = "基础版", price = "¥1,999", period = "/ 年", badge = "", highlighted = false, features = new[] { "单站点独立部署", "内置核心微件库", "标准工单支持" }, buttonText = "了解详情", buttonHref = "#contact" },
                    new { name = "企业版", price = "¥5,999", period = "/ 年", badge = "最受欢迎", highlighted = true, features = new[] { "多站点与独立域名", "AI 官网生成与高德地图", "7×24 专属技术顾问" }, buttonText = "立即咨询", buttonHref = "#contact" },
                    new { name = "旗舰定制版", price = "按需报价", period = "", badge = "", highlighted = false, features = new[] { "私有化集群与源码授权", "专属模块深度定制", "SLA 99.99% 保障" }, buttonText = "预约沟通", buttonHref = "#contact" }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.testimonials", "客户真实评价", JsonSerializer.Serialize(new
            {
                heading = "客户信赖与评价",
                items = new[]
                {
                    new { quote = "上线效率非常高，可视化设计器让运营团队也能独立维护官网页面。", author = "张明", role = "某智能制造集团 CTO", avatarUrl = "https://images.unsplash.com/photo-1560250097-0b93528c311a?auto=format&fit=crop&w=300&q=80" },
                    new { quote = "多语言和热插拔模块非常贴合我们的集团业务，省去了大量重复开发。", author = "李娜", role = "某数字科技公司产品总监", avatarUrl = "https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?auto=format&fit=crop&w=300&q=80" }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.partners", "合作伙伴墙", JsonSerializer.Serialize(new
            {
                heading = "生态合作伙伴",
                items = new[]
                {
                    new { name = "华为云生态" },
                    new { name = "阿里云伙伴" },
                    new { name = "腾讯云" },
                    new { name = "OpenTelemetry" },
                    new { name = "PostgreSQL" },
                    new { name = "MinIO" }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.articles", "新闻动态与案例", JsonSerializer.Serialize(new
            {
                heading = "新闻动态与案例",
                subheading = "了解我们的最新产品发布与行业实践",
                items = new[]
                {
                    new { title = $"{brand} CMS 正式支持可视化拖拽建站", category = "产品发布", date = "2026-11-22", summary = "内置 22 个精细组件、高德地图与 AI 生成能力，助力企业快速搭建官网。", content = "内置 22 个精细组件、高德地图与 AI 生成能力，助力企业快速搭建官网，并支持多语言一键切换与实时预览发布。", coverUrl = "https://images.unsplash.com/photo-1517245386807-bb43f82c33c4?auto=format&fit=crop&w=900&q=80" },
                    new { title = "某智能制造集团数字化门户落地案例", category = "客户案例", date = "2026-11-18", summary = "基于模块化热插拔架构，2 周完成集团官网与多语言业务门户上线。", content = "基于模块化热插拔架构，2 周完成集团官网与多语言业务门户上线，统一了品牌形象与客户留资转化流程。", coverUrl = "https://images.unsplash.com/photo-1504384308090-c894fdcc538d?auto=format&fit=crop&w=900&q=80" }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.faq", "常见问题解答", JsonSerializer.Serialize(new
            {
                heading = "常见问题",
                items = new[]
                {
                    new { question = "支持独立域名访问吗？", answer = "支持，通过域名路由模块即可将企业域名直接指向官网。" },
                    new { question = "如何上传和管理图片与视频？", answer = "直接复用平台附件管理模块，支持本地与 S3/MinIO 存储，也支持填写外部 CDN 地址。" }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.divider", "区块分隔线", JsonSerializer.Serialize(new
            {
                height = 28,
                showLine = true,
                label = "欢迎来访与咨询"
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("联系我们", "contact", true, "single-column",
        [
            new("main", "cms.contact", "商务咨询", JsonSerializer.Serialize(new
            {
                heading = $"与 {brand} 取得联系",
                subheading = "留下您的需求，我们的顾问将在 1 个工作日内响应。",
                submitText = "提交咨询",
                amapKey = "adc139d56406f3844c8f1cf1c6b65c41",
                longitude = 113.6401,
                latitude = 34.72468,
                zoom = 15
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("页脚", "footer", false, "single-column",
        [
            new("main", "cms.footer", "官网页脚", JsonSerializer.Serialize(new
            {
                brand,
                tagline = $"{industry} · 模块化数字化底座",
                copyright = $"© {DateTime.UtcNow.Year} {brand}. All rights reserved."
            }, CmsDtoMapper.JsonOptions))
        ])
    ];

    private static List<CmsAiSectionDraft> BuildEnglishSections(string brand, string industry, string? stylePrompt) =>
    [
        new("Home", "home", true, "single-column",
        [
            new("main", "cms.hero", $"{brand} Hero", JsonSerializer.Serialize(new
            {
                badge = industry,
                headline = $"{brand} · Next-Gen {industry} Solutions",
                subheadline = string.IsNullOrWhiteSpace(stylePrompt)
                    ? "Empowering sustainable business growth with reliability and modern design."
                    : stylePrompt.Trim(),
                primaryButtonText = "Get Started",
                primaryButtonHref = "#contact",
                secondaryButtonText = "Explore Features",
                secondaryButtonHref = "#features"
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.carousel", "Hero Showcase Carousel", JsonSerializer.Serialize(new
            {
                height = 420,
                autoPlay = true,
                intervalMs = 5000,
                slides = new[]
                {
                    new { imageUrl = "https://images.unsplash.com/photo-1497366216548-37526070297c?auto=format&fit=crop&w=1400&q=80", title = $"{brand} Digital Workspace", subtitle = "Modular enterprise portal and operations platform", buttonText = "Explore", buttonHref = "#features" },
                    new { imageUrl = "https://images.unsplash.com/photo-1551434678-e076c223a692?auto=format&fit=crop&w=1400&q=80", title = "Rapid Delivery & Visual Editing", subtitle = "Drag-and-drop builder with instant multi-language publishing", buttonText = "Contact Us", buttonHref = "#contact" }
                }
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("Features", "features", true, "single-column",
        [
            new("main", "cms.heading", "Features Heading", JsonSerializer.Serialize(new
            {
                title = $"Why Choose {brand}",
                subtitle = $"Deep expertise in {industry} with end-to-end reliability",
                level = "h2",
                align = "center"
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.text", "Features Intro", JsonSerializer.Serialize(new
            {
                text = "Compose high-converting enterprise portals and digital touchpoints with hot-pluggable modules and fine-grained widgets.",
                align = "center",
                fontSize = "1rem"
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.button", "Features CTA", JsonSerializer.Serialize(new
            {
                text = "Book a Demo",
                href = "#contact",
                variant = "fill",
                align = "center",
                openNewTab = false
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.features", "Core Features", JsonSerializer.Serialize(new
            {
                heading = "Core Capabilities",
                subheading = "Built for performance, security, and extensibility",
                items = new[]
                {
                    new { icon = "Rocket", title = "Rapid Delivery", description = "Modular architecture adapts quickly to evolving business needs." },
                    new { icon = "Shield", title = "Enterprise Security", description = "End-to-end permission isolation and audit logging." },
                    new { icon = "Check", title = "Responsive UX", description = "Crafted for desktop and mobile with fluid interactions." }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.tabs", "Industry Solutions", JsonSerializer.Serialize(new
            {
                heading = "Industry Solutions",
                items = new[]
                {
                    new { title = "Smart Manufacturing", content = "Connect production scheduling, IoT telemetry, and quality traceability.", imageUrl = "https://images.unsplash.com/photo-1581091226825-a6a2a5aee158?auto=format&fit=crop&w=900&q=80" },
                    new { title = "Public Sector", content = "High-assurance multi-tenant governance with fine-grained audit trails.", imageUrl = "https://images.unsplash.com/photo-1486406146926-c627a92ad1ab?auto=format&fit=crop&w=900&q=80" },
                    new { title = "Modern Retail", content = "Omnichannel loyalty and order orchestration at scale.", imageUrl = "https://images.unsplash.com/photo-1556742049-0cfed4f6a45d?auto=format&fit=crop&w=900&q=80" }
                }
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("About", "about", true, "two-column",
        [
            new("left", "cms.richtext", "Our Story", JsonSerializer.Serialize(new
            {
                heading = $"About {brand}",
                body = $"{brand} is dedicated to innovation and customer success across {industry}, trusted by leading organizations.",
                imageUrl = "https://images.unsplash.com/photo-1522071820081-009f0129c71c?auto=format&fit=crop&w=1000&q=80"
            }, CmsDtoMapper.JsonOptions)),
            new("left", "cms.html", "Custom Highlight", JsonSerializer.Serialize(new
            {
                html = "<div style=\"padding:1rem 1.25rem;border-left:4px solid var(--cms-pri,#0f6cbd);background:rgba(15,108,189,0.06);border-radius:8px;\"><strong>24/7 Dedicated Support</strong><div style=\"opacity:.8;font-size:.9rem;margin-top:4px;\">Private deployment, source access, and custom modules available.</div></div>"
            }, CmsDtoMapper.JsonOptions)),
            new("right", "cms.stats", "Key Metrics", JsonSerializer.Serialize(new
            {
                items = new[]
                {
                    new { value = "99.9%", label = "Uptime SLA" },
                    new { value = "500+", label = "Customers" },
                    new { value = "24/7", label = "Support" }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("right", "cms.video", "Product Walkthrough", JsonSerializer.Serialize(new
            {
                videoUrl = "https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4",
                posterUrl = "https://images.unsplash.com/photo-1519389950473-47ba0277781c?auto=format&fit=crop&w=1000&q=80",
                caption = "Platform & Visual Builder Demo",
                controls = true,
                autoPlay = false,
                loop = false,
                maxHeight = 260
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("Showcase", "showcase", true, "single-column",
        [
            new("main", "cms.image", "Office Spotlight", JsonSerializer.Serialize(new
            {
                imageUrl = "https://images.unsplash.com/photo-1497215728101-856f4ea42174?auto=format&fit=crop&w=1200&q=80",
                alt = $"{brand} R&D Center",
                caption = $"{brand} Engineering & Customer Success Center",
                maxHeight = 360,
                rounded = true,
                shadow = true
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.gallery", "Project Gallery", JsonSerializer.Serialize(new
            {
                heading = "Featured Work",
                items = new[]
                {
                    new { url = "https://images.unsplash.com/photo-1460925895917-afdab827c52f?auto=format&fit=crop&w=800&q=80", caption = "Analytics Dashboard" },
                    new { url = "https://images.unsplash.com/photo-1531482615713-2afd69097998?auto=format&fit=crop&w=800&q=80", caption = "Architecture Review" },
                    new { url = "https://images.unsplash.com/photo-1504384308090-c894fdcc538d?auto=format&fit=crop&w=800&q=80", caption = "Agile Delivery" }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.pricing", "Pricing Plans", JsonSerializer.Serialize(new
            {
                heading = "Flexible Pricing",
                subheading = "Scale effortlessly as your organization grows",
                plans = new[]
                {
                    new { name = "Starter", price = "$299", period = "/ year", badge = "", highlighted = false, features = new[] { "Single-site deployment", "Core widget library", "Standard support" }, buttonText = "Learn More", buttonHref = "#contact" },
                    new { name = "Enterprise", price = "$899", period = "/ year", badge = "Most Popular", highlighted = true, features = new[] { "Multi-site & custom domains", "AI builder & AMap", "24/7 dedicated advisor" }, buttonText = "Get Started", buttonHref = "#contact" },
                    new { name = "Custom", price = "Custom", period = "", badge = "", highlighted = false, features = new[] { "Private cluster & source code", "Bespoke modules", "99.99% SLA" }, buttonText = "Contact Sales", buttonHref = "#contact" }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.testimonials", "Customer Stories", JsonSerializer.Serialize(new
            {
                heading = "Trusted by Leaders",
                items = new[]
                {
                    new { quote = "Launch speed doubled — our marketing team updates pages independently.", author = "David Zhang", role = "CTO, SmartTech Group", avatarUrl = "https://images.unsplash.com/photo-1560250097-0b93528c311a?auto=format&fit=crop&w=300&q=80" },
                    new { quote = "Hot-pluggable modules and localization fit our global portals out of the box.", author = "Sarah Li", role = "VP of Product", avatarUrl = "https://images.unsplash.com/photo-1573496359142-b8d87734a5a2?auto=format&fit=crop&w=300&q=80" }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.partners", "Ecosystem Partners", JsonSerializer.Serialize(new
            {
                heading = "Technology Partners",
                items = new[]
                {
                    new { name = "Azure" },
                    new { name = "PostgreSQL" },
                    new { name = "OpenTelemetry" },
                    new { name = "MinIO" },
                    new { name = "Redis" },
                    new { name = "Docker" }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.articles", "News & Case Studies", JsonSerializer.Serialize(new
            {
                heading = "News & Case Studies",
                subheading = "Latest product updates and customer stories",
                items = new[]
                {
                    new { title = $"{brand} CMS Launches Visual Drag-and-Drop Builder", category = "Release", date = "2026-11-22", summary = "22 modular widgets, AMap integration, and AI site generation out of the box.", content = "22 modular widgets, AMap integration, and AI site generation out of the box with instant multi-language publishing.", coverUrl = "https://images.unsplash.com/photo-1517245386807-bb43f82c33c4?auto=format&fit=crop&w=900&q=80" },
                    new { title = "Global Manufacturer Launches Multi-Language Portal in 2 Weeks", category = "Case Study", date = "2026-11-18", summary = "Unified brand presence and automated lead routing across regions.", content = "Unified brand presence and automated lead routing across regions powered by modular architecture.", coverUrl = "https://images.unsplash.com/photo-1504384308090-c894fdcc538d?auto=format&fit=crop&w=900&q=80" }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.faq", "FAQ", JsonSerializer.Serialize(new
            {
                heading = "Frequently Asked Questions",
                items = new[]
                {
                    new { question = "Are custom domains supported?", answer = "Yes, bind any domain to your site via the Domain Routing module." },
                    new { question = "How are images and videos managed?", answer = "Use the built-in Attachments library (Local or S3/MinIO) or external CDN URLs." }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.divider", "Section Divider", JsonSerializer.Serialize(new
            {
                height = 28,
                showLine = true,
                label = "Visit & Connect"
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("Contact", "contact", true, "single-column",
        [
            new("main", "cms.contact", "Contact Us", JsonSerializer.Serialize(new
            {
                heading = $"Get in Touch with {brand}",
                subheading = "Leave your message and our team will respond within 1 business day.",
                submitText = "Submit Inquiry",
                amapKey = "adc139d56406f3844c8f1cf1c6b65c41",
                longitude = 113.6401,
                latitude = 34.72468,
                zoom = 15
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("Footer", "footer", false, "single-column",
        [
            new("main", "cms.footer", "Site Footer", JsonSerializer.Serialize(new
            {
                brand,
                tagline = $"{industry} · Modular Digital Platform",
                copyright = $"© {DateTime.UtcNow.Year} {brand}. All rights reserved."
            }, CmsDtoMapper.JsonOptions))
        ])
    ];

    public Task<CmsAiSectionDraft> GenerateSectionAsync(CmsAiGenerateSectionRequest request, CancellationToken ct = default)
    {
        var slug = string.IsNullOrWhiteSpace(request.TargetSlug) ? "ai-section" : request.TargetSlug.Trim().ToLowerInvariant();
        var section = new CmsAiSectionDraft(
            Title: "AI 生成区块",
            AnchorSlug: slug,
            NavVisible: true,
            LayoutPreset: "single-column",
            Widgets:
            [
                new("main", "cms.richtext", "AI 内容区块", JsonSerializer.Serialize(new
                {
                    heading = "智能生成内容",
                    body = string.IsNullOrWhiteSpace(request.Prompt)
                        ? "请在此补充您的业务介绍与核心价值主张。"
                        : $"根据您的描述生成：{request.Prompt.Trim()}"
                }, CmsDtoMapper.JsonOptions))
            ]);
        return Task.FromResult(section);
    }

    public Task<CmsAiWidgetDraft> RefineWidgetAsync(CmsAiRefineWidgetRequest request, CancellationToken ct = default) =>
        Task.FromResult(new CmsAiWidgetDraft(
            ZoneId: "main",
            WidgetType: request.WidgetType,
            Title: $"已润色 ({request.Instruction})",
            PropsJson: string.IsNullOrWhiteSpace(request.CurrentPropsJson) ? "{}" : request.CurrentPropsJson));
}
