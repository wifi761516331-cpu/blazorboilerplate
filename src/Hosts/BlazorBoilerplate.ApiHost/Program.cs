using System.Threading.RateLimiting;
using BlazorBoilerplate.ApiHost.Auth;
using BlazorBoilerplate.ApiHost.HealthChecks;
using BlazorBoilerplate.ApiHost.Modules;
using BlazorBoilerplate.ApiHost.Setup;
using BlazorBoilerplate.Infrastructure;
using BlazorBoilerplate.Infrastructure.Email;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.ModuleRuntime;
using BlazorBoilerplate.ModuleRuntime.Runtime;
using BlazorBoilerplate.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- 日志：Serilog 结构化输出 ----
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext());

builder.Services.AddHttpLogging(o => { });

// ---- 认证：自签 JWT（HS256，零外部包）的 Bearer 方案 ----
// 只作为「WebHost → ApiHost」这段的凭据；浏览器 ↔ WebHost 由 Cookie 承担（Blazor Interactive Server）。
// 不用 FallbackPolicy：平台自身的 setup / health / openapi / modules 端点保持原有访问规则，
// 需要登录的 MVC Controller 各自标 [Authorize]（Identity / PushNotification 标 [AllowAnonymous]）。
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = AppAuthSchemes.Bearer;
        options.DefaultChallengeScheme = AppAuthSchemes.Bearer;
    })
    .AddScheme<AuthenticationSchemeOptions, AppBearerHandler>(AppAuthSchemes.Bearer, null);
builder.Services.AddAuthorization();

// ---- MVC Controller（Bluebird 风格共享契约的服务端实现）----
// 宿主 DI 也注册一份 IdentityDbContext：Controller 是宿主编译期类型，无法解析模块私有 ALC 容器里的注册。
// 与模块容器共用 PlatformConnectionState 的同一连接串，两侧互不影响。
builder.Services.AddControllers(options => options.Filters.Add<ApiExceptionFilter>());
builder.Services.AddDbContext<IdentityDbContext>(o => o.UseIdentityProvider());
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<CurrentUserFactory>();
builder.Services.AddScoped<ModuleMigrationRunner>();
builder.Services.AddScoped<ModuleMenuSyncService>();
// 必要模块 identity 的迁移由宿主兜底：已安装的旧版本包可能还没有迁移声明，
// 但宿主的 DbContext / Controller 已按新模型编译，升级时结构必须先补齐。
builder.Services.AddSingleton(new HostModuleMigration(
    BlazorBoilerplate.Modules.Identity.IdentityModule.ModuleId, new IdentityDatabaseMigrations()));

// ---- 配置：把模块包根目录解析为绝对路径（env BLAZORBOILERPLATE_MODULES_ROOT 优先）----
var modulesRoot = Environment.GetEnvironmentVariable("BLAZORBOILERPLATE_MODULES_ROOT");
builder.Services.PostConfigure<ModuleRuntimeOptions>(options =>
{
    if (!string.IsNullOrWhiteSpace(modulesRoot))
        options.PackagesRoot = Path.GetFullPath(modulesRoot);
    else
        options.PackagesRoot = Path.GetFullPath(Path.Combine(
            builder.Environment.ContentRootPath, options.PackagesRoot));
    // 开发打包：默认指向解决方案内的模块源码目录（仅 Development 环境生效）。
    if (string.IsNullOrWhiteSpace(options.SourceRoot))
        options.SourceRoot = Path.GetFullPath(Path.Combine(
            builder.Environment.ContentRootPath, "..", "..", "..", "src", "Modules"));
});

// ---- 开发打包服务（/api/modules/pack）----
builder.Services.AddSingleton<ModulePackService>();

// ---- 平台能力（第一批为占位实现）+ 模块运行时 ----
builder.Services.Configure<SetupOptions>(builder.Configuration.GetSection(SetupOptions.SectionName));
builder.Services.AddBlazorBoilerplateInfrastructurePlaceholders();
builder.Services.AddBlazorBoilerplateEmail(builder.Configuration);
builder.Services.AddBlazorBoilerplateAttachments(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddBlazorBoilerplateModuleStorage(builder.Configuration);
builder.Services.AddBlazorBoilerplateModuleRuntime(builder.Configuration);
builder.Services.AddSingleton<SetupStateProvider>();
builder.Services.AddSingleton<ISetupInitializer, DefaultSetupInitializer>();

// ---- HTTP 管线通用能力 ----
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<SmtpHealthCheck>("smtp", failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded);
builder.Services.AddOpenApi();
builder.Services.AddCors(options => options.AddPolicy("web", policy =>
    policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromSeconds(10),
                QueueLimit = 0
            }));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseHttpLogging();
