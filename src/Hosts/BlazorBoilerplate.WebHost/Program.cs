using Bit.BlazorUI;
using BlazorBoilerplate.WebHost.Auth;
using BlazorBoilerplate.WebHost.Components;
using BlazorBoilerplate.WebHost.Localization;
using BlazorBoilerplate.WebHost.Services;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- 日志：与 ApiHost 一致，Console + 按天滚动的文件（logs/webhost-yyyyMMdd.log）----
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext());

// 直接运行 exe（非 dotnet run）且环境不是 Development 时，静态 Web 资源清单不会自动加载，
// _content/Bit.BlazorUI、_framework/blazor.web.js 等资源会 404 并落进 ModuleHost catch-all
// 返回首页 HTML（症状：控制台一排 Unexpected token '<'、页面裸奔）。显式加载修复。
builder.WebHost.UseStaticWebAssets();

// ---- Bit.BlazorUI 10.7.0 设计系统 ----
builder.Services.AddBitBlazorUIServices(true);

// ---- Blazor Web App（Interactive Server + WebAssembly 混合模式）----
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

// ---- 统一调用 ApiHost 的 HttpClient（CORS 已在 ApiHost 放行 http://localhost:5200）----
var apiBaseUrl = builder.Configuration["ApiHost:BaseUrl"] ?? "http://localhost:5100";
builder.Services.AddHttpClient();
builder.Services.Configure<HttpClientFactoryOptions>(options =>
{
    options.HttpClientActions.Add(client =>
    {
        client.BaseAddress = new Uri(apiBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
    });
});

// ---- 浏览器 ↔ WebHost：Cookie 认证票据（内含 JWT）+ 线路级会话 + Scoped ApiClient ----
builder.Services.AddWebHostAuth(apiBaseUrl);

// ---- 全局通知：scoped（每条 Blazor 线路一个实例），BlazorBoilerplateToast 订阅渲染 ----
builder.Services.AddScoped<ToastService>();

// ---- 登录态 / 设备偏好 / 多语言：scoped，随每条 Blazor 线路 ----
builder.Services.AddScoped<AccountState>();
builder.Services.AddScoped<SessionRevocationWatcher>();
builder.Services.AddScoped<ISessionRevocationWatcher>(sp => sp.GetRequiredService<SessionRevocationWatcher>());
builder.Services.AddSingleton<IRenderModeArbitrator, ServerRenderModeArbitrator>();
builder.Services.AddScoped<BlazorBoilerplate.WebHost.Client.Services.IModuleCatalogControl, ServerModuleCatalogControl>();
builder.Services.AddScoped<ICurrentUserPermissionService>(sp => sp.GetRequiredService<AccountState>());
builder.Services.AddScoped<BlazorBoilerplate.WebHost.Client.Services.ClientModuleCatalog>();
builder.Services.AddScoped<SystemSettingsState>();
builder.Services.AddScoped<UserPreferences>();
builder.Services.AddScoped<BlazorBoilerplate.Web.Shared.Localization.FrontendLocalizationService>();
builder.Services.AddScoped<BlazorBoilerplate.Application.Localization.ILocalizationService>(sp =>
    sp.GetRequiredService<BlazorBoilerplate.Web.Shared.Localization.FrontendLocalizationService>());

// ---- 导航刷新事件：模块加载/卸载后通知 MainLayout 重建侧边栏菜单 ----
builder.Services.AddScoped<BlazorBoilerplate.WebHost.Services.NavRefreshService>();
builder.Services.AddScoped<BlazorBoilerplate.Web.Shared.Services.INavRefreshService>(sp =>
    sp.GetRequiredService<BlazorBoilerplate.WebHost.Services.NavRefreshService>());

// ---- 动态模块前端程序集目录（运行时注入：上传/切版本后刷新页面即生效，无需重启）----
// 在 Build() 前加载一次，以支持模块前端 IModuleClientStartup / IClientService 自动注入 DI。
var bootstrapHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
using var tempProvider = builder.Services.BuildServiceProvider();
var moduleCatalog = new BlazorBoilerplate.WebHost.Services.ModuleAssemblyCatalog(
    bootstrapHttp,
    builder.Configuration,
    tempProvider.GetRequiredService<ILogger<BlazorBoilerplate.WebHost.Services.ModuleAssemblyCatalog>>());
moduleCatalog.LoadAll();
moduleCatalog.RegisterClientServices(builder.Services);
builder.Services.AddSingleton(moduleCatalog);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();

// 多语言：切文化 + 预渲染前预热词典，封装在 FrontendLocalizationExtensions.UseFrontendLocalization
app.UseFrontendLocalization();

// 必须显式 UseRouting 且放在 UseStaticFiles 之后：ModuleHost.razor 的 /{**path} catch-all
// 会匹配所有路径，若路由先于静态文件运行，StaticFileMiddleware 因请求已匹配 endpoint 而
// 跳过文件服务，所有 CSS/JS 落进 catch-all 返回首页 HTML（症状：Unexpected token '<'、页面裸奔）。
app.UseRouting();

// Cookie 认证：SSR 首屏与 Blazor Interactive Server 线路都从 HttpContext.User 拿票据 claims。
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// 同源 /auth/*：登录、刷新、切换账号/租户、多账号列表、登出（JS 只拿 accountId，不接触令牌）。
app.MapWebAuthEndpoints();

// 同源 BFF 反向代理：将 WASM 端的 /api/* 转发至 ApiHost（服务端注入 Bearer 与 X-Tenant-Host）。
app.MapBffApiProxy();

// 动态模块前端样式、版本清单、DLL 与渲染策略切换端点。
app.MapModuleClientEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(BlazorBoilerplate.WebHost.Client._Imports).Assembly);
// 注意：模块组件不走静态 AdditionalAssemblies —— SSR 由 ModuleHost.razor（/{**path} catch-all）
// 运行时解析渲染；interactive 导航由 Routes.razor 的 Router 绑定 ModuleAssemblyCatalog.Assemblies。

app.Run();
