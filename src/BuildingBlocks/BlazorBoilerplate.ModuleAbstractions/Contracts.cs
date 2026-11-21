using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.ModuleAbstractions;

/// <summary>
/// 平台 SDK 稳定契约。Host 与每个模块共享【同一份】程序集实例：
/// 模块 ALC 必须把它们解析到默认上下文，否则类型身份不一致。
/// 这里只允许放稳定接口、标识、版本与跨边界 DTO，禁止放模块私有实现类型。
/// </summary>

public sealed class ModuleManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>显示名称（如中文名）。为空时 UI 回退到 Name。</summary>
    public string? DisplayName { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string EntryAssembly { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty;
    public string ContractVersion { get; set; } = "1.0";
    public string ModuleSdkVersion { get; set; } = "1.0";
    public string HostCompatibility { get; set; } = ">=1.0.0 <2.0.0";

    /// <summary>
    /// 模块加载顺序。数值越小越先加载；普通模块默认 3000，特殊基础模块可使用更小序号。
    /// </summary>
    public int DisplayOrder { get; set; } = ModuleStartupDefaults.DefaultOrder;

    /// <summary>
    /// 依赖的模块 Id 清单（nopCommerce 的 DependsOnSystemNames）。
    /// 被依赖的模块一定先于本模块加载；缺失依赖或成环将导致加载图校验失败。
    /// </summary>
    public string[] Dependencies { get; set; } = [];

    /// <summary>
    /// 必要模块：装机后始终加载且【不允许卸载】（平台增强，nopCommerce 无此概念）。
    /// 其余模块可经 API/UI 动态加载与卸载。
    /// </summary>
    public bool IsEssential { get; set; }

    public string[] Permissions { get; set; } = [];

    /// <summary>结构化权限声明（含中文展示名称与类型）；与 Permissions 兼容，模块加载时同步到 PermissionDefinitions。</summary>
    public ModulePermissionManifest[] PermissionItems { get; set; } = [];

    public ModuleFrontendManifest Frontend { get; set; } = new();

    /// <summary>模块数据库声明：启用后宿主在模块加载时执行只增不减的增量迁移。</summary>
    public ModuleDatabaseManifest Database { get; set; } = new();
    public string? ChecksumSha256 { get; set; }
}

public sealed class ModuleFrontendManifest
{
    /// <summary>独立前端 RCL 程序集名（位于包内 frontend/ 目录）。为空时兼容旧单 DLL 模式回退到 backend/entryAssembly。</summary>
    public string? Assembly { get; set; }
    public string[] Routes { get; set; } = [];
    public string[] Menus { get; set; } = [];
    public string[] Localization { get; set; } = [];
    public string[] Assets { get; set; } = [];

    /// <summary>
    /// 模块声明的侧边栏菜单（结构化）。安装向导会把它写入「菜单管理」的数据库表，
    /// 之后由管理员在 /identity/menus 维护，无需改代码。
    /// </summary>
    public ModuleMenuManifest[] MenuItems { get; set; } = [];
}

public sealed class ModulePermissionManifest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "button";
}

/// <summary>模块在 manifest.json 中声明的菜单项。</summary>
public sealed class ModuleMenuManifest
{
    public string Text { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int Sort { get; set; }
}

public sealed class ModuleEndpointDefinition
{
    public required string Pattern { get; init; }
    public required string HttpMethod { get; init; }
    public string Permission { get; init; } = string.Empty;
    public required RequestDelegate Handler { get; init; }
}

public interface IModuleEndpointBuilder
{
    void Map(string pattern, string httpMethod, string permission, RequestDelegate handler);
}

public interface IModuleContext
{
    string ModuleId { get; }
    string Version { get; }
    IServiceProvider HostServices { get; }
    ILoggerFactory LoggerFactory { get; }
    IModuleAudit Audit { get; }
    CancellationToken HostStopping { get; }
}

/// <summary>
/// 模块启动配置契约。
/// 模块程序集内可定义零个或多个实现，运行时按 Order 升序依次执行服务注册与端点映射。
/// </summary>
public interface IModuleStartup
{
    /// <summary>注册模块服务。</summary>
    void ConfigureServices(IServiceCollection services, IModuleContext context);

    /// <summary>映射模块端点（可选，默认空实现）。</summary>
    void MapEndpoints(IModuleEndpointBuilder builder, IServiceProvider moduleServices) { }

    /// <summary>启动配置顺序（默认 3000，仅特殊模块指定其他序号）。</summary>
    int Order => ModuleStartupDefaults.DefaultOrder;
}

public interface IModule : IAsyncDisposable
{
    void ConfigureServices(IServiceCollection services, IModuleContext context);
    void MapEndpoints(IModuleEndpointBuilder builder, IServiceProvider moduleServices);
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAcceptingAsync(CancellationToken cancellationToken);
    Task DrainAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 模块安装/升级/卸载业务钩子。
/// </summary>
public interface IModuleLifecycleHooks
{
    /// <summary>首次安装模块时触发。</summary>
    Task InstallAsync(IModuleContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>模块跨版本升级时触发。</summary>
    Task UpdateAsync(IModuleContext context, string previousVersion, string targetVersion, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>彻底卸载模块时触发。</summary>
    Task UninstallAsync(IModuleContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}

public interface IModuleAudit
{
    void Record(ModuleAuditEntry entry);
    IReadOnlyList<ModuleAuditEntry> Recent(int count);
}

public static class ModulePackageLayout
{
    public const string ManifestFile = "manifest.json";
    public const string BackendFolder = "backend";
    public const string FrontendFolder = "frontend";
    public const string MigrationsFolder = "migrations";
    public const string LocalizationFolder = "localization";
    public const string ChecksumsFile = "checksums.sha256";
}

public static class PlatformContract
{
    public const string Version = "1.0";
    public const string HostVersion = "1.0.0";
}
