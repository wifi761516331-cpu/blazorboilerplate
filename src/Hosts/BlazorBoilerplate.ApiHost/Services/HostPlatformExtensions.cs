using System.Threading.RateLimiting;
using BlazorBoilerplate.ApiHost.Auth;
using BlazorBoilerplate.ApiHost.HealthChecks;
using BlazorBoilerplate.ApiHost.Setup;
using BlazorBoilerplate.Application.DataExchange;
using BlazorBoilerplate.Application.Platform;
using BlazorBoilerplate.Application.Rules;
using BlazorBoilerplate.Application.Search;
using BlazorBoilerplate.Infrastructure;
using BlazorBoilerplate.Infrastructure.Caching;
using BlazorBoilerplate.Infrastructure.DataExchange;
using BlazorBoilerplate.Infrastructure.Email;
using BlazorBoilerplate.Infrastructure.Platform;
using BlazorBoilerplate.Infrastructure.Rules;
using BlazorBoilerplate.Infrastructure.Search;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.ModuleRuntime.Runtime;
using Microsoft.AspNetCore.RateLimiting;
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
        services.AddBlazorBoilerplateCaching(configuration, contentRootPath);
        services.AddBlazorBoilerplateEventBus();
        services.AddBlazorBoilerplateEmail(configuration);
        services.AddBlazorBoilerplateAttachments(configuration, contentRootPath);
        services.AddBlazorBoilerplateModuleStorage(configuration);

        var dataDir = Path.Combine(contentRootPath, "data");
        services.AddSingleton<ISettingService>(_ =>
            new Persistence.Platform.EfSettingService(new JsonSettingService(dataDir)));
        services.AddSingleton<IActivityLogService>(_ =>
            new Persistence.Platform.EfActivityLogService(new JsonActivityLogService(dataDir)));
        services.AddSingleton<IMessageTemplateService>(_ =>
            new Persistence.Platform.EfMessageTemplateService(new JsonMessageTemplateService(dataDir)));
        services.AddSingleton<IQueuedEmailService>(sp =>
        {
            var sender = sp.GetRequiredService<Application.Abstractions.IEmailSender>();
            var jsonFallback = new JsonQueuedEmailService(
                dataDir,
                sender,
                sp.GetRequiredService<ILogger<JsonQueuedEmailService>>());
            return new Persistence.Platform.EfQueuedEmailService(
                sender,
                sp.GetRequiredService<ILogger<Persistence.Platform.EfQueuedEmailService>>(),
                jsonFallback);
        });
        services.AddSingleton<IScheduleTaskManager, ModuleScheduleTaskManager>();
        services.AddSingleton<IProviderManager>(_ => new ModuleProviderManager(dataDir));
        services.AddSingleton<ModularAssetManager>();
        services.AddSingleton<IRuleEngine, PlatformRuleEngine>();
        services.AddSingleton<IDataExchangeService, PlatformDataExchangeService>();
        services.AddSingleton<ISearchService, InMemorySearchService>();
        services.AddSingleton<IDistributedLockService, RedisDistributedLockService>();
        services.AddScoped<IWorkContext, HttpWorkContext>();
        services.AddHostedService<ScheduleTaskBackgroundService>();
        services.AddHostedService<QueuedEmailSchedulerRegistration>();

        services.AddSingleton<SetupStateProvider>();
        services.AddSingleton<ISetupInitializer, DefaultSetupInitializer>();
        services.AddSingleton<SystemSettingsStore>();
        services.AddSingleton<EmailVerificationCodeStore>();
        services.AddScoped<AccountRegistrationService>();
        services.AddSingleton<ISmtpSettingsProvider, SystemSettingsSmtpProvider>();
        services.AddSingleton<BlazorBoilerplate.Infrastructure.Attachments.IAttachmentStorage, DynamicAttachmentStorage>();

        services.AddProblemDetails();
        services.AddHealthChecks()
            .AddCheck<SmtpHealthCheck>("smtp", failureStatus: HealthStatus.Degraded)
            .AddCheck<RedisCacheHealthCheck>("redis", failureStatus: HealthStatus.Degraded);
        services.AddOpenApi();

        services.AddCors(options => options.AddPolicy("web", policy =>
            policy.WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
                .AllowAnyHeader()
                .AllowAnyMethod()));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var ip = context.Connection.RemoteIpAddress;
                if (ip is not null && System.Net.IPAddress.IsLoopback(ip))
                {
                    return RateLimitPartition.GetNoLimiter("loopback");
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    ip?.ToString() ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 200,
                        Window = TimeSpan.FromSeconds(10),
                        QueueLimit = 0
                    });
            });
            options.AddPolicy("auth-sensitive", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        return services;
    }

    private sealed class QueuedEmailSchedulerRegistration(
        IScheduleTaskManager manager,
        IQueuedEmailService emailQueue) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            manager.Register("platform", "platform:queued-email", new QueuedEmailTask(emailQueue));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class QueuedEmailTask(IQueuedEmailService emailQueue) : IScheduleTask
    {
        public string Name => "邮件队列发送";
        public int Seconds => 30;
        public Task ExecuteAsync(CancellationToken cancellationToken) =>
            emailQueue.ProcessPendingAsync(maxBatch: 20, maxTries: 3, cancellationToken);
    }
}

