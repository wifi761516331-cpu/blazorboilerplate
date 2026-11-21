using BlazorBoilerplate.ApiHost.Auth;
using BlazorBoilerplate.ApiHost.Localization;
using BlazorBoilerplate.ApiHost.Modules;
using BlazorBoilerplate.ApiHost.Services;
using BlazorBoilerplate.ApiHost.Setup;
using BlazorBoilerplate.ModuleRuntime;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext());

builder.Services
    .AddApiHostPlatform(builder.Configuration, builder.Environment.ContentRootPath)
    .AddAppAuthentication()
    .AddApiHostModules(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddSignalR();

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseHttpLogging();
app.UseCors("web");
app.UseRateLimiter();
app.UseSetupBootstrapGate();
app.UseTenantResolution();
app.UsePlatformRequestLocalization();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseEndpointPermissionGuard();
app.UseModuleEndpointDataSource();

app.MapHealthChecks("/health");
app.MapOpenApi("/openapi/v1.json");
app.MapSetupEndpoints();
app.MapModuleManagementEndpoints();
app.MapControllers();
app.MapHub<BlazorBoilerplate.ApiHost.Hubs.AppHub>(BlazorBoilerplate.ApiHost.Hubs.AppHub.HubPath);

await app.InitializeModulesOnStartupAsync();

app.Run();

/// <summary>供 WebApplicationFactory 测试使用。</summary>
public partial class Program;