app.UseCors("web");
app.UseRateLimiter();

// ---- Setup 门槛：Bootstrap 模式只开放 setup / health / openapi ----
var setup = app.Services.GetRequiredService<SetupStateProvider>();
app.Use(async (context, next) =>
{
    if (setup.CurrentMode == SetupMode.Bootstrap)
    {
        var path = context.Request.Path;
        var allowed = path.StartsWithSegments("/api/setup")
                      || path.StartsWithSegments("/health")
                      || path.StartsWithSegments("/openapi");
        if (!allowed)
        {
            context.Response.StatusCode = StatusCodes.Status423Locked;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Platform is not installed. Complete POST /api/setup/complete first."
            });
            return;
        }
    }
    await next();
});

// ---- 路由：核心端点 + 模块动态 Endpoint 数据源 ----
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseModuleEndpointDataSource();

app.MapHealthChecks("/health");
app.MapOpenApi("/openapi/v1.json");
app.MapSetupEndpoints();
app.MapModuleManagementEndpoints();
app.MapControllers();

// ---- 启动时发现并（nopCommerce 风格）自动按序加载已安装模块 ----
RestorePlatformConnectionState(app.Environment.ContentRootPath);

await using (var scope = app.Services.CreateAsyncScope())
{
    var manager = scope.ServiceProvider.GetRequiredService<ModuleLifecycleManager>();
    var options = scope.ServiceProvider.GetRequiredService<IOptions<ModuleRuntimeOptions>>().Value;

    var discovered = await manager.RefreshCatalogAsync(CancellationToken.None);
    Log.Information("Module catalog refreshed. Discovered={Count}, Mode={Mode}", discovered, setup.CurrentMode);

    if (options.AutoLoadOnStartup && setup.CurrentMode == SetupMode.Normal && discovered > 0)
    {
        // ResolveLoadOrder：依赖优先 + DisplayOrder 升序；依赖失败的模块标记 Failed 并跳过，不阻断其余。
        // 仅在已安装（Normal）模式下自动加载，与 nopCommerce 一致：装好之后插件才被激活。
        var loaded = await manager.LoadAllAsync(CancellationToken.None);
        var running = loaded.Count(d => d.State == ModuleState.Running);
        Log.Information(
            "nopCommerce-style auto-load at startup. Loaded={Running}/{Total}, Mode={Mode}",
            running, loaded.Count, setup.CurrentMode);

        // 模块数据库增量迁移（只增不减）+ 菜单/权限码绑定：加载成功后执行。
        // Bootstrap 未安装时 Hook 内部直接跳过（此时数据库还不存在）。
        foreach (var descriptor in loaded.Where(d => d.State == ModuleState.Running))
            await ModuleDataHooks.OnLoadedAsync(
                scope.ServiceProvider, descriptor.Id, descriptor.PackagePath, CancellationToken.None);
    }
}

app.Run();

/// <summary>
/// 从安装向导持久化的 data/blazorboilerplate.settings.json 恢复平台数据连接状态，
/// 供模块（Identity 等）在 ALC 中注册 DbContext 使用。
/// </summary>
static void RestorePlatformConnectionState(string contentRoot)
{
    var path = Path.Combine(contentRoot, "data", "blazorboilerplate.settings.json");
    if (!File.Exists(path))
        return;

    try
    {
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        var provider = json.TryGetProperty("databaseProvider", out var p) ? p.GetString() : null;
        var infra = json.TryGetProperty("infrastructure", out var i) ? i : default;
        string? pg = infra.ValueKind != System.Text.Json.JsonValueKind.Undefined && infra.TryGetProperty("postgreSQL", out var pgp) ? pgp.GetString() : null;
        string? sqlite = infra.ValueKind != System.Text.Json.JsonValueKind.Undefined && infra.TryGetProperty("sqlite", out var sp) ? sp.GetString() : null;
        PlatformConnectionState.Set(provider, pg, sqlite);
        Serilog.Log.Information("Platform connection state restored. Provider={Provider}", PlatformConnectionState.DatabaseProvider);
    }
    catch (Exception ex)
    {
        Serilog.Log.Warning(ex, "Failed to restore platform connection state; modules may fall back to defaults.");
    }
}

/// <summary>供 WebApplicationFactory 测试使用。</summary>
public partial class Program;
