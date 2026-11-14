using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlazorBoilerplate.ModuleRuntime.Runtime;

public sealed class ModuleRuntimeOptions
{
    public const string SectionName = "ModuleRuntime";
    public string PackagesRoot { get; set; } = "modules/installed";
    public string StagingRoot { get; set; } = "modules/staging";
    public string QuarantineRoot { get; set; } = "modules/quarantine";
    public int DrainTimeoutSeconds { get; set; } = 15;
    public bool VerifyChecksum { get; set; } = true;

    /// <summary>
    /// 开发打包：模块源码根目录（其下每个子目录含 manifest.json + .csproj 视为一个模块工程）。
    /// 仅当宿主处于 Development 环境时暴露 /api/modules/pack 打包能力。
    /// </summary>
    public string? SourceRoot { get; set; }

    /// <summary>
    /// nopCommerce 风格：应用启动时自动按序加载全部已安装模块（依赖优先 + DisplayOrder）。
    /// 与 nopCommerce 一致 —— 已安装的插件在每次启动时都会被激活。
    /// 设为 false 可回退到“只发现+登记、手动触发加载”的行为。
    /// </summary>
    public bool AutoLoadOnStartup { get; set; } = true;
}

public sealed class InMemoryModuleAudit : IModuleAudit
{
    private readonly object _gate = new();
    private readonly List<ModuleAuditEntry> _entries = [];
    private const int MaxEntries = 1000;

    public void Record(ModuleAuditEntry entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
        }
    }

    public IReadOnlyList<ModuleAuditEntry> Recent(int count)
    {
        lock (_gate)
            return _entries.TakeLast(Math.Clamp(count, 0, MaxEntries)).ToArray();
    }
}

/// <summary>合法状态迁移表。管理 API 不得直接改状态。</summary>
public static class ModuleStateMachine
{
    private static readonly Dictionary<ModuleState, ModuleState[]> Transitions = new()
    {
        [ModuleState.Discovered] = [ModuleState.Verifying, ModuleState.Quarantined, ModuleState.Uninstalled],
        [ModuleState.Verifying] = [ModuleState.Staging, ModuleState.Failed, ModuleState.Quarantined],
        [ModuleState.Staging] = [ModuleState.Installed, ModuleState.Failed],
        [ModuleState.Installed] = [ModuleState.Loading, ModuleState.Uninstalled],
        [ModuleState.Loading] = [ModuleState.Running, ModuleState.Failed],
        [ModuleState.Running] = [ModuleState.Draining, ModuleState.Disabling, ModuleState.Updating, ModuleState.Failed],
        [ModuleState.Draining] = [ModuleState.Unloading, ModuleState.Disabled, ModuleState.Failed],
        // 动态卸载 = 停用但保留已安装包（可再次加载），因此回到 Installed；
        // Uninstalled 仅用于真正从目录移除包（RefreshCatalog 时重新发现）。
        [ModuleState.Unloading] = [ModuleState.Installed, ModuleState.Uninstalled, ModuleState.Failed],
        [ModuleState.Disabling] = [ModuleState.Disabled, ModuleState.Failed],
        [ModuleState.Disabled] = [ModuleState.Loading, ModuleState.Uninstalled],
        [ModuleState.Updating] = [ModuleState.Running, ModuleState.Failed],
        [ModuleState.Failed] = [ModuleState.Loading, ModuleState.Quarantined, ModuleState.Uninstalled],
        [ModuleState.Quarantined] = [ModuleState.Verifying, ModuleState.Uninstalled],
        [ModuleState.Uninstalled] = [ModuleState.Discovered]
    };

    public static bool CanMove(ModuleState from, ModuleState to) =>
        from == to || (Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to));

    public static void Ensure(ModuleState from, ModuleState to)
    {
        if (!CanMove(from, to))
            throw new InvalidOperationException($"Illegal module state transition: {from} -> {to}.");
    }
}

