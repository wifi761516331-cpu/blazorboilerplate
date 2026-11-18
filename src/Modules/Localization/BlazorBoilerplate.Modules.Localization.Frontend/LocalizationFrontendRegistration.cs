using System.Runtime.CompilerServices;
using BlazorBoilerplate.ModuleAbstractions;

namespace BlazorBoilerplate.Modules.Localization.Frontend;

/// <summary>模块前端自注册：程序集加载时把本模块的菜单条目登记到宿主的注册表。</summary>
internal static class LocalizationFrontendRegistration
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Register()
    {
        ModuleFrontendRegistry.Register(new ModuleFrontendMenu("多语言管理", "/localization", "localization"));
    }
}

