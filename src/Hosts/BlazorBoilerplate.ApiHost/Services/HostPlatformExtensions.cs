using System.Threading.RateLimiting;
using BlazorBoilerplate.ApiHost.Auth;
using BlazorBoilerplate.ApiHost.HealthChecks;
using BlazorBoilerplate.ApiHost.Setup;
using BlazorBoilerplate.Infrastructure;
using BlazorBoilerplate.Infrastructure.Email;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BlazorBoilerplate.ApiHost.Services;

/// <summary>ApiHost 平台基础能力、HTTP 管线基础设施（CORS、限流、健康检查、OpenAPI）注册扩展。</summary>
public static class HostPlatformExtensions
{
    public static IServiceCollection AddApiHostPlatform(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        services.AddHttpLogging(_ => { });
        services.AddControllers(options => options.Filters.Add<ApiExceptionFilter>());

        services.Configure<SetupOptions>(configuration.GetSection(SetupOptions.SectionName));
        services.AddBlazorBoilerplateInfrastructurePlaceholders();
        services.AddBlazorBoilerplateEmail(configuration);
        services.AddBlazorBoilerplateAttachments(configuration, contentRootPath);
        services.AddBlazorBoilerplateModuleStorage(configuration);

        services.AddSingleton<SetupStateProvider>();
        services.AddSingleton<ISetupInitializer, DefaultSetupInitializer>();
        services.AddSingleton<SystemSettingsStore>();
        services.AddSingleton<ISmtpSettingsProvider, SystemSettingsSmtpProvider>();
        services.AddSingleton<BlazorBoilerplate.Infrastructure.Attachments.IAttachmentStorage, DynamicAttachmentStorage>();

        services.AddProblemDetails();
        services.AddHealthChecks()
            .AddCheck<SmtpHealthCheck>("smtp", failureStatus: HealthStatus.Degraded);
        services.AddOpenApi();

        services.AddCors(options => options.AddPolicy("web", policy =>
            policy.WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
                .AllowAnyHeader()
                .AllowAnyMethod()));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 200,
                        Window = TimeSpan.FromSeconds(10),
                        QueueLimit = 0
                    }));
        });

        return services;
    }
}

