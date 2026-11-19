using System.IO.Compression;
using System.Text.Json;
using BlazorBoilerplate.ApiHost.Auth;
using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.ModuleRuntime.Runtime;
using Microsoft.Extensions.Options;

namespace BlazorBoilerplate.ApiHost.Modules;

public static class ModuleManagementEndpoints
{
    public static void MapModuleManagementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/modules").WithTags("Modules");

        group.MapGet("/", (ModuleLifecycleManager manager) => Results.Ok(manager.List()))
            .WithSummary("List all modules (stable descriptors only)");

        // 页面「刷新」按钮：重新扫描安装目录发现新模块（List 只列已登记条目，
        // 手动放入 installed 目录的模块不 rescan 永远不会出现在列表里）。
        group.MapPost("/rescan", async (ModuleLifecycleManager manager, HttpContext context, CancellationToken ct) =>
            {
                var found = await manager.RefreshCatalogAsync(ct);
                return Results.Ok(new { found, modules = manager.List() });
            })
            .WithSummary("Re-scan the installed directory to discover newly added modules")
            .RequirePermission("module.load");

        group.MapGet("/load-order", (ModuleLifecycleManager manager) => Results.Ok(manager.ListOrdered()))
            .WithSummary("List modules in resolved nopCommerce-style load order (dependencies first, then DisplayOrder)");

        group.MapGet("/load-graph", (ModuleLifecycleManager manager) =>
            {
                var result = manager.ValidateLoadGraph();
                return Results.Ok(new
                {
                    isValid = result.IsValid,
                    missingDependencies = result.MissingDependencies,
                    cycles = result.Cycles
                });
            })
            .WithSummary("Validate the module load graph (missing dependencies + circular dependencies)");

