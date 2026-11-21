using BlazorBoilerplate.ModuleAbstractions;

namespace BlazorBoilerplate.Modules.Demo;

/// <summary>
/// 强类型模块配置示例（实现 <see cref="ISettings"/>）。
/// 支持通过 <see cref="ISettingService"/> 读写，并可在运维中心「模块配置」页面动态修改。
/// </summary>
public sealed class DemoSettings : ISettings
{
    public string GreetingPrefix { get; set; } = "Hello from DemoSettings";
    public bool EnableAutoAudit { get; set; } = true;
    public int MaxBatchSize { get; set; } = 20;
}

