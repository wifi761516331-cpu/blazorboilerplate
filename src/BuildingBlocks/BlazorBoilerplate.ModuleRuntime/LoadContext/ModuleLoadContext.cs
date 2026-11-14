using System.Reflection;
using System.Runtime.Loader;

namespace BlazorBoilerplate.ModuleRuntime.LoadContext;

/// <summary>
/// 每个模块版本的独立、可回收 AssemblyLoadContext。
/// 关键：平台共享契约与框架程序集必须回落到默认上下文，
/// 否则 Host 与模块拿到的是两个不同类型身份的 IModule / Manifest。
/// </summary>
public sealed class ModuleLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.Ordinal)
    {
        "BlazorBoilerplate.ModuleAbstractions",
        "BlazorBoilerplate.Application",
        "BlazorBoilerplate.Domain",
        "BlazorBoilerplate.Infrastructure",
        "BlazorBoilerplate.ModuleRuntime",
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

    public ModuleLoadContext(string moduleId, string version, string mainAssemblyPath)
        : base($"module:{moduleId}:{version}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (string.IsNullOrEmpty(name))
            return null;

        if (SharedAssemblies.Contains(name) ||
            name is "System" or "netstandard" or "mscorlib" ||
            name.StartsWith("System.", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.", StringComparison.Ordinal) ||
            name.StartsWith("Serilog", StringComparison.Ordinal))
        {
            // 显式按简单名加载进默认上下文（Assembly.Load 绑定 Default ALC），而非 return null 回落：
            // 打包注入 -p:Version 会沿构建图给共享契约盖章（如 ModuleAbstractions 1.0.1.0），
            // 回落默认上下文按完整程序集身份绑定会因版本不匹配 FileNotFound。
            // 简单名绑定忽略版本，保证宿主与模块始终共用同一份类型实例。
            return Assembly.Load(new AssemblyName(name));
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
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
