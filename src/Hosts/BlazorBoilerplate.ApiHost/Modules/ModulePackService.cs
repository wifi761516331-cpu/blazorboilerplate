using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.ModuleRuntime.Runtime;
using Microsoft.Extensions.Options;

namespace BlazorBoilerplate.ApiHost.Modules;

/// <summary>模块源码工程信息（/api/modules/pack/info 返回）。</summary>
public sealed record ModuleSourceInfo(string Name, string Directory, string Id, string Version);

/// <summary>开发环境模块打包服务：把 src/Modules 下的模块工程 build + 组包成可上传的 .zip。
/// 仅在 Development 环境且配置了 ModuleRuntime:SourceRoot 时启用。</summary>
public sealed class ModulePackService(
    IOptions<ModuleRuntimeOptions> options,
    IWebHostEnvironment environment,
    ILogger<ModulePackService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private string? SourceRoot
    {
        get
        {
            var root = options.Value.SourceRoot;
            if (string.IsNullOrWhiteSpace(root)) return null;
            var full = Path.GetFullPath(root, Directory.GetCurrentDirectory());
            return Directory.Exists(full) ? full : null;
        }
    }

    public bool IsEnabled => environment.IsDevelopment() && SourceRoot is not null;

    /// <summary>扫描源码根目录（含两层：分类目录/工程目录），列出含 manifest.json + .csproj 的模块工程。</summary>
    public IReadOnlyList<ModuleSourceInfo> ListSources()
    {
        var root = SourceRoot;
        if (root is null) return [];
        var sources = new List<ModuleSourceInfo>();
        // 兼容 src/Modules/<工程> 与 src/Modules/<分类>/<工程> 两种布局。
        var projectDirs = Directory.EnumerateDirectories(root)
            .SelectMany(d => Directory.EnumerateFileSystemEntries(d).Any(File.Exists)
                ? [d]
                : Directory.EnumerateDirectories(d))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in projectDirs)
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;
            if (!Directory.EnumerateFiles(dir, "*.csproj").Any()) continue;
            string? id = null, version = null;
            try
            {
                using var stream = File.OpenRead(manifestPath);
                var manifest = JsonSerializer.Deserialize<ModuleManifest>(stream, JsonOptions);
                id = manifest?.Id;
                version = manifest?.Version;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "解析 {Manifest} 失败，跳过该工程", manifestPath);
                continue;
            }
            if (!string.IsNullOrWhiteSpace(id))
                sources.Add(new ModuleSourceInfo(Path.GetFileName(dir), dir, id, version ?? "-"));
        }
        return sources.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>按源码工程名打包：dotnet build Release → 组包（manifest + backend + 可选目录）→ zip 流。</summary>
    public async Task<(Stream Content, string FileName)> PackAsync(string sourceName, CancellationToken cancellationToken)
    {
        var root = SourceRoot ?? throw new InvalidOperationException("未配置 ModuleRuntime:SourceRoot，无法打包。");
        // sourceName 是工程目录名；按 ListSources 的两层布局解析真实路径（天然防路径穿越）。
        var sourceDir = ListSources().FirstOrDefault(s => s.Name == sourceName)?.Directory
                        ?? throw new InvalidOperationException($"找不到模块源码工程：{sourceName}");
        if (!File.Exists(Path.Combine(sourceDir, "manifest.json")))
            throw new FileNotFoundException("该工程目录缺少 manifest.json。", Path.Combine(sourceDir, "manifest.json"));

        ModuleManifest manifest;
        using (var stream = File.OpenRead(Path.Combine(sourceDir, "manifest.json")))
            manifest = await JsonSerializer.DeserializeAsync<ModuleManifest>(stream, JsonOptions, cancellationToken)
                       ?? throw new InvalidOperationException("manifest.json 解析失败。");
        if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.EntryAssembly))
            throw new InvalidOperationException("manifest 必须声明 id / version / entryAssembly。");

        // 1) dotnet build -c Release（注入 -p:Version=manifest.version：
        //    让程序集身份随包版本变化，WebHost 才能在重启后按版本优先加载包内前端组件 DLL）
        var csproj = Directory.EnumerateFiles(sourceDir, "*.csproj").FirstOrDefault()
                     ?? throw new FileNotFoundException("工程目录下没有 .csproj。");
        logger.LogInformation("打包模块 {Id}：开始构建 {Csproj}（v{Version}）", manifest.Id, csproj, manifest.Version);
        var buildOut = await RunDotnetBuildAsync(csproj, manifest.Version, cancellationToken);
        var entryName = Path.GetFileName(manifest.EntryAssembly);
        var entryDll = FindBuildOutput(buildOut, entryName)
                       ?? throw new FileNotFoundException($"构建产物缺少 {entryName}。");
        var depsFile = Path.Combine(Path.GetDirectoryName(entryDll)!, Path.GetFileNameWithoutExtension(entryName) + ".deps.json");

        // 2) 组包（临时目录 → zip）
        var stage = Path.Combine(Path.GetTempPath(), "blazorboilerplate-pack", $"{manifest.Id}-{manifest.Version}");
        if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        try
        {
            Directory.CreateDirectory(Path.Combine(stage, "backend"));
            File.Copy(Path.Combine(sourceDir, "manifest.json"), Path.Combine(stage, "manifest.json"), overwrite: true);
            File.Copy(entryDll, Path.Combine(stage, "backend", entryName), overwrite: true);
            if (File.Exists(depsFile))
                File.Copy(depsFile, Path.Combine(stage, "backend", Path.GetFileName(depsFile)), overwrite: true);
            foreach (var folder in new[] { ModulePackageLayout.FrontendFolder, ModulePackageLayout.MigrationsFolder, ModulePackageLayout.LocalizationFolder })
            {
                var src = Path.Combine(sourceDir, folder);
                if (Directory.Exists(src))
                    CopyDirectory(src, Path.Combine(stage, folder));
            }
            if (!string.IsNullOrWhiteSpace(manifest.Frontend.Assembly))
            {
                var frontendCsproj = FindFrontendCsproj(sourceDir, manifest.Frontend.Assembly);
                if (frontendCsproj is not null)
                {
                    var frontendBuildOut = await RunDotnetBuildAsync(frontendCsproj, manifest.Version, cancellationToken);
                    var frontendName = Path.GetFileName(manifest.Frontend.Assembly);
                    var frontendDll = FindBuildOutput(frontendBuildOut, frontendName)
                                      ?? throw new FileNotFoundException($"前端构建产物缺少 {frontendName}。");
                    var frontendStage = Path.Combine(stage, ModulePackageLayout.FrontendFolder);
                    Directory.CreateDirectory(frontendStage);
                    File.Copy(frontendDll, Path.Combine(frontendStage, frontendName), overwrite: true);
                    var frontendDeps = Path.Combine(Path.GetDirectoryName(frontendDll)!,
                        Path.GetFileNameWithoutExtension(frontendName) + ".deps.json");
                    if (File.Exists(frontendDeps))
                        File.Copy(frontendDeps, Path.Combine(frontendStage, Path.GetFileName(frontendDeps)), overwrite: true);
                }
            }
            var checksums = Path.Combine(sourceDir, "checksums.sha256");
            if (File.Exists(checksums)) File.Copy(checksums, Path.Combine(stage, "checksums.sha256"));

            var ms = new MemoryStream();
            ZipFile.CreateFromDirectory(stage, ms, CompressionLevel.Optimal, includeBaseDirectory: false);
            ms.Position = 0;
            logger.LogInformation("打包完成 {Id} v{Version}（{Bytes} 字节）", manifest.Id, manifest.Version, ms.Length);
            return (ms, $"{manifest.Id}-{manifest.Version}.zip");
        }
        finally
        {
            try { Directory.Delete(stage, recursive: true); } catch { /* 清理失败不影响交付 */ }
        }
    }

    /// <summary>
    /// 开发期启动自动同步：在模块 ALC 加载前，把 src/Modules 下已有最新构建产物同步到
    /// modules/installed/{id}/{version}/，避免改完源码 F5 启动仍加载到旧版包 DLL。
    /// </summary>
    public int SyncDevModulesToInstalled(string packagesRoot)
    {
        if (!IsEnabled) return 0;

        var synced = 0;
        foreach (var source in ListSources())
        {
            var manifestPath = Path.Combine(source.Directory, "manifest.json");
            ModuleManifest? manifest;
            try
            {
                using var stream = File.OpenRead(manifestPath);
                manifest = JsonSerializer.Deserialize<ModuleManifest>(stream, JsonOptions);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "启动同步跳过无法解析的 manifest：{Path}", manifestPath);
                continue;
            }

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id) ||
                string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.EntryAssembly))
                continue;

            var entryName = Path.GetFileName(manifest.EntryAssembly);
            var debugDll = FindBuildOutput(Path.Combine(source.Directory, "bin", "Debug"), entryName);
            var releaseDll = FindBuildOutput(Path.Combine(source.Directory, "bin", "Release"), entryName);
            var sourceDll = (debugDll, releaseDll) switch
            {
                (not null, not null) => File.GetLastWriteTimeUtc(debugDll) >= File.GetLastWriteTimeUtc(releaseDll) ? debugDll : releaseDll,
                (not null, null) => debugDll,
                (null, not null) => releaseDll,
                _ => null
            };
            if (sourceDll is null) continue;

            var moduleRoot = Path.Combine(packagesRoot, manifest.Id);
            var targetVersion = manifest.Version;
            if (Directory.Exists(moduleRoot))
            {
                var latestExisting = Directory.EnumerateDirectories(moduleRoot)
                    .Select(Path.GetFileName)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .OrderByDescending(v => Version.TryParse(v, out var parsed) ? parsed : new Version(0, 0), Comparer<Version>.Default)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(latestExisting))
                    targetVersion = latestExisting;
            }

            var installedDir = Path.Combine(moduleRoot, targetVersion);
            var installedBackend = Path.Combine(installedDir, "backend");
            var installedDll = Path.Combine(installedBackend, entryName);
            var installedManifest = Path.Combine(installedDir, "manifest.json");

            string? frontendSourceDll = null;
            string? frontendName = null;
            string? installedFrontendDll = null;
            if (!string.IsNullOrWhiteSpace(manifest.Frontend.Assembly))
            {
                frontendName = Path.GetFileName(manifest.Frontend.Assembly);
                var frontendDir = FindFrontendProjectDir(source.Directory, manifest.Frontend.Assembly);
                if (frontendDir is not null)
                {
                    var fDebug = FindBuildOutput(Path.Combine(frontendDir, "bin", "Debug"), frontendName);
                    var fRelease = FindBuildOutput(Path.Combine(frontendDir, "bin", "Release"), frontendName);
                    frontendSourceDll = (fDebug, fRelease) switch
                    {
                        (not null, not null) => File.GetLastWriteTimeUtc(fDebug) >= File.GetLastWriteTimeUtc(fRelease) ? fDebug : fRelease,
                        (not null, null) => fDebug,
                        (null, not null) => fRelease,
                        _ => null
                    };
                }
                installedFrontendDll = Path.Combine(installedDir, ModulePackageLayout.FrontendFolder, frontendName);
            }

            var needsBackendSync = !File.Exists(installedDll)
                                   || File.GetLastWriteTimeUtc(sourceDll) > File.GetLastWriteTimeUtc(installedDll)
                                   || !File.Exists(installedManifest)
                                   || File.GetLastWriteTimeUtc(manifestPath) > File.GetLastWriteTimeUtc(installedManifest);
            var needsFrontendSync = frontendSourceDll is not null && installedFrontendDll is not null &&
                                    (!File.Exists(installedFrontendDll) ||
                                     File.GetLastWriteTimeUtc(frontendSourceDll) > File.GetLastWriteTimeUtc(installedFrontendDll));
            if (!needsBackendSync && !needsFrontendSync) continue;

            if (needsBackendSync)
            {
                Directory.CreateDirectory(installedBackend);
                File.Copy(manifestPath, installedManifest, overwrite: true);
                File.Copy(sourceDll, installedDll, overwrite: true);

                var depsFile = Path.Combine(Path.GetDirectoryName(sourceDll)!,
                    Path.GetFileNameWithoutExtension(entryName) + ".deps.json");
                if (File.Exists(depsFile))
                    File.Copy(depsFile, Path.Combine(installedBackend, Path.GetFileName(depsFile)), overwrite: true);

                foreach (var folder in new[] { ModulePackageLayout.FrontendFolder, ModulePackageLayout.MigrationsFolder, ModulePackageLayout.LocalizationFolder })
                {
                    var src = Path.Combine(source.Directory, folder);
                    if (Directory.Exists(src))
                        CopyDirectory(src, Path.Combine(installedDir, folder));
                }
            }

            if (needsFrontendSync && frontendSourceDll is not null && installedFrontendDll is not null && frontendName is not null)
            {
                var installedFrontendDir = Path.GetDirectoryName(installedFrontendDll)!;
                Directory.CreateDirectory(installedFrontendDir);
                File.Copy(frontendSourceDll, installedFrontendDll, overwrite: true);
                var frontendDeps = Path.Combine(Path.GetDirectoryName(frontendSourceDll)!,
                    Path.GetFileNameWithoutExtension(frontendName) + ".deps.json");
                if (File.Exists(frontendDeps))
                    File.Copy(frontendDeps, Path.Combine(installedFrontendDir, Path.GetFileName(frontendDeps)), overwrite: true);
            }

            synced++;
            logger.LogInformation("开发期自动同步模块包：{Id} v{Version}（后端={BackendSync}, 前端={FrontendSync}）",
                manifest.Id, manifest.Version, needsBackendSync, needsFrontendSync);
        }

        return synced;
    }

    private async Task<string> RunDotnetBuildAsync(string csproj, string version, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("dotnet", $"build \"{csproj}\" -c Release -p:Version={version} --nologo -v q")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(csproj)!
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("dotnet build 启动失败。");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        // 打包构建给足时间（冷构建含还原可能数十秒），超时视为失败。
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("dotnet build 超时（5 分钟）。");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"dotnet build 失败（exit={process.ExitCode}）：\n{stderr}\n{stdout}");
        var binDir = Path.Combine(Path.GetDirectoryName(csproj)!, "bin", "Release");
        return binDir;
    }

    private static string? FindFrontendProjectDir(string sourceDir, string frontendAssembly)
    {
        var baseName = Path.GetFileNameWithoutExtension(frontendAssembly);
        var parent = Path.GetDirectoryName(sourceDir);
        if (string.IsNullOrEmpty(parent)) return null;
        var candidate = Path.Combine(parent, baseName);
        return Directory.Exists(candidate) ? candidate : null;
    }

    private static string? FindFrontendCsproj(string sourceDir, string frontendAssembly)
    {
        var dir = FindFrontendProjectDir(sourceDir, frontendAssembly);
        return dir is null ? null : Directory.EnumerateFiles(dir, "*.csproj").FirstOrDefault();
    }

    private static string? FindBuildOutput(string binDir, string entryName)
    {
        if (!Directory.Exists(binDir)) return null;
        // 优先 net10.0，其次任一 TFM 目录（非 ref 目录）。
        var candidates = Directory.EnumerateDirectories(binDir)
            .Where(d => !Path.GetFileName(d).Equals("ref", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);
        foreach (var dir in candidates)
        {
            var dll = Path.Combine(dir, entryName);
            if (File.Exists(dll)) return dll;
        }
        return null;
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var target = Path.Combine(targetDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
