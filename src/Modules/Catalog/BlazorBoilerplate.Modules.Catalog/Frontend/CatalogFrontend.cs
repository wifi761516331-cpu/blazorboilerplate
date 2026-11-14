using System.Runtime.CompilerServices;
using BlazorBoilerplate.ModuleAbstractions;

namespace BlazorBoilerplate.Modules.Catalog.Frontend;

/// <summary>模块前端自注册：程序集加载时把本模块的菜单条目登记到宿主的注册表。</summary>
internal static class CatalogFrontendRegistration
{
#pragma warning disable CA2255 // ModuleInitializer 在宿主（WebHost）加载本模块程序集时触发自注册
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Register()
    {
        ModuleFrontendRegistry.Register(new ModuleFrontendMenu("商品目录", "/catalog", "catalog"));
    }
}