public sealed record ModulePackage(string PackagePath, ModuleManifest Manifest, string EntryAssemblyPath);

/// <summary>只读包校验：路径防穿越、Manifest、契约版本、入口文件存在性、SHA-256。</summary>
public sealed class ModulePackageManager(
    IOptions<ModuleRuntimeOptions> options,
    IModuleAudit audit,
    ILogger<ModulePackageManager> logger)
{
    public async Task<ModulePackage> ReadAsync(string packageDirectory, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(options.Value.PackagesRoot);
        var full = Path.GetFullPath(packageDirectory);
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(full, root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Package path escapes the controlled modules root: {packageDirectory}");
        }

        var manifestPath = Path.Combine(full, ModulePackageLayout.ManifestFile);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("manifest.json not found.", manifestPath);

        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<ModuleManifest>(stream,
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken)
                       ?? throw new InvalidOperationException("manifest.json is empty.");

        if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidOperationException("manifest.json must declare id and version.");
        if (manifest.ContractVersion != PlatformContract.Version)
            throw new InvalidOperationException(
                $"Contract version mismatch: module={manifest.ContractVersion}, host={PlatformContract.Version}.");
        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly) || string.IsNullOrWhiteSpace(manifest.EntryType))
            throw new InvalidOperationException("manifest.json must declare entryAssembly and entryType.");
        if (Path.GetFileName(manifest.EntryAssembly) != manifest.EntryAssembly)
            throw new InvalidOperationException("entryAssembly must be a bare file name (path traversal guard).");

        var backendRoot = Path.GetFullPath(Path.Combine(full, ModulePackageLayout.BackendFolder));
        var entryPath = Path.GetFullPath(Path.Combine(backendRoot, manifest.EntryAssembly));
        if (!entryPath.StartsWith(backendRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("entryAssembly escapes the backend folder.");
        if (!File.Exists(entryPath))
            throw new FileNotFoundException("Entry assembly not found.", entryPath);

        if (options.Value.VerifyChecksum && !string.IsNullOrWhiteSpace(manifest.ChecksumSha256))
        {
            await using var entryStream = File.OpenRead(entryPath);
            var hash = await SHA256.HashDataAsync(entryStream, cancellationToken);
            var actual = Convert.ToHexString(hash);
            if (!string.Equals(actual, manifest.ChecksumSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Module entry assembly checksum mismatch.");
        }

        logger.LogInformation("Module package verified. Id={Id}, Version={Version}, Entry={Entry}",
            manifest.Id, manifest.Version, manifest.EntryAssembly);
        audit.Record(new ModuleAuditEntry(DateTimeOffset.UtcNow, manifest.Id, manifest.Version, "Verify", "OK", full));
        return new ModulePackage(full, manifest, entryPath);
    }
}

public sealed class ModuleDiscovery(
    IOptions<ModuleRuntimeOptions> options,
    ModulePackageManager packages,
    IModuleAudit audit,
    ILogger<ModuleDiscovery> logger)
{
    public async Task<IReadOnlyList<ModulePackage>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(options.Value.PackagesRoot);
        if (!Directory.Exists(root))
            return [];

        var found = new List<ModulePackage>();
        foreach (var manifestPath in Directory.EnumerateFiles(root, ModulePackageLayout.ManifestFile, SearchOption.AllDirectories))
        {
            try
            {
                found.Add(await packages.ReadAsync(Path.GetDirectoryName(manifestPath)!, cancellationToken));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Module package rejected: {Path}", manifestPath);
                audit.Record(new ModuleAuditEntry(DateTimeOffset.UtcNow, "-", "-", "Discover", "Quarantined", ex.Message));
            }
        }
        return found;
    }
}

public sealed class ModuleCatalogEntry
{
    public required ModuleDescriptor Descriptor { get; set; }
    public required ModuleManifest Manifest { get; set; }
}

/// <summary>加载图校验结果。缺失依赖与循环依赖是 nopCommerce 式加载顺序的两类硬错误。</summary>
public sealed class LoadGraphValidationResult
{
    public List<string> MissingDependencies { get; } = [];
    public List<List<string>> Cycles { get; } = [];
    public bool IsValid => MissingDependencies.Count == 0 && Cycles.Count == 0;
}

/// <summary>安装清单：只保存稳定 DTO，绝不保存模块私有 Type/Assembly。
/// 版本感知：同一模块 Id 可并存多个版本（id → version → entry）；对外暴露的「当前版本」为 ActiveVersion。</summary>
public sealed class ModuleCatalog(IModuleAudit audit)
{
    // id → (version → entry)。一个 Id 可挂多个版本；运行时同一 Id 仅加载一个 Active 版本。
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ModuleCatalogEntry>> _versions =
        new(StringComparer.OrdinalIgnoreCase);

    // id → 当前激活版本。由加载/上传显式设置；未设置时回退为最高语义版本。
    private readonly ConcurrentDictionary<string, string> _activeVersion = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ModulePackage package, ModuleState state = ModuleState.Discovered)
    {
        var manifest = package.Manifest;
        var id = manifest.Id;
        var version = manifest.Version;
        var perVersion = _versions.GetOrAdd(id, _ => new ConcurrentDictionary<string, ModuleCatalogEntry>(StringComparer.OrdinalIgnoreCase));
        // 模块声明的页面路由 = frontend.routes ∪ menuItems[].route；导航据此判断菜单归属模块。
        var routes = manifest.Frontend.Routes
            .Concat(manifest.Frontend.MenuItems.Select(mi => mi.Route))
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var descriptor = new ModuleDescriptor(
            manifest.Id, manifest.Name, manifest.Version, manifest.Publisher,
            state, package.PackagePath, null, null, manifest.Permissions, manifest.DisplayOrder, manifest.IsEssential, 1,
            manifest.DisplayName)
        {
            Routes = routes
        };
        perVersion[version] = new ModuleCatalogEntry { Descriptor = descriptor, Manifest = manifest };
        _activeVersion.TryAdd(id, version); // 首次发现时设为默认；显式激活（Load/上传）会覆盖。
        audit.Record(new ModuleAuditEntry(DateTimeOffset.UtcNow, id, version, "Install", "OK", package.PackagePath));
    }

    /// <summary>显式设置某模块 Id 的激活版本（加载或上传时调用）。</summary>
    public void SetActiveVersion(string id, string version)
    {
        if (!_versions.TryGetValue(id, out var perVersion) || !perVersion.ContainsKey(version))
            throw new KeyNotFoundException($"Module '{id}' version '{version}' is not registered.");
        _activeVersion[id] = version;
    }

    private string ActiveVersion(string id)
    {
        if (_activeVersion.TryGetValue(id, out var av) &&
            _versions.TryGetValue(id, out var perVersion) && perVersion.ContainsKey(av))
            return av;
        // 回退：该 Id 下的最高语义版本。
        return HighestVersion(id) ?? throw new KeyNotFoundException($"Module '{id}' is not installed.");
    }

    private string? HighestVersion(string id) =>
        _versions.TryGetValue(id, out var perVersion)
            ? perVersion.Keys.OrderByDescending(v => v, SemanticVersionComparer.Instance).FirstOrDefault()
            : null;

    private ModuleCatalogEntry ActiveEntry(string id) => _versions[id][ActiveVersion(id)];

    /// <summary>已发现模块的 manifest 快照（供安装向导读取模块声明的菜单等元数据），含全部版本。</summary>
    public IReadOnlyList<ModuleManifest> Manifests() =>
        _versions.Values.SelectMany(p => p.Values.Select(e => e.Manifest)).ToArray();

    public bool TryGet(string id, out ModuleCatalogEntry entry)
    {
        if (!_versions.TryGetValue(id, out var perVersion))
        {
            entry = null!;
            return false;
        }
        entry = ActiveEntry(id);
        entry.Descriptor = entry.Descriptor with { VersionCount = perVersion.Count };
        return true;
    }

    /// <summary>读取指定版本的快照（含 VersionCount）。</summary>
    public ModuleDescriptor GetVersionDescriptor(string id, string version)
    {
        if (!_versions.TryGetValue(id, out var perVersion) || !perVersion.TryGetValue(version, out var entry))
            throw new KeyNotFoundException($"Module '{id}' v{version} not found.");
        return entry.Descriptor with { VersionCount = perVersion.Count };
    }

    /// <summary>列出某模块 Id 的全部版本（语义版本降序）。</summary>
    public IReadOnlyList<ModuleCatalogEntry> ListVersions(string id)
    {
        if (!_versions.TryGetValue(id, out var perVersion))
            throw new KeyNotFoundException($"Module '{id}' is not installed.");
        return perVersion.Values
            .OrderByDescending(e => e.Manifest.Version, SemanticVersionComparer.Instance)
            .ToArray();
    }

    public bool HasVersion(string id, string version) =>
        _versions.TryGetValue(id, out var perVersion) && perVersion.ContainsKey(version);

    public IReadOnlyList<ModuleDescriptor> List()
    {
        var result = new List<ModuleDescriptor>();
        foreach (var id in _versions.Keys)
        {
            if (!_versions.TryGetValue(id, out var perVersion)) continue;
            var count = perVersion.Count;
            result.Add(ActiveEntry(id).Descriptor with { VersionCount = count });
        }
        result.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
        return result.ToArray();
    }

    /// <summary>
    /// 根据依赖图（Dependencies）与 DisplayOrder 解析出确定性的加载顺序（基于各 Id 的激活版本）。
    /// 任意一个模块都能在中间任意插入：只要它的 DisplayOrder 在相邻模块之间、且依赖已声明，
    /// 就不会影响其他模块的加载次序。
    /// </summary>
    public IReadOnlyList<ModuleDescriptor> ListOrdered() =>
        ResolveLoadOrder().Select(id => ActiveEntry(id).Descriptor with { VersionCount = _versions[id].Count }).ToArray();

    /// <summary>nopCommerce 风格的加载序列：拓扑排序（依赖优先）+ DisplayOrder 升序 + Id 兜底。</summary>
    public IReadOnlyList<string> ResolveLoadOrder()
    {
        var validation = ValidateLoadGraph();
        if (!validation.IsValid)
        {
            var parts = new List<string>();
            parts.AddRange(validation.MissingDependencies.Select(d => $"missing dependency [{d}]"));
            parts.AddRange(validation.Cycles.Select(c => "cycle [" + string.Join(" -> ", c) + "]"));
            throw new InvalidOperationException("Module load graph is invalid: " + string.Join("; ", parts));
        }

        var ids = _versions.Keys.ToList();
        var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var remainingDeps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            dependents[id] = [];
            remainingDeps[id] = 0;
        }
        foreach (var id in ids)
        {
            foreach (var dep in ActiveEntry(id).Manifest.Dependencies)
            {
                if (!_versions.ContainsKey(dep))
                    continue; // 缺失依赖已在 ValidateLoadGraph 报告
                dependents[dep].Add(id);
                remainingDeps[id]++;
            }
        }

        // 就绪集合按 (DisplayOrder, Id) 排序，保证确定性且数值小者先加载。
        var ready = new SortedSet<string>(Comparer<string>.Create((a, b) =>
        {
            var da = ActiveEntry(a).Manifest.DisplayOrder;
            var db = ActiveEntry(b).Manifest.DisplayOrder;
            return da != db
                ? da.CompareTo(db)
                : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }));
        foreach (var id in ids)
            if (remainingDeps[id] == 0)
                ready.Add(id);

        var order = new List<string>();
        while (ready.Count > 0)
        {
            var current = ready.Min!;
            ready.Remove(current);
            order.Add(current);
            foreach (var dependent in dependents[current])
                if (--remainingDeps[dependent] == 0)
                    ready.Add(dependent);
        }

        if (order.Count != ids.Count)
            throw new InvalidOperationException("Module load graph contains a cycle (topological sort incomplete).");
        return order;
    }

    /// <summary>校验加载图：缺失依赖 + 循环依赖。不抛异常，返回结构化结果供 API 上报。</summary>
    public LoadGraphValidationResult ValidateLoadGraph()
    {
        var result = new LoadGraphValidationResult();
        var ids = _versions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var perVersion in _versions.Values)
            foreach (var entry in perVersion.Values)
                foreach (var dep in entry.Manifest.Dependencies)
                    if (!ids.Contains(dep))
                        result.MissingDependencies.Add($"{entry.Descriptor.Id} -> {dep}");

        var visited = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 0=visiting, 1=done
        foreach (var id in ids)
            if (!visited.ContainsKey(id))
                DetectCycle(id, visited, [], result.Cycles);
        return result;
    }

    private void DetectCycle(string start, Dictionary<string, int> visited, Stack<string> path, List<List<string>> cycles)
    {
        if (path.Contains(start))
        {
            var idx = path.ToList().IndexOf(start);
            cycles.Add(path.Skip(idx).Concat([start]).ToList());
            return;
        }
        if (visited.TryGetValue(start, out var st) && st == 1)
            return;
        if (!_versions.ContainsKey(start)) // 缺失依赖，已在别处报告
            return;

        visited[start] = 0;
        path.Push(start);
        foreach (var dep in ActiveEntry(start).Manifest.Dependencies)
            if (_versions.ContainsKey(dep))
                DetectCycle(dep, visited, path, cycles);
        path.Pop();
        visited[start] = 1;
    }

    /// <summary>version 为空时作用于激活版本；否则作用于指定版本条目（多版本共存时安装/上传走指定版本）。</summary>
    public void SetState(string id, ModuleState target, string? error = null, string? version = null)
    {
        ModuleCatalogEntry entry;
        if (!string.IsNullOrWhiteSpace(version) &&
            _versions.TryGetValue(id, out var perVersion) &&
            perVersion.TryGetValue(version, out var versioned))
        {
            entry = versioned;
        }
        else
        {
            entry = ActiveEntry(id);
        }
        ModuleStateMachine.Ensure(entry.Descriptor.State, target);
        entry.Descriptor = entry.Descriptor with
        {
            State = target,
            Error = error,
            LoadedAtUtc = target == ModuleState.Running ? DateTimeOffset.UtcNow : entry.Descriptor.LoadedAtUtc
        };
        audit.Record(new ModuleAuditEntry(DateTimeOffset.UtcNow, id, entry.Descriptor.Version,
            target.ToString(), error is null ? "OK" : "Failed", error));
    }

    public void Remove(string id)
    {
        _versions.TryRemove(id, out _);
        _activeVersion.TryRemove(id, out _);
    }
}

/// <summary>语义化版本比较（支持 1~4 段数字，如 1 / 1.2 / 1.2.3 / 1.2.3.4；段不足补 0）。</summary>
internal sealed class SemanticVersionComparer : IComparer<string>
{
    public static readonly SemanticVersionComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        var a = Parse(x);
        var b = Parse(y);
        for (var i = 0; i < 4; i++)
        {
            var c = a[i].CompareTo(b[i]);
            if (c != 0) return c;
        }
        // 同数值时按原始串字典序兜底（区分预发布标签等）。
        return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }

    private static int[] Parse(string? v)
    {
        var parts = new int[4];
        if (!string.IsNullOrWhiteSpace(v))
        {
            var segs = v.Split('.', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < Math.Min(segs.Length, 4); i++)
                int.TryParse(segs[i].Trim(), out parts[i]);
        }
        return parts;
    }
}
