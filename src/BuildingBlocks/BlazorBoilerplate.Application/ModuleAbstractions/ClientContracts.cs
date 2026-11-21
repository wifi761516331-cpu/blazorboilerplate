using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.ModuleAbstractions;

/// <summary>前端模块服务注册契约（由 WebHost 加载模块前端程序集时按 Order 升序调用）。</summary>
public interface IModuleClientStartup
{
    /// <summary>向前端 Blazor DI 容器注册本模块服务。</summary>
    void ConfigureServices(IServiceCollection services);

    /// <summary>启动顺序（默认 3000）。</summary>
    int Order => ModuleStartupDefaults.DefaultOrder;
}

/// <summary>前端 Scoped 服务标记接口（自动扫描注册为 Scoped）。</summary>
public interface IClientService;

/// <summary>前端 Transient 服务标记接口（自动扫描注册为 Transient）。</summary>
public interface ITransientClientService;

/// <summary>组件按需静态资源类型。</summary>
public enum ModuleResourceType
{
    Stylesheet,
    Script
}

/// <summary>组件按需加载的静态资源描述。</summary>
public sealed record ModuleResource(ModuleResourceType Type, string Url);

/// <summary>在 Razor 组件类上声明按需加载的 CSS 或 JS 资源（仅组件渲染时注入页面）。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ModuleResourceAttribute(ModuleResourceType type, string url) : Attribute
{
    public ModuleResourceType Type { get; } = type;
    public string Url { get; } = url;
}

/// <summary>由组件实例动态提供按需加载的静态资源列表。</summary>
public interface IModuleResourceProvider
{
    IReadOnlyList<ModuleResource> Resources { get; }
}

