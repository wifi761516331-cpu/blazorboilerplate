using System.Text.Json;
using BlazorBoilerplate.ApiHost.Modules;
using BlazorBoilerplate.ApiHost.Services;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.ModuleRuntime.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlazorBoilerplate.ApiHost.Setup;

public enum SetupMode
{
    /// <summary>未安装：只开放 /api/setup、/health、静态资源。</summary>
    Bootstrap,

    /// <summary>正常模式。</summary>
    Normal,

    /// <summary>恢复模式：管理员修复配置（第一批预留，未实现，见 ROADMAP）。</summary>
    Recovery
}

public sealed class SetupOptions
{
    public const string SectionName = "Setup";

    /// <summary>
    /// 可选开发便利开关：设为 true 可跳过首次安装引导（进入 Normal 模式）。
    /// 默认 false —— 即首次运行必须经过配置页（与 nopCommerce 一致），生产环境务必保持关闭。
    /// 本地开发想跳过时，可在 appsettings.Development.json 设置 "Setup": { "DevBypass": true }。
    /// </summary>
    public bool DevBypass { get; set; }
}

/// <summary>
/// 安装状态。Bootstrap 状态保存在本地标记文件，不依赖尚未配置的业务数据库。
/// Secret 加密存储、完整向导（PG/Redis/MinIO/SMTP/JWT 配置与连通性测试）为第二批，见 docs/ROADMAP.md。
/// </summary>
public sealed class SetupStateProvider(
    IOptions<SetupOptions> options,
    IHostEnvironment environment)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _markerDirectory = Path.Combine(environment.ContentRootPath, "data");

    private string MarkerPath => Path.Combine(_markerDirectory, "setup.json");

    public SetupMode CurrentMode
    {
        get
        {
            if (File.Exists(MarkerPath))
                return SetupMode.Normal;
            if (options.Value.DevBypass && environment.IsDevelopment())
                return SetupMode.Normal;
            return SetupMode.Bootstrap;
        }
    }

    public bool IsInstalled => File.Exists(MarkerPath);

    public void Complete(string adminEmail, string? systemName)
    {
        Directory.CreateDirectory(_markerDirectory);
        var payload = new
        {
            installedAtUtc = DateTimeOffset.UtcNow,
            adminEmail,
            systemName = string.IsNullOrWhiteSpace(systemName) ? "BlazorBoilerplate Platform" : systemName
        };
        File.WriteAllText(MarkerPath, JsonSerializer.Serialize(payload, JsonOptions));
    }

    public object GetStatus() => new
    {
        mode = CurrentMode.ToString(),
        installed = IsInstalled,
        devBypass = options.Value.DevBypass && environment.IsDevelopment()
    };
}

/// <summary>日志分类标记（非静态，供 ILogger&lt;T&gt; 使用，避免静态类作泛型参数触发 CS0718）。</summary>
file sealed class SetupLogCategory;

public static class SetupEndpoints
{
    public static void MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/setup").WithTags("Setup");

        group.MapGet("/status", (SetupStateProvider setup) => Results.Ok(setup.GetStatus()));

        // 批次 2：收集基础设施配置 → EF Core Migrations 建库建表 + 种子数据 → 标记安装完成 → 按序加载模块。
        // 迁移产物不含数据库级外键（参考 Nova 的 CustomNpgsqlMigrationsSqlGenerator）。
        group.MapPost("/complete", async (
            SetupConfiguration request,
            SetupStateProvider setup,
            ISetupInitializer initializer,
            ModuleLifecycleManager modules,
            IOptions<ModuleRuntimeOptions> runtimeOptions,
            IServiceProvider serviceProvider,
            ILogger<SetupLogCategory> logger,
            CancellationToken cancellationToken) =>
        {
            if (setup.IsInstalled)
                return Results.Conflict(new { error = "Platform is already installed." });

            try
            {
                await initializer.InitializeAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                return Results.Problem(title: "Setup initialization failed", detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            setup.Complete(request.AdminEmail, request.SystemName);

            // 首装填写的系统名称同步到系统设置，使 /system/settings 与安装向导保持一致。
            // 此时 system-settings.json 通常尚未生成，SystemSettingsStore 会从 IConfiguration
            // 继承默认值后写入；这里把 SystemName 覆盖为首装值并落盘。
            if (!string.IsNullOrWhiteSpace(request.SystemName))
            {
                var settingsStore = serviceProvider.GetRequiredService<SystemSettingsStore>();
                var current = settingsStore.GetRaw();
                if (!string.Equals(current.SystemName, request.SystemName.Trim(), StringComparison.Ordinal))
                {
                    current.SystemName = request.SystemName.Trim();
                    settingsStore.Update(current);
                }
            }

            // 安装完成后进入 Normal 模式：按 nopCommerce 风格自动加载全部已安装模块，
            // 使首次运行完成配置后无需重启即可使用。
            if (runtimeOptions.Value.AutoLoadOnStartup)
            {
                try
                {
                    var loaded = await modules.LoadAllAsync(cancellationToken);

                    // 与启动 auto-load、/api/modules/load-all 保持一致：模块加载后必须跑数据钩子，
                    // 否则首次安装完成的进程里 PermissionDefinitions / 菜单同步为空（要重启才补上）。
                    foreach (var descriptor in loaded.Where(d => d.State == ModuleState.Running))
                        await ModuleDataHooks.OnLoadedAsync(
                            serviceProvider, descriptor.Id, descriptor.PackagePath, cancellationToken);
                }
                catch (Exception ex)
                {
                    // 模块加载失败不应回滚安装；记录后继续，运维可在 /api/modules 排查。
                    logger.LogWarning(ex, "Module auto-load after setup had failures.");
                }
            }

            return Results.Ok(setup.GetStatus());
        });
    }

    /// <summary>Setup 门槛：Bootstrap 模式下仅开放 /api/setup、/health 与 /openapi。</summary>
    public static IApplicationBuilder UseSetupBootstrapGate(this IApplicationBuilder app)
    {
        var setup = app.ApplicationServices.GetRequiredService<SetupStateProvider>();
        return app.Use(async (context, next) =>
        {
            if (setup.CurrentMode == SetupMode.Bootstrap)
            {
                var path = context.Request.Path;
                var allowed = path.StartsWithSegments("/api/setup")
                              || path.StartsWithSegments("/api/localization")
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
    }
}