        group.MapPost("/load-all", async (ModuleLifecycleManager manager, HttpContext context, CancellationToken cancellationToken) =>
            {
                try
                {
                    var descriptors = await manager.LoadAllAsync(cancellationToken);
                    foreach (var descriptor in descriptors.Where(d => d.State == ModuleState.Running))
                        await ModuleDataHooks.OnLoadedAsync(context.RequestServices, descriptor.Id, descriptor.PackagePath, cancellationToken);
                    return Results.Ok(descriptors);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new { error = ex.Message });
                }
            })
            .WithSummary("Load every module in resolved order; dependents are skipped if their dependencies fail")
            .RequirePermission("module.load");

        group.MapGet("/audit", (IModuleAudit audit) => Results.Ok(audit.Recent(100)))
            .WithSummary("Recent module lifecycle audit entries");

        group.MapGet("/{id}", (string id, ModuleLifecycleManager manager) =>
            TryGetDescriptor(id, manager, out var descriptor)
                ? Results.Ok(descriptor)
                : Results.NotFound(new { error = $"Module '{id}' not found." }));

        group.MapPost("/install", async (InstallModuleRequest request, ModuleLifecycleManager manager, HttpContext context, CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.PackageDirectory))
                    return Results.BadRequest(new { error = "packageDirectory is required." });
                try
                {
                    var descriptor = await manager.InstallAsync(request.PackageDirectory, cancellationToken);
                    // 安装即补齐模块声明的菜单路由与按钮权限码（菜单已存在则保留，不覆盖管理员改动）。
                    await ModuleDataHooks.OnInstalledAsync(context.RequestServices, descriptor.Id, descriptor.PackagePath, cancellationToken);
                    return Results.Ok(descriptor);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("escapes", StringComparison.Ordinal))
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .WithSummary("Install a module package from a directory under the controlled modules root")
            .RequirePermission("module.upload");

        group.MapGet("/{id}/versions", (string id, ModuleLifecycleManager manager) =>
            {
                try
                {
                    var entries = manager.ListVersions(id);
                    var active = manager.Get(id).Version;
                    return Results.Ok(entries.Select(e => new ModuleVersionDto(
                        e.Descriptor.Id, e.Descriptor.Version, e.Descriptor.State,
                        string.Equals(e.Descriptor.Version, active, StringComparison.OrdinalIgnoreCase),
                        e.Descriptor.LoadedAtUtc, e.Descriptor.Error)).ToArray());
                }
                catch (KeyNotFoundException) { return Results.NotFound(new { error = $"Module '{id}' not found." }); }
            })
            .WithSummary("List all versions of a module (semver desc), marking the active one");

        group.MapPost("/upload", async (IFormFile package, ModuleLifecycleManager manager, IFileStorage storage,
                    IOptions<ModuleRuntimeOptions> options, ILoggerFactory loggerFactory, HttpContext context,
                    CancellationToken cancellationToken) =>
                {
                    var logger = loggerFactory.CreateLogger("BlazorBoilerplate.ApiHost.Modules.Upload");
                    if (package is null || package.Length == 0)
                        return Results.BadRequest(new { error = "缺少上传文件（package）。" });
                    if (!package.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        return Results.BadRequest(new { error = "模块包必须是 .zip 压缩包。" });

                    await using var ms = new MemoryStream();
                    await package.CopyToAsync(ms, cancellationToken);

                    ModuleManifest? manifest = null;
                    ms.Position = 0;
                    using (var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
                    {
                        // 条目分隔符归一化：Windows 上部分压缩工具（如 PS 5.1 Compress-Archive）用 '\' 分隔。
                        var manifestEntry = archive.Entries.FirstOrDefault(e =>
                            string.Equals(NormalizeEntryName(e.FullName), "manifest.json", StringComparison.OrdinalIgnoreCase));
                        if (manifestEntry is null)
                            return Results.BadRequest(new { error = "压缩包缺少根目录 manifest.json。" });
                        await using var mstream = manifestEntry.Open();
                        manifest = await JsonSerializer.DeserializeAsync<ModuleManifest>(mstream,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
                    }
                    if (manifest is null)
                        return Results.BadRequest(new { error = "manifest.json 解析失败。" });

                    if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Version))
                        return Results.BadRequest(new { error = "manifest 必须声明 id 与 version。" });
                    if (!IsSafeName(manifest.Id) || !IsSafeName(manifest.Version))
                        return Results.BadRequest(new { error = "id / version 仅允许字母、数字与 . _ -。" });
                    if (!IsValidSemVer(manifest.Version))
                        return Results.BadRequest(new { error = "version 必须是合法语义版本（如 1.0.0）。" });
                    if (manifest.ContractVersion != PlatformContract.Version)
                        return Results.BadRequest(new
                        {
                            error = $"契约版本不匹配：module={manifest.ContractVersion}, host={PlatformContract.Version}。"
                        });
                    if (string.IsNullOrWhiteSpace(manifest.EntryAssembly) || string.IsNullOrWhiteSpace(manifest.EntryType))
                        return Results.BadRequest(new { error = "manifest 必须声明 entryAssembly 与 entryType。" });

                    var id = manifest.Id;
                    var version = manifest.Version;

                    try
                    {
                        var existing = manager.ListVersions(id)
                            .FirstOrDefault(e => string.Equals(e.Manifest.Version, version, StringComparison.OrdinalIgnoreCase));
                        if (existing is not null && existing.Descriptor.State is not
                            (ModuleState.Uninstalled or ModuleState.Failed or ModuleState.Quarantined))
                            return Results.Conflict(new
                            {
                                error = $"Module '{id}' v{version} 已安装（state={existing.Descriptor.State}），请勿重复上传。"
                            });
                    }
                    catch (KeyNotFoundException) { /* 模块尚不存在 */ }

                    ms.Position = 0;
                    using (var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
                    {
                        var entryPath = "backend/" + manifest.EntryAssembly;
                        // 分隔符归一化后再比对（部分压缩工具用 '\' 分隔条目）。
                        if (!archive.Entries.Any(e =>
                                string.Equals(NormalizeEntryName(e.FullName), entryPath, StringComparison.OrdinalIgnoreCase)))
                            return Results.BadRequest(new { error = $"压缩包缺少入口程序集 {entryPath}。" });
                    }

                    // 1) 原始 zip 写入 S3 / MinIO（对象存储为包归档源）。
                    var s3Key = $"modules/{id}/{version}/{id}-{version}.zip";
                    try
                    {
                        ms.Position = 0;
                        await storage.PutAsync(s3Key, ms, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "模块包上传 S3 失败。Key={Key}", s3Key);
                        return Results.Problem($"模块包上传到对象存储失败：{ex.Message}");
                    }

                    // 2) 解压到受控目录 modules/installed/{id}/{version}（防 zip-slip）。
                    var packagesRoot = Path.GetFullPath(options.Value.PackagesRoot);
                    var destDir = Path.GetFullPath(Path.Combine(packagesRoot, id, version));
                    var rootPrefix = packagesRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!destDir.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                        return Results.BadRequest(new { error = "非法的模块 id / version。" });
                    if (Directory.Exists(destDir))
                        Directory.Delete(destDir, recursive: true);
                    Directory.CreateDirectory(destDir);

                    ms.Position = 0;
                    using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            var target = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
                            if (!target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                                return Results.BadRequest(new { error = $"压缩包含非法路径（zip-slip）：{entry.FullName}" });
                            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                            {
                                Directory.CreateDirectory(target);
                                continue;
                            }
                            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                            await using var es = entry.Open();
                            await using var fs = File.Create(target);
                            await es.CopyToAsync(fs, cancellationToken);
                        }
                    }

                    // 3) 登记 / 安装新版本（自动校验 manifest、契约版本、入口程序集、校验和）。
                    try
                    {
                        var descriptor = await manager.InstallAsync(destDir, cancellationToken);
                        // 安装即补菜单/权限码；加载时再执行增量数据库迁移（只增不减）。
                        await ModuleDataHooks.OnInstalledAsync(context.RequestServices, descriptor.Id, descriptor.PackagePath, cancellationToken);
                        logger.LogInformation("模块上传并安装成功。Id={Id}, Version={Version}, S3={S3}", id, version, s3Key);
                        return Results.Ok(new { descriptor, s3Key });
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.Conflict(new { error = ex.Message });
                    }
                })
            .WithSummary("上传模块包（.zip）至 S3/MinIO 并解压安装为新版本")
            .DisableAntiforgery()
            .RequirePermission("module.upload");

        group.MapGet("/{id}/download", async (string id, string? version, ModuleLifecycleManager manager,
                    IFileStorage storage, CancellationToken cancellationToken) =>
                {
                    ModuleDescriptor descriptor;
                    try { descriptor = manager.Get(id); }
                    catch (KeyNotFoundException) { return Results.NotFound(new { error = $"Module '{id}' not found." }); }

                    // 默认下载当前激活版本；?version= 可下载历史版本。
                    var ver = string.IsNullOrWhiteSpace(version) ? descriptor.Version : version;
                    var fileName = $"{id}-{ver}.zip";

                    // 1) 优先取上传时归档到 S3/MinIO 的原始包（字节级还原上传内容）。
                    var s3Key = $"modules/{id}/{ver}/{id}-{ver}.zip";
                    var archived = await storage.GetAsync(s3Key, cancellationToken);
                    if (archived is not null)
                        return Results.Stream(archived, "application/zip", fileDownloadName: fileName);

                    // 2) 回退：现场打包已安装目录（内置模块如 identity 从未经上传，无 S3 归档）。
                    var sourceDir = manager.ListVersions(id)
                        .FirstOrDefault(e => string.Equals(e.Descriptor.Version, ver, StringComparison.OrdinalIgnoreCase))
                        ?.Descriptor.PackagePath;
                    if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
                        return Results.NotFound(new { error = $"模块 {id} v{ver} 既无对象存储归档，也无已安装目录，无法下载。" });

                    var ms = new MemoryStream();
                    ZipFile.CreateFromDirectory(sourceDir, ms, CompressionLevel.Optimal, includeBaseDirectory: false);
                    ms.Position = 0;
                    return Results.Stream(ms, "application/zip", fileDownloadName: fileName);
                })
            .WithSummary("下载模块包（.zip）：优先 S3 归档，内置模块现场打包安装目录；?version= 指定历史版本")
            .RequirePermission("module.download");

        // ---- 开发打包（仅 Development + 配置 ModuleRuntime:SourceRoot 时启用）----

        group.MapGet("/pack/info", (ModulePackService pack) =>
                Results.Ok(new { enabled = pack.IsEnabled, sources = pack.ListSources() }))
            .WithSummary("开发打包能力状态与模块源码工程清单");

        group.MapPost("/pack", async (PackModuleRequest request, ModulePackService pack,
                    CancellationToken cancellationToken) =>
                {
                    if (!pack.IsEnabled)
                        return Results.BadRequest(new { error = "打包功能仅在开发环境启用（需 Development 环境 + ModuleRuntime:SourceRoot 配置）。" });
                    if (string.IsNullOrWhiteSpace(request.Source))
                        return Results.BadRequest(new { error = "source（源码工程名）为必填。" });
                    try
                    {
                        var (content, fileName) = await pack.PackAsync(request.Source, cancellationToken);
                        return Results.Stream(content, "application/zip", fileDownloadName: fileName);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
                    {
                        return Results.BadRequest(new { error = ex.Message });
                    }
                })
            .WithSummary("按源码工程打包模块（dotnet build Release → 组包 → .zip 流），打包后可直接上传安装")
            .DisableAntiforgery()
            .RequirePermission("module.pack");

        group.MapPost("/{id}/load", async (string id, string? version, ModuleLifecycleManager manager,
                    HttpContext context, CancellationToken cancellationToken) =>
            {
                try
                {
                    var descriptor = await manager.LoadAsync(id, cancellationToken, version);
                    // 增量迁移（Sequence 单调递增，降级不回滚）+ 菜单恢复/补齐 + 权限码登记。
                    await ModuleDataHooks.OnLoadedAsync(context.RequestServices, id, descriptor.PackagePath, cancellationToken);
                    return Results.Ok(descriptor);
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new { error = $"Module '{id}' not found." });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new { error = ex.Message });
                }
            })
            .WithSummary("Load and run a module (collectible ALC + dynamic endpoints); optional ?version=")
            .RequirePermission("module.load");

        group.MapPost("/{id}/unload", async (string id, ModuleLifecycleManager manager,
                    HttpContext context, CancellationToken cancellationToken) =>
            {
                try
                {
                    await manager.UnloadAsync(id, cancellationToken);
                    // 菜单只禁用不删除；数据库结构与迁移历史一律保留（重新 Load 立即恢复）。
                    await ModuleDataHooks.OnUnloadedAsync(context.RequestServices, id, cancellationToken);
                    // 动态卸载后回到 Installed（包仍在 modules/installed，可再次 Load）。
                    return Results.Ok(new { id, state = manager.Get(id).State.ToString() });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new { error = ex.Message });
                }
            })
            .WithSummary("Drain, stop and unload a module without restarting the host")
            .RequirePermission("module.unload");
    }

    private static bool TryGetDescriptor(string id, ModuleLifecycleManager manager, out ModuleDescriptor descriptor)
    {
        try
        {
            descriptor = manager.Get(id);
            return true;
        }
        catch (KeyNotFoundException)
        {
            descriptor = null!;
            return false;
        }
    }

    /// <summary>id / version 仅允许字母、数字与 . _ -，杜绝路径穿越与非法文件系统/对象键。</summary>
    private static bool IsSafeName(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-');

    /// <summary>zip 条目名统一为 '/' 分隔，兼容 Windows 压缩工具产生的 '\' 条目。</summary>
    private static string NormalizeEntryName(string fullName) => fullName.Replace('\\', '/');

    /// <summary>语义版本：1~4 段数字（如 1 / 1.2 / 1.2.3 / 1.2.3.4）。</summary>
    private static bool IsValidSemVer(string value)
    {
        if (!IsSafeName(value)) return false;
        var segs = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return segs.Length is >= 1 and <= 4 && segs.All(s => s.Length <= 9 && int.TryParse(s, out _));
    }
}

public sealed record InstallModuleRequest(string PackageDirectory);

/// <summary>POST /api/modules/pack 请求体：源码工程名（pack/info 返回的 name）。</summary>
public sealed record PackModuleRequest(string Source);
