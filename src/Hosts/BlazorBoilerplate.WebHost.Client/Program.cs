using Bit.BlazorUI;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using BlazorBoilerplate.WebHost.Client.Services;
using BlazorBoilerplate.WebHost.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddBitBlazorUIServices(true);
builder.Services.AddScoped<ElevatedAccessContext>();
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    // AI 生成官网等环节可能超过默认 100s；服务端 BFF 同样放宽到 35 分钟。
    Timeout = TimeSpan.FromMinutes(35)
});
builder.Services.AddScoped<BlazorBoilerplateApiClient>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<FrontendLocalizationService>();
builder.Services.AddScoped<ILocalizationService>(sp => sp.GetRequiredService<FrontendLocalizationService>());
builder.Services.AddScoped<AccountState>();
builder.Services.AddScoped<ICurrentUserPermissionService>(sp => sp.GetRequiredService<AccountState>());
builder.Services.AddScoped<SystemSettingsState>();
builder.Services.AddScoped<UserPreferences>();
builder.Services.AddScoped<NavRefreshService>();
builder.Services.AddScoped<INavRefreshService>(sp => sp.GetRequiredService<NavRefreshService>());

using var bootstrapHttp = new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
};
var catalog = new ClientModuleCatalog(bootstrapHttp);
await catalog.PreloadAutoModulesAsync(builder.Services);
builder.Services.AddSingleton(catalog);
builder.Services.AddScoped<IModuleCatalogControl, ClientModuleCatalogControl>();

await builder.Build().RunAsync();
