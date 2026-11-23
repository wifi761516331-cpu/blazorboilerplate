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

        sections = ApplyAnimations(sections);

        return Task.FromResult(new CmsAiSiteDraft(
            Title: isEn ? $"{brand} Official Website" : $"{brand} 官方网站",
            Description: $"{brand} - {industry}",
            Theme: theme,
            Sections: sections,
            ProviderNote: "当前由默认 Schema 占位生成；接入 LLM Provider 后可替换 ICmsAiDesignerService 实现。"));
    }

    private static readonly string[] DefaultAnimSequence =
        ["fade-up", "zoom-in", "slide-left", "scale-up", "flip", "bounce", "blur-in", "slide-down", "slide-up-big", "slide-right", "fade-in"];

    private static List<CmsAiSectionDraft> ApplyAnimations(List<CmsAiSectionDraft> sections)
    {
        for (var s = 0; s < sections.Count; s++)
        {
            var sec = sections[s];
            var baseAnim = DefaultAnimSequence[s % DefaultAnimSequence.Length];
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
                        : (wIdx == 0 ? "slide-left" : DefaultAnimSequence[(s + wIdx) % DefaultAnimSequence.Length]),
                    "three-column" => w.ZoneId switch
                    {
                        "col-1" => "slide-right",
                        "col-2" => "zoom-in",
                        "col-3" => "slide-left",
                        _ => baseAnim
                    },
                    _ => wIdx == 0 ? baseAnim : DefaultAnimSequence[(s + wIdx) % DefaultAnimSequence.Length]
                };

                updatedWidgets.Add(w with { StyleJson = JsonSerializer.Serialize(new { animation = anim }, CmsDtoMapper.JsonOptions) });
            }

            sections[s] = sec with { Widgets = updatedWidgets };
        }
        return sections;
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
                    new { imageUrl = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=1200", title = $"{brand} 数字化工作空间", subtitle = "现代化、模块化的企业门户与业务协同平台", buttonText = "探索方案", buttonHref = "#features" },
                    new { imageUrl = "https://images.pexels.com/photos/3183150/pexels-photo-3183150.jpeg?auto=compress&cs=tinysrgb&w=1200", title = "高效研发与敏捷交付", subtitle = "所见即所得可视化设计，助力品牌快速上线", buttonText = "联系我们", buttonHref = "#contact" }
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
                    new { icon = "Check", title = "智能体验", description = "兼顾设计美感与交互流畅度，全面适配桌面与移动端。" },
                    new { icon = "Globe", title = "多语言与独立域名", description = "原生支持中英文无刷新切换与企业独立域名映射。" },
                    new { icon = "Puzzle", title = "热插拔模块扩展", description = "支持按需加载与热卸载业务模块，零停机平滑升级。" },
                    new { icon = "Chart", title = "全链路可观测", description = "内置 OpenTelemetry 追踪、结构化日志与性能大盘。" }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.tabs", "行业解决方案", JsonSerializer.Serialize(new
            {
                heading = "行业解决方案",
                items = new[]
                {
                    new { title = "智能制造", content = "打通生产排程、设备联网与质量追溯全流程，提升产线协同效率。", imageUrl = "https://images.pexels.com/photos/1181675/pexels-photo-1181675.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "数字政务", content = "高安全合规架构，支持多租户隔离与细粒度权限审计管控。", imageUrl = "https://images.pexels.com/photos/3184328/pexels-photo-3184328.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "现代零售", content = "全渠道会员与订单协同，轻松支撑大促高并发业务场景。", imageUrl = "https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900" }
                }
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("架构亮点", "architecture", true, "three-column",
        [
            new("col-1", "cms.richtext", "热插拔模块化", JsonSerializer.Serialize(new
            {
                heading = "🧩 真正的热插拔模块",
                body = "基于 AssemblyLoadContext 隔离加载，业务模块可独立打包、灰度升级与运行时卸载，互不污染核心宿主。",
                imageUrl = "https://images.pexels.com/photos/1181675/pexels-photo-1181675.jpeg?auto=compress&cs=tinysrgb&w=900"
            }, CmsDtoMapper.JsonOptions)),
            new("col-2", "cms.richtext", "多数据库无缝迁移", JsonSerializer.Serialize(new
            {
                heading = "🗄️ 四大主流数据库通吃",
                body = "一套领域模型与无外键规范，原生支持 SQLite、PostgreSQL、SQL Server 与 MySQL 自动建表迁移。",
                imageUrl = "https://images.pexels.com/photos/577585/pexels-photo-577585.jpeg?auto=compress&cs=tinysrgb&w=900"
            }, CmsDtoMapper.JsonOptions)),
            new("col-3", "cms.richtext", "全链路可观测", JsonSerializer.Serialize(new
            {
                heading = "📈 OpenTelemetry 可观测",
                body = "开箱接入分布式链路追踪、结构化审计日志与健康检查探针，线上故障分钟级定位。",
                imageUrl = "https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900"
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("关于我们", "about", true, "two-column",
        [
            new("left", "cms.richtext", "品牌故事", JsonSerializer.Serialize(new
            {
                heading = $"关于 {brand}",
                body = $"{brand}专注于{industry}领域的技术创新与客户成功，服务众多行业领先企业。",
                imageUrl = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=1200"
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
                posterUrl = "https://images.pexels.com/photos/3183150/pexels-photo-3183150.jpeg?auto=compress&cs=tinysrgb&w=1200",
                caption = "平台能力与可视化搭建演示",
                controls = true,
                autoPlay = false,
                loop = false,
                maxHeight = 260
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("项目画廊", "showcase", true, "single-column",
        [
            new("main", "cms.image", "办公环境展示", JsonSerializer.Serialize(new
            {
                imageUrl = "https://images.pexels.com/photos/3184328/pexels-photo-3184328.jpeg?auto=compress&cs=tinysrgb&w=1200",
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
                    new { url = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "企业数字化工作空间" },
                    new { url = "https://images.pexels.com/photos/3183150/pexels-photo-3183150.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "敏捷研发与技术交付" },
                    new { url = "https://images.pexels.com/photos/1181675/pexels-photo-1181675.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "安全可靠的数字底座" },
                    new { url = "https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "数据分析驾驶舱" },
                    new { url = "https://images.pexels.com/photos/1181359/pexels-photo-1181359.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "架构师团队协作" },
                    new { url = "https://images.pexels.com/photos/1181671/pexels-photo-1181671.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "数字化协同平台" }
                }
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("方案定价", "pricing", true, "single-column",
        [
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
            new("main", "cms.html", "企业交付保障提示", JsonSerializer.Serialize(new
            {
                html = "<div style=\"padding:1.1rem 1.4rem;border-radius:10px;background:rgba(15,108,189,0.06);border:1px solid rgba(15,108,189,0.18);display:flex;flex-wrap:wrap;justify-content:space-between;align-items:center;gap:1rem;\"><div><strong>🏢 需要私有化集群部署或源码授权？</strong><div style=\"font-size:0.9rem;opacity:0.8;margin-top:4px;\">支持信创环境适配、等保三级合规咨询与专属架构师驻场支持。</div></div><a href=\"#contact\" style=\"padding:0.55rem 1.2rem;border-radius:8px;background:var(--cms-pri,#0f6cbd);color:#fff;text-decoration:none;font-weight:600;\">联系架构师</a></div>"
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("客户动态", "cases", true, "two-column",
        [
            new("left", "cms.articles", "新闻动态与案例", JsonSerializer.Serialize(new
            {
                heading = "新闻动态与案例",
                subheading = "了解我们的最新产品发布与行业实践",
                items = new[]
                {
                    new { title = $"{brand} CMS 正式支持可视化拖拽建站", category = "产品发布", date = "2026-11-22", summary = "内置 22 个精细组件、高德地图与 AI 生成能力，助力企业快速搭建官网。", content = "内置 22 个精细组件、高德地图与 AI 生成能力，助力企业快速搭建官网，并支持多语言一键切换与实时预览发布。", coverUrl = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "某智能制造集团数字化门户落地案例", category = "客户案例", date = "2026-11-18", summary = "基于模块化热插拔架构，2 周完成集团官网与多语言业务门户上线。", content = "基于模块化热插拔架构，2 周完成集团官网与多语言业务门户上线，统一了品牌形象与客户留资转化流程。", coverUrl = "https://images.pexels.com/photos/1181671/pexels-photo-1181671.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "多数据库迁移与热卸载缓存架构实践", category = "技术专栏", date = "2026-11-12", summary = "深度解析 SQLite / PostgreSQL / SQL Server / MySQL 统一适配与模块隔离设计。", content = "通过无外键逻辑约定与 JSON 序列化缓存边界，实现模块在运行时的安全加载、升级与卸载。", coverUrl = "https://images.pexels.com/photos/577585/pexels-photo-577585.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "独立域名路由与多品牌官网矩阵指南", category = "最佳实践", date = "2026-11-05", summary = "一套平台同时托管品牌官网、产品文档站与活动着陆页。", content = "结合域名路由解析器与不可变发布快照，不同域名访客可秒级直达对应的品牌门户。", coverUrl = "https://images.pexels.com/photos/3184328/pexels-photo-3184328.jpeg?auto=compress&cs=tinysrgb&w=900" }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("right", "cms.testimonials", "客户真实评价", JsonSerializer.Serialize(new
            {
                heading = "客户信赖与评价",
                items = new[]
                {
                    new { quote = "上线效率非常高，可视化设计器让运营团队也能独立维护官网页面。", author = "张明", role = "某智能制造集团 CTO", avatarUrl = "https://images.pexels.com/photos/220453/pexels-photo-220453.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "多语言和热插拔模块非常贴合我们的集团业务，省去了大量重复开发。", author = "李娜", role = "某数字科技公司产品总监", avatarUrl = "https://images.pexels.com/photos/774909/pexels-photo-774909.jpeg?auto=compress&cs=tinysrgb&w=300" }
                }
            }))
        ]),
        new("生态伙伴", "partners", true, "single-column",
        [
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
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("演进路线", "roadmap", true, "single-column",
        [
            new("main", "cms.heading", "产品演进路线图", JsonSerializer.Serialize(new
            {
                title = "持续进化的平台路线图",
                subtitle = "每季度稳定迭代，长期向后兼容",
                level = "h2",
                align = "center"
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.html", "路线图时间轴", JsonSerializer.Serialize(new
            {
                html = "<div style=\"display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:1rem;\"><div style=\"padding:1.1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(15,108,189,0.2);\"><strong style=\"color:var(--cms-pri,#0f6cbd);\">Q1 · 模块内核</strong><div style=\"font-size:0.88rem;opacity:0.82;margin-top:4px;\">ALC 热插拔运行时、统一权限契约与多数据库迁移。</div></div><div style=\"padding:1.1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(15,108,189,0.2);\"><strong style=\"color:var(--cms-pri,#0f6cbd);\">Q2 · 可视化 CMS</strong><div style=\"font-size:0.88rem;opacity:0.82;margin-top:4px;\">12 栅格布局器、22+ 业务微件与多语言无刷新门户。</div></div><div style=\"padding:1.1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(15,108,189,0.2);\"><strong style=\"color:var(--cms-pri,#0f6cbd);\">Q3 · AI 建站</strong><div style=\"font-size:0.88rem;opacity:0.82;margin-top:4px;\">接入大模型智能生成整站结构、文案润色与多语翻译。</div></div><div style=\"padding:1.1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(15,108,189,0.2);\"><strong style=\"color:var(--cms-pri,#0f6cbd);\">Q4 · 生态市场</strong><div style=\"font-size:0.88rem;opacity:0.82;margin-top:4px;\">开放插件分发市场、企业级 SSO 联邦认证与集群运维大盘。</div></div></div>"
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("开发者生态", "developers", true, "two-column",
        [
            new("left", "cms.richtext", "开发者文档与 SDK", JsonSerializer.Serialize(new
            {
                heading = "为开发者打造的优雅扩展体验",
                body = "提供完整的模块 SDK、Minimal API 脚手架与强类型前端组件基类，10 分钟即可开发并热加载一个全新业务模块。",
                imageUrl = "https://images.pexels.com/photos/577585/pexels-photo-577585.jpeg?auto=compress&cs=tinysrgb&w=900"
            }, CmsDtoMapper.JsonOptions)),
            new("right", "cms.html", "技术栈与开发规范", JsonSerializer.Serialize(new
            {
                html = "<div style=\"padding:1.25rem;border-radius:10px;border:1px solid rgba(15,108,189,0.2);background:rgba(15,108,189,0.05);\"><strong>⚡ 现代全栈技术基座</strong><ul style=\"margin:0.65rem 0 0;padding-left:1.2rem;line-height:1.8;\"><li>.NET 10 + Blazor WebAssembly / Auto 混合渲染</li><li>DDD 整洁分层：Domain / Application / Infrastructure</li><li>EF Core 多数据库抽象（SQLite / PG / SQL Server / MySQL）</li><li>OpenTelemetry + Serilog 全链路结构化可观测</li></ul></div>"
            }, CmsDtoMapper.JsonOptions)),
            new("right", "cms.button", "申请开发者沙箱", JsonSerializer.Serialize(new
            {
                text = "申请体验开发者沙箱",
                href = "#contact",
                variant = "outline",
                align = "left",
                openNewTab = false
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("常见问答", "faq", true, "sidebar",
        [
            new("sidebar", "cms.richtext", "技术支持说明", JsonSerializer.Serialize(new
            {
                heading = "还有架构或选型疑问？",
                body = "我们的技术顾问团队可为您提供 1 对 1 架构演示、POC 环境搭建与源码评估指导。"
            }, CmsDtoMapper.JsonOptions)),
            new("sidebar", "cms.button", "预约技术演示", JsonSerializer.Serialize(new
            {
                text = "预约 1v1 技术演示",
                href = "#contact",
                variant = "fill",
                align = "left",
                openNewTab = false
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.faq", "常见问题解答", JsonSerializer.Serialize(new
            {
                heading = "常见问题",
                items = new[]
                {
                    new { question = "支持独立域名访问吗？", answer = "支持，通过域名路由模块即可将企业域名直接指向官网。" },
                    new { question = "如何上传和管理图片与视频？", answer = "直接复用平台附件管理模块，支持本地与 S3/MinIO 存储，也支持填写外部 CDN 地址。" },
                    new { question = "设计器修改后会影响线上访客吗？", answer = "不会。所有编辑都在草稿态进行，只有点击“发布”后才会生成新的不可变线上快照。" },
                    new { question = "支持多语言官网无刷新切换吗？", answer = "支持。站点可配置多个支持语言，并可一键克隆源语言结构进行翻译，访客端支持单页无刷新切换。" },
                    new { question = "如何扩展自定义微件或接入大模型？", answer = "后端可替换 ICmsAiDesignerService 接入任意 LLM，前端在 CmsWidgetCatalog 注册新微件即可。" }
                }
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("联系我们", "contact", true, "two-column",
        [
            new("left", "cms.map", "公司位置地图", JsonSerializer.Serialize(new
            {
                heading = "来访地址与交通指南",
                subheading = "欢迎莅临技术中心交流指导",
                title = $"{brand} 研发中心",
                address = "上海市浦东新区张江高科技园区",
                longitude = 121.602,
                latitude = 31.203,
                zoom = 13,
                height = 340,
                amapKey = "adc139d56406f3844c8f1cf1c6b65c41"
            }, CmsDtoMapper.JsonOptions)),
            new("right", "cms.contact", "商务咨询", JsonSerializer.Serialize(new
            {
                heading = $"与 {brand} 取得联系",
                subheading = "留下您的需求，我们的顾问将在 1 个工作日内响应。",
                submitText = "提交咨询",
                email = "contact@example.com",
                phone = "400-888-6600",
                address = "上海市浦东新区张江高科技园区"
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
                    new { imageUrl = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=1200", title = $"{brand} Digital Workspace", subtitle = "Modular enterprise portal and operations platform", buttonText = "Explore", buttonHref = "#features" },
                    new { imageUrl = "https://images.pexels.com/photos/3183150/pexels-photo-3183150.jpeg?auto=compress&cs=tinysrgb&w=1200", title = "Rapid Delivery & Visual Editing", subtitle = "Drag-and-drop builder with instant multi-language publishing", buttonText = "Contact Us", buttonHref = "#contact" }
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
                    new { icon = "Check", title = "Responsive UX", description = "Crafted for desktop and mobile with fluid interactions." },
                    new { icon = "Globe", title = "Multi-Language & Domains", description = "Client-side culture switching and custom domain routing built in." },
                    new { icon = "Puzzle", title = "Hot-Pluggable Modules", description = "Load, upgrade, and unload business modules with zero downtime." },
                    new { icon = "Chart", title = "Full Observability", description = "OpenTelemetry tracing, structured audit logs, and health probes." }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.tabs", "Industry Solutions", JsonSerializer.Serialize(new
            {
                heading = "Industry Solutions",
                items = new[]
                {
                    new { title = "Smart Manufacturing", content = "Connect production scheduling, IoT telemetry, and quality traceability.", imageUrl = "https://images.pexels.com/photos/1181675/pexels-photo-1181675.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "Public Sector", content = "High-assurance multi-tenant governance with fine-grained audit trails.", imageUrl = "https://images.pexels.com/photos/3184328/pexels-photo-3184328.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "Modern Retail", content = "Omnichannel loyalty and order orchestration at scale.", imageUrl = "https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900" }
                }
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("Architecture", "architecture", true, "three-column",
        [
            new("col-1", "cms.richtext", "Hot-Pluggable Modules", JsonSerializer.Serialize(new
            {
                heading = "🧩 Isolated Hot-Plug Modules",
                body = "Powered by AssemblyLoadContext isolation — load, upgrade, and unload business modules without restarting the host.",
                imageUrl = "https://images.pexels.com/photos/1181675/pexels-photo-1181675.jpeg?auto=compress&cs=tinysrgb&w=900"
            }, CmsDtoMapper.JsonOptions)),
            new("col-2", "cms.richtext", "Multi-Database Ready", JsonSerializer.Serialize(new
            {
                heading = "🗄️ 4 Database Providers",
                body = "One clean domain model with automatic migrations across SQLite, PostgreSQL, SQL Server, and MySQL.",
                imageUrl = "https://images.pexels.com/photos/577585/pexels-photo-577585.jpeg?auto=compress&cs=tinysrgb&w=900"
            }, CmsDtoMapper.JsonOptions)),
            new("col-3", "cms.richtext", "Full Observability", JsonSerializer.Serialize(new
            {
                heading = "📈 OpenTelemetry Built In",
                body = "Distributed tracing, structured audit logs, and health probes out of the box for rapid diagnostics.",
                imageUrl = "https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900"
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("About", "about", true, "two-column",
        [
            new("left", "cms.richtext", "Our Story", JsonSerializer.Serialize(new
            {
                heading = $"About {brand}",
                body = $"{brand} is dedicated to innovation and customer success across {industry}, trusted by leading organizations.",
                imageUrl = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=1200"
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
                posterUrl = "https://images.pexels.com/photos/3183150/pexels-photo-3183150.jpeg?auto=compress&cs=tinysrgb&w=1200",
                caption = "Platform & Visual Builder Demo",
                controls = true,
                autoPlay = false,
                loop = false,
                maxHeight = 260
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("Gallery", "showcase", true, "single-column",
        [
            new("main", "cms.image", "Office Spotlight", JsonSerializer.Serialize(new
            {
                imageUrl = "https://images.pexels.com/photos/3184328/pexels-photo-3184328.jpeg?auto=compress&cs=tinysrgb&w=1200",
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
                    new { url = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Enterprise Digital Workspace" },
                    new { url = "https://images.pexels.com/photos/3183150/pexels-photo-3183150.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Agile R&D Delivery" },
                    new { url = "https://images.pexels.com/photos/1181675/pexels-photo-1181675.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Secure Digital Foundation" },
                    new { url = "https://images.pexels.com/photos/590022/pexels-photo-590022.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Analytics Dashboard" },
                    new { url = "https://images.pexels.com/photos/1181359/pexels-photo-1181359.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Architecture Review" },
                    new { url = "https://images.pexels.com/photos/1181671/pexels-photo-1181671.jpeg?auto=compress&cs=tinysrgb&w=900", caption = "Digital Collaboration Platform" }
                }
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("Pricing", "pricing", true, "single-column",
        [
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
            new("main", "cms.html", "Enterprise Deployment Callout", JsonSerializer.Serialize(new
            {
                html = "<div style=\"padding:1.1rem 1.4rem;border-radius:10px;background:rgba(15,108,189,0.06);border:1px solid rgba(15,108,189,0.18);display:flex;flex-wrap:wrap;justify-content:space-between;align-items:center;gap:1rem;\"><div><strong>🏢 Need Private Cluster Deployment or Source Access?</strong><div style=\"font-size:0.9rem;opacity:0.8;margin-top:4px;\">Dedicated architecture advisory, security compliance review, and custom module engineering.</div></div><a href=\"#contact\" style=\"padding:0.55rem 1.2rem;border-radius:8px;background:var(--cms-pri,#0f6cbd);color:#fff;text-decoration:none;font-weight:600;\">Talk to an Architect</a></div>"
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("Stories", "cases", true, "two-column",
        [
            new("left", "cms.articles", "News & Case Studies", JsonSerializer.Serialize(new
            {
                heading = "News & Case Studies",
                subheading = "Latest product updates and customer stories",
                items = new[]
                {
                    new { title = $"{brand} CMS Launches Visual Drag-and-Drop Builder", category = "Release", date = "2026-11-22", summary = "22 modular widgets, AMap integration, and AI site generation out of the box.", content = "22 modular widgets, AMap integration, and AI site generation out of the box with instant multi-language publishing.", coverUrl = "https://images.pexels.com/photos/3184291/pexels-photo-3184291.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "Global Manufacturer Launches Multi-Language Portal in 2 Weeks", category = "Case Study", date = "2026-11-18", summary = "Unified brand presence and automated lead routing across regions.", content = "Unified brand presence and automated lead routing across regions powered by modular architecture.", coverUrl = "https://images.pexels.com/photos/1181671/pexels-photo-1181671.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "Multi-Database Migrations & Hot-Unload Friendly Caching", category = "Engineering", date = "2026-11-12", summary = "How SQLite, PostgreSQL, SQL Server, and MySQL work seamlessly with isolated modules.", content = "Foreign-key-free conventions and JSON-serialized cache boundaries keep module assembly unloading clean.", coverUrl = "https://images.pexels.com/photos/577585/pexels-photo-577585.jpeg?auto=compress&cs=tinysrgb&w=900" },
                    new { title = "Custom Domain Routing for Multi-Brand Portals", category = "Guide", date = "2026-11-05", summary = "Host corporate sites, product microsites, and landing pages on one platform.", content = "Domain route resolution maps each host directly to its immutable published snapshot.", coverUrl = "https://images.pexels.com/photos/3184328/pexels-photo-3184328.jpeg?auto=compress&cs=tinysrgb&w=900" }
                }
            }, CmsDtoMapper.JsonOptions)),
            new("right", "cms.testimonials", "Customer Stories", JsonSerializer.Serialize(new
            {
                heading = "Trusted by Leaders",
                items = new[]
                {
                    new { quote = "Launch speed doubled — our marketing team updates pages independently.", author = "David Zhang", role = "CTO, SmartTech Group", avatarUrl = "https://images.pexels.com/photos/220453/pexels-photo-220453.jpeg?auto=compress&cs=tinysrgb&w=300" },
                    new { quote = "Hot-pluggable modules and localization fit our global portals out of the box.", author = "Sarah Li", role = "VP of Product", avatarUrl = "https://images.pexels.com/photos/774909/pexels-photo-774909.jpeg?auto=compress&cs=tinysrgb&w=300" }
                }
            }))
        ]),
        new("Partners", "partners", true, "single-column",
        [
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
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("Roadmap", "roadmap", true, "single-column",
        [
            new("main", "cms.heading", "Platform Roadmap", JsonSerializer.Serialize(new
            {
                title = "Continuous Platform Evolution",
                subtitle = "Predictable quarterly releases with long-term compatibility",
                level = "h2",
                align = "center"
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.html", "Roadmap Timeline", JsonSerializer.Serialize(new
            {
                html = "<div style=\"display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:1rem;\"><div style=\"padding:1.1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(15,108,189,0.2);\"><strong style=\"color:var(--cms-pri,#0f6cbd);\">Q1 · Module Core</strong><div style=\"font-size:0.88rem;opacity:0.82;margin-top:4px;\">ALC hot-pluggable runtime, unified permissions, and 4-DB migrations.</div></div><div style=\"padding:1.1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(15,108,189,0.2);\"><strong style=\"color:var(--cms-pri,#0f6cbd);\">Q2 · Visual CMS</strong><div style=\"font-size:0.88rem;opacity:0.82;margin-top:4px;\">12-column grid builder, 22+ widgets, and multi-language portals.</div></div><div style=\"padding:1.1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(15,108,189,0.2);\"><strong style=\"color:var(--cms-pri,#0f6cbd);\">Q3 · AI Builder</strong><div style=\"font-size:0.88rem;opacity:0.82;margin-top:4px;\">LLM-powered site generation, copy refinement, and localization.</div></div><div style=\"padding:1.1rem;border-radius:10px;background:var(--cms-bg,#fff);border:1px solid rgba(15,108,189,0.2);\"><strong style=\"color:var(--cms-pri,#0f6cbd);\">Q4 · Marketplace</strong><div style=\"font-size:0.88rem;opacity:0.82;margin-top:4px;\">Plugin marketplace, federated SSO, and cluster observability.</div></div></div>"
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("Developers", "developers", true, "two-column",
        [
            new("left", "cms.richtext", "Developer Experience", JsonSerializer.Serialize(new
            {
                heading = "Built for Developers Who Ship",
                body = "Complete Module SDK, Minimal API scaffolding, and strongly typed Blazor widget contracts — build and hot-load a new module in 10 minutes.",
                imageUrl = "https://images.pexels.com/photos/577585/pexels-photo-577585.jpeg?auto=compress&cs=tinysrgb&w=900"
            }, CmsDtoMapper.JsonOptions)),
            new("right", "cms.html", "Tech Stack Highlights", JsonSerializer.Serialize(new
            {
                html = "<div style=\"padding:1.25rem;border-radius:10px;border:1px solid rgba(15,108,189,0.2);background:rgba(15,108,189,0.05);\"><strong>⚡ Modern Full-Stack Foundation</strong><ul style=\"margin:0.65rem 0 0;padding-left:1.2rem;line-height:1.8;\"><li>.NET 10 + Blazor WebAssembly / Auto Rendering</li><li>Clean DDD Layers: Domain / Application / Infrastructure</li><li>EF Core Multi-DB (SQLite / PG / SQL Server / MySQL)</li><li>OpenTelemetry + Serilog End-to-End Observability</li></ul></div>"
            }, CmsDtoMapper.JsonOptions)),
            new("right", "cms.button", "Developer Sandbox CTA", JsonSerializer.Serialize(new
            {
                text = "Request Developer Sandbox",
                href = "#contact",
                variant = "outline",
                align = "left",
                openNewTab = false
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("FAQ", "faq", true, "sidebar",
        [
            new("sidebar", "cms.richtext", "Technical Advisory", JsonSerializer.Serialize(new
            {
                heading = "Questions on Architecture?",
                body = "Our solutions architects provide 1-on-1 walkthroughs, POC sandboxes, and integration guidance."
            }, CmsDtoMapper.JsonOptions)),
            new("sidebar", "cms.button", "Schedule Demo", JsonSerializer.Serialize(new
            {
                text = "Book 1v1 Technical Demo",
                href = "#contact",
                variant = "fill",
                align = "left",
                openNewTab = false
            }, CmsDtoMapper.JsonOptions)),
            new("main", "cms.faq", "FAQ", JsonSerializer.Serialize(new
            {
                heading = "Frequently Asked Questions",
                items = new[]
                {
                    new { question = "Are custom domains supported?", answer = "Yes, bind any domain to your site via the Domain Routing module." },
                    new { question = "How are images and videos managed?", answer = "Use the built-in Attachments library (Local or S3/MinIO) or external CDN URLs." },
                    new { question = "Do draft edits affect live visitors?", answer = "No. Edits stay in draft until you click Publish to generate a new immutable snapshot." },
                    new { question = "Can visitors switch languages without reloading?", answer = "Yes, multi-language sites support instant client-side culture switching." },
                    new { question = "How can we add custom widgets or LLM providers?", answer = "Replace ICmsAiDesignerService on the backend and register new widgets in CmsWidgetCatalog." }
                }
            }, CmsDtoMapper.JsonOptions))
        ]),
        new("Contact", "contact", true, "two-column",
        [
            new("left", "cms.map", "Location Map", JsonSerializer.Serialize(new
            {
                heading = "Visit Our Engineering Center",
                subheading = "Interactive Amap location",
                title = $"{brand} R&D Center",
                address = "Zhangjiang Hi-Tech Park, Pudong, Shanghai",
                longitude = 121.602,
                latitude = 31.203,
                zoom = 13,
                height = 340,
                amapKey = "adc139d56406f3844c8f1cf1c6b65c41"
            }, CmsDtoMapper.JsonOptions)),
            new("right", "cms.contact", "Contact Us", JsonSerializer.Serialize(new
            {
                heading = $"Get in Touch with {brand}",
                subheading = "Leave your message and our team will respond within 1 business day.",
                submitText = "Submit Inquiry",
                email = "contact@example.com",
                phone = "+1-800-555-0100",
                address = "Zhangjiang Hi-Tech Park, Pudong, Shanghai"
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
