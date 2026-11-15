using System.Text.Json;
using BlazorBoilerplate.ApiHost.Services;
using BlazorBoilerplate.ApiHost.Setup;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.ModuleRuntime;
using BlazorBoilerplate.ModuleRuntime.Runtime;
using BlazorBoilerplate.Modules.Identity;
using BlazorBoilerplate.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;
using Serilog;

namespace BlazorBoilerplate.ApiHost.Modules;

/// <summary>ApiHost 动态模块运行时注册与启动期发现/加载编排。</summary>
public static class ModuleStartupExtensions
{
    public static IServiceCollection AddApiHostModules(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        // 宿主 DI 也注册一份 IdentityDbContext：Controller 是宿主编译期类型，无法解析模块私有 ALC 容器里的注册。
        // 与模块容器共用 PlatformConnectionState 的同一连接串，两侧互不影响。
        services.AddDbContext<IdentityDbContext>(o => o.UseIdentityProvider());
        services.AddScoped<ModuleMigrationRunner>();
        services.AddScoped<ModuleMenuSyncService>();

        // 必要模块 identity 的迁移由宿主兜底：已安装的旧版本包可能还没有迁移声明，
        // 但宿主的 DbContext / Controller 已按新模型编译，升级时结构必须先补齐。
        services.AddSingleton(new HostModuleMigration(
            IdentityModule.ModuleId, new IdentityDatabaseMigrations()));

        // 把模块包根目录解析为绝对路径（env BLAZORBOILERPLATE_MODULES_ROOT 优先）
        var modulesRoot = Environment.GetEnvironmentVariable("BLAZORBOILERPLATE_MODULES_ROOT");
        services.PostConfigure<ModuleRuntimeOptions>(options =>
        {
            options.PackagesRoot = !string.IsNullOrWhiteSpace(modulesRoot)
                ? Path.GetFullPath(modulesRoot)
                : Path.GetFullPath(Path.Combine(contentRootPath, options.PackagesRoot));

            // 开发打包：默认指向解决方案内的模块源码目录（仅 Development 环境生效）。
            if (string.IsNullOrWhiteSpace(options.SourceRoot))
            {
                options.SourceRoot = Path.GetFullPath(Path.Combine(
                    contentRootPath, "..", "..", "..", "src", "Modules"));
            }
        });

        services.AddSingleton<ModulePackService>();
        services.AddBlazorBoilerplateModuleRuntime(configuration);
        return services;
    }

    /// <summary>启动时恢复平台连接状态，并按 nopCommerce 顺序发现、加载已安装模块及执行增量迁移。</summary>
    public static async Task InitializeModulesOnStartupAsync(this WebApplication app)
    {
        RestorePlatformConnectionState(app.Environment.ContentRootPath);

        await using var scope = app.Services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<SetupStateProvider>();
        var manager = scope.ServiceProvider.GetRequiredService<ModuleLifecycleManager>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<ModuleRuntimeOptions>>().Value;
        var systemSettings = scope.ServiceProvider.GetRequiredService<SystemSettingsStore>().GetRaw();

        options.AutoLoadOnStartup = systemSettings.AutoLoadOnStartup;
        options.VerifyChecksum = systemSettings.VerifyChecksum;
        options.DrainTimeoutSeconds = systemSettings.DrainTimeoutSeconds;

        var discovered = await manager.RefreshCatalogAsync(CancellationToken.None);
        Log.Information("Module catalog refreshed. Discovered={Count}, Mode={Mode}", discovered, setup.CurrentMode);

        if (options.AutoLoadOnStartup && setup.CurrentMode == SetupMode.Normal && discovered > 0)
        {
            var loaded = await manager.LoadAllAsync(CancellationToken.None, respectPersistedState: true);
            var running = loaded.Count(d => d.State == ModuleState.Running);
            Log.Information(
                "nopCommerce-style auto-load at startup. Loaded={Running}/{Total}, Mode={Mode}",
                running, loaded.Count, setup.CurrentMode);

            foreach (var descriptor in loaded)
            {
                if (descriptor.State == ModuleState.Running)
                    await ModuleDataHooks.OnLoadedAsync(
                        scope.ServiceProvider, descriptor.Id, descriptor.PackagePath, CancellationToken.None);
                else
                    await ModuleDataHooks.OnUnloadedAsync(
                        scope.ServiceProvider, descriptor.Id, CancellationToken.None);
            }
        }
    }

    private static void RestorePlatformConnectionState(string contentRoot)
    {
        var path = Path.Combine(contentRoot, "data", "blazorboilerplate.settings.json");
        if (!File.Exists(path))
            return;

        try
        {
            var json = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            var provider = json.TryGetProperty("databaseProvider", out var p) ? p.GetString() : null;
            var infra = json.TryGetProperty("infrastructure", out var i) ? i : default;
            string? pg = infra.ValueKind != JsonValueKind.Undefined && infra.TryGetProperty("postgreSQL", out var pgp)
                ? pgp.GetString()
                : null;
            string? sqlite = infra.ValueKind != JsonValueKind.Undefined && infra.TryGetProperty("sqlite", out var sp)
                ? sp.GetString()
                : null;
            PlatformConnectionState.Set(provider, pg, sqlite);
            Log.Information("Platform connection state restored. Provider={Provider}", PlatformConnectionState.DatabaseProvider);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to restore platform connection state; modules may fall back to defaults.");
        }
    }
}

