using System.Reflection;
using System.Runtime.Loader;

namespace BlazorBoilerplate.ModuleRuntime.LoadContext;

/// <summary>
/// 每个模块版本的独立、可回收 AssemblyLoadContext。
/// 解析顺序（通用规则，宿主无需感知模块用了哪些第三方包）：
///   1) 显式共享白名单（平台契约 / 宿主已静态引用且必须类型同一的程序集）→ 默认 ALC；
///   2) 宿主/共享框架已有、且版本不低于模块显式引用版本的程序集 → 默认 ALC（类型身份必须相同）；
///   3) 其余模块包自带的私有依赖（deps.json 解析 + 同目录直接探测；含 Microsoft.Extensions.AI / OpenAI）→ 模块 ALC；
///   4) 包里也没有的框架程序集（System.* / Microsoft.* / Serilog）→ 默认 ALC。
/// 关键：平台共享契约必须回落到默认上下文，否则 Host 与模块拿到两个类型身份的 IModule / Manifest。
/// </summary>
public sealed class ModuleLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _moduleDirectory;

    // 宿主默认 ALC 可解析到的程序集版本（含共享框架与宿主静态依赖）。
    // 用于避免"包内副本与宿主同版本"时误加载出第二份类型身份——
    // 典型症状：TypeLoadException: Method 'ConfigureServices' ... does not have an implementation
    // （IServiceCollection / DbContext 等参数类型在模块 ALC 与默认 ALC 各一份）。
    private static readonly IReadOnlyDictionary<string, Version> DefaultAssemblyVersions =
        BuildDefaultAssemblyVersions();

    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.Ordinal)
    {
        "BlazorBoilerplate.ModuleAbstractions",
        "BlazorBoilerplate.Application",
        "BlazorBoilerplate.Domain",
        "BlazorBoilerplate.Infrastructure",
        "BlazorBoilerplate.ModuleRuntime",
        "BlazorBoilerplate.Persistence",
        // 前端共享 RCL：AppIcon / IdentityLayout / BlazorBoilerplateApiClient 等。
        // 模块 RCL 把前端页面与后端同体打包，故动态加载的模块程序集编译期引用它；
        // 加入共享白名单可回落默认上下文，避免 ALC 再加载一份或解析失败。
        "BlazorBoilerplate.Web.Shared",
        // Host 已静态引用的模块级共享依赖：若让模块 ALC 再加载一份，
        // 会出现 EF Provider / Bit 组件的类型身份冲突（宿主与模块各一份）。
        "Npgsql",
        "Npgsql.EntityFrameworkCore.PostgreSQL",
        "Bit.BlazorUI",
        "Bit.BlazorUI.Extras",
        "Bit.BlazorUI.Icons"
    };

    private static IReadOnlyDictionary<string, Version> BuildDefaultAssemblyVersions()
    {
        var result = new Dictionary<string, Version>(StringComparer.Ordinal);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not string tpa)
            return result;

        foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var name = AssemblyName.GetAssemblyName(path);
                if (string.IsNullOrEmpty(name.Name))
                    continue;
                var assemblyVersion = name.Version ?? new Version(0, 0, 0, 0);
                if (!result.TryGetValue(name.Name, out var existing) || assemblyVersion > existing)
                    result[name.Name] = assemblyVersion;
            }
            catch (BadImageFormatException)
            {
                // 原生 DLL / 非托管资源，跳过。
            }
            catch (FileNotFoundException)
            {
                // TPA 中已不存在的条目，跳过。
            }
        }
        return result;
    }

    public ModuleLoadContext(string moduleId, string version, string mainAssemblyPath)
        : base($"module:{moduleId}:{version}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        _moduleDirectory = Path.GetDirectoryName(Path.GetFullPath(mainAssemblyPath))!;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (string.IsNullOrEmpty(name))
            return null;

        // 1) 显式共享契约：必须按简单名绑定默认 ALC，保证 Host 与模块类型身份一致。
        //    打包注入 -p:Version 会沿构建图给共享契约盖章（如 ModuleAbstractions 1.0.1.0），
        //    按完整身份回落会因版本不匹配 FileNotFound；简单名绑定忽略版本。
        if (SharedAssemblies.Contains(name) || name is "System" or "netstandard" or "mscorlib")
            return Assembly.Load(new AssemblyName(name));

        // 2) 宿主 / 共享框架已提供同版本（或更高版本）的程序集：必须回落默认 ALC。
        //    否则包内自带的一份会形成第二份类型身份，导致 IServiceCollection / DbContext
        //    等参数类型不一致，表现为 "Method 'ConfigureServices' ... does not have an implementation"。
        //    反过来：模块版本更高（如 System.ClientModel 宿主 1.8 / 模块 1.15）时不劫持，
        //    继续走第 3 步在模块 ALC 加载模块私有副本。
        if (assemblyName.Version is not null &&
            DefaultAssemblyVersions.TryGetValue(name, out var hostVersion) &&
            hostVersion >= assemblyName.Version)
        {
            try
            {
                return Assembly.Load(new AssemblyName(name));
            }
            catch (FileNotFoundException)
            {
                // TPA 声明但当前无法加载，继续按包内依赖解析。
            }
            catch (FileLoadException)
            {
                // 同上，避免把可恢复的加载问题升级成模块加载失败。
            }
        }

        // 3) 模块私有依赖：包里携带且宿主没有同版本副本的 DLL，在模块 ALC 内加载。
        //    不能先用 Microsoft.* / System.* 前缀拦截，否则模块自带的 Microsoft.Extensions.AI、
        //    System.ClientModel（模块 1.15 > 宿主 1.8）会被错误绑定到宿主默认 ALC。
        //    deps.json 缺失/过期时，再按同目录简单名直接探测一次：依赖 dll 就在 backend/ 下，
        //    不能因为清单没写就误落到默认 ALC。这是通用规则，不按具体包名硬编码。
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            var localPath = Path.Combine(_moduleDirectory, name + ".dll");
            if (File.Exists(localPath))
                path = localPath;
        }
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            return LoadAssemblyNoLock(path);

        // 4) 包里没有的框架 / 宿主程序集才回落默认 ALC。
        if (name.StartsWith("System.", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
            name.StartsWith("Serilog", StringComparison.Ordinal))
            return Assembly.Load(new AssemblyName(name));

        return null;
    }

    /// <summary>
    /// 从内存流加载程序集（含可选 .pdb），避免操作系统对磁盘 DLL 持有文件句柄锁，
    /// 确保模块停止、切换版本或卸载后可立即删除磁盘目录与 DLL。
    /// </summary>
    public Assembly LoadAssemblyNoLock(string assemblyPath)
    {
        using var dllStream = new MemoryStream(File.ReadAllBytes(assemblyPath));
        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        if (File.Exists(pdbPath))
        {
            using var pdbStream = new MemoryStream(File.ReadAllBytes(pdbPath));
            return LoadFromStream(dllStream, pdbStream);
        }
        return LoadFromStream(dllStream);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}

public sealed class ModuleLoadContextFactory
{
    public ModuleLoadContext Create(string moduleId, string version, string mainAssemblyPath)
    {
        if (!File.Exists(mainAssemblyPath))
            throw new FileNotFoundException("Module entry assembly not found.", mainAssemblyPath);
        return new ModuleLoadContext(moduleId, version, mainAssemblyPath);
    }
}
