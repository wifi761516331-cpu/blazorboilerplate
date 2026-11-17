using System.Collections.Concurrent;
using System.Text.Json;
using BlazorBoilerplate.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;
using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Application.Attachments;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Domain.Events;
using BlazorBoilerplate.Infrastructure.Attachments;
using BlazorBoilerplate.Infrastructure.Events;
using BlazorBoilerplate.Infrastructure.Email;
using BlazorBoilerplate.Infrastructure.Localization;
using BlazorBoilerplate.Infrastructure.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.Infrastructure;

/// <summary>
/// 第一批占位实现：让 Host 在没有任何外部基础设施时也能启动（Setup Wizard 要求）。
/// 这些实现【不是】生产实现；PostgreSQL / Redis / MinIO / SMTP / MassTransit / Hangfire
/// 真实接入见 docs/ROADMAP.md 第二批。配置节只做校验，不建立连接。
/// </summary>
public sealed class InfrastructureOptions
{
    public const string SectionName = "Infrastructure";
    public string PostgreSQL { get; set; } = string.Empty;
    public string Redis { get; set; } = string.Empty;
    public string MinIO { get; set; } = string.Empty;
    public string Smtp { get; set; } = string.Empty;
    public bool StrictMode { get; set; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class PlaceholderCacheService : ICacheService
{
    private sealed record Entry(object? Value, string? Tag);
    private readonly ConcurrentDictionary<string, Entry> _store = new(StringComparer.Ordinal);

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_store.TryGetValue(key, out var entry) && entry.Value is T typed ? typed : default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl, string? tag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store[key] = new Entry(value, tag);
        return Task.CompletedTask;
    }

    public Task RemoveByTagAsync(string tag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var pair in _store.Where(p => string.Equals(p.Value.Tag, tag, StringComparison.Ordinal)).ToArray())
            _store.TryRemove(pair.Key, out _);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public async Task<T?> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory,
        CacheEntryOptions? options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_store.TryGetValue(key, out var hit) && hit.Value is T typed)
            return typed;
        var value = await factory(cancellationToken).ConfigureAwait(false);
        if (value is not null)
            _store[key] = new Entry(value, options?.Tag);
        return value;
    }
}

public sealed class PlaceholderFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _store = new(StringComparer.Ordinal);

    public Task<Stream?> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_store.TryGetValue(key, out var bytes) ? (Stream?)new MemoryStream(bytes, writable: false) : null);
    }

    public async Task PutAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        _store[key] = buffer.ToArray();
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

public sealed class PlaceholderEmailSender(ILogger<PlaceholderEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogWarning("SMTP 尚未接入（第二批）；邮件未实际发送。To={To}, Subject={Subject}", to, subject);
        return Task.CompletedTask;
    }
}

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddBlazorBoilerplateInfrastructurePlaceholders(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICacheService, PlaceholderCacheService>();
        services.AddSingleton<IFileStorage, PlaceholderFileStorage>();
        services.AddSingleton<IEmailSender, PlaceholderEmailSender>();
        services.AddSingleton<IUseCaseDispatcher, UseCaseDispatcher>();
        services.AddBlazorBoilerplateLocalization();
        return services;
    }

    public static IServiceCollection AddBlazorBoilerplateLocalization(this IServiceCollection services)
    {
        services.AddSingleton<ILocalizationRegistry>(_ =>
        {
            var registry = new InMemoryLocalizationRegistry();
            PlatformBuiltInResources.Register(registry);
            return registry;
        });
        services.AddScoped<ILocalizationService, PlatformLocalizationService>();
        return services;
    }

    /// <summary>
    /// 仅注册进程内内存缓存（兼容旧调用方）。生产宿主使用 AddBlazorBoilerplateCaching。
    /// </summary>
    public static IServiceCollection AddBlazorBoilerplateMemoryCache(this IServiceCollection services)
    {
        RemovePlaceholderCache(services);
        services.AddMemoryCache(options => options.ExpirationScanFrequency = TimeSpan.FromMinutes(1));
        services.AddSingleton<ICacheKeyBuilder, CacheKeyBuilder>();
        services.AddSingleton<KeyedLockPool>();
        services.AddSingleton<MemoryCacheService>();
        services.AddSingleton<ICacheService>(sp => sp.GetRequiredService<MemoryCacheService>());
        return services;
    }

    /// <summary>
    /// 注册 L1 内存缓存 + L2 Redis。Redis 未配置时 L2 no-op；安装向导在运行时写入 Redis 配置后自动生效。
    /// </summary>
    public static IServiceCollection AddBlazorBoilerplateCaching(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        RedisCacheState.Configure(ResolveRedisConnectionString(configuration, contentRootPath));

        RemovePlaceholderCache(services);
        services.AddMemoryCache(options => options.ExpirationScanFrequency = TimeSpan.FromMinutes(1));
        services.AddSingleton<ICacheKeyBuilder, CacheKeyBuilder>();
        services.AddSingleton<KeyedLockPool>();
        services.AddSingleton<MemoryCacheService>();
        services.AddSingleton<RedisConnectionProvider>();
        services.AddSingleton<IRedisCacheStore, RedisCacheStore>();
        services.AddSingleton<HybridCacheService>();
        services.AddSingleton<ICacheService>(sp => sp.GetRequiredService<HybridCacheService>());
        services.AddHostedService<RedisCacheInvalidationSubscriber>();
        services.AddHostedService<RedisCacheTagIndexCleaner>();
        return services;
    }

    private static void RemovePlaceholderCache(IServiceCollection services)
    {
        var placeholder = services.FirstOrDefault(d =>
            d.ServiceType == typeof(ICacheService) && d.ImplementationType == typeof(PlaceholderCacheService));
        if (placeholder is not null)
            services.Remove(placeholder);
    }

    private static string? ResolveRedisConnectionString(IConfiguration configuration, string contentRootPath)
    {
        // 安装向导写出的设置优先：用户在向导里显式留空 = 禁用 L2，不能被 appsettings 默认值覆盖。
        var settingsPath = Path.Combine(contentRootPath, "data", "blazorboilerplate.settings.json");
        if (File.Exists(settingsPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
                var root = document.RootElement;
                if (root.TryGetProperty("infrastructure", out var infrastructure)
                    && infrastructure.ValueKind == JsonValueKind.Object
                    && infrastructure.TryGetProperty("redis", out var redis))
                {
                    var persisted = redis.ValueKind == JsonValueKind.String ? redis.GetString() : null;
                    return string.IsNullOrWhiteSpace(persisted) ? null : persisted!.Trim();
                }
            }
            catch (JsonException)
            {
                // 配置文件损坏时继续走 appsettings / 默认值。
            }
            catch (IOException)
            {
                // 文件被占用时继续走 appsettings / 默认值。
            }
        }

        var configured = configuration["Infrastructure:Redis"];
        if (!string.IsNullOrWhiteSpace(configured)
            && !string.Equals(configured.Trim(), "not-configured", StringComparison.OrdinalIgnoreCase))
            return configured.Trim();

        return RedisCacheState.DefaultConnectionString;
    }

    /// <summary>
    /// 接入真实 S3 / MinIO 模块存储。读取 ModuleStorage 配置：
    /// 启用时注册 <see cref="S3FileStorage"/>（实现 IFileStorage）并启动桶初始化；
    /// 禁用时回退到内存占位，保证离线/测试环境仍可启动。
    /// </summary>
    public static IServiceCollection AddBlazorBoilerplateModuleStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(S3StorageOptions.SectionName).Get<S3StorageOptions>() ?? new S3StorageOptions();

        // 移除占位 IFileStorage（若存在），改挂 S3 实现。
        var placeholder = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IFileStorage) && d.ImplementationType == typeof(PlaceholderFileStorage));
        if (placeholder is not null)
            services.Remove(placeholder);

        if (options.Enabled)
        {
            services.AddSingleton<S3FileStorage>(sp =>
                new S3FileStorage(options, sp.GetRequiredService<ILogger<S3FileStorage>>()));
            services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<S3FileStorage>());
            services.AddHostedService<S3BucketInitializer>();
        }
        else
        {
            services.AddSingleton<IFileStorage, PlaceholderFileStorage>();
        }

        return services;
    }

    /// <summary>
    /// 注册事件总线：进程内强类型 <see cref="IEventPublisher"/>（EventBus）+ 字符串频道 <see cref="IChannelBus"/>。
    /// 强类型契约位于 Domain 共享程序集，宿主与模块 ALC 共用类型身份。
    /// ChannelBus 默认纯进程内收发（不依赖外部组件），配置 Redis 时首次使用惰性建立跨节点订阅。
    /// </summary>
    public static IServiceCollection AddBlazorBoilerplateEventBus(this IServiceCollection services)
    {
        services.AddSingleton<EventBus>();
        services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<EventBus>());
        services.AddSingleton<ChannelBus>();
        services.AddSingleton<IChannelBus>(sp => sp.GetRequiredService<ChannelBus>());
        return services;
    }

    /// <summary>
    /// 接入真实 SMTP 邮件发送服务（读取 ConnectionStrings:smtp / Infrastructure:Smtp / Email 配置节）。
    /// 始终替换占位 IEmailSender 为 <see cref="SmtpEmailSender"/>；若未配置，发送时抛出明确异常或由调用方感知。
    /// </summary>
    public static IServiceCollection AddBlazorBoilerplateEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISmtpSettingsProvider>(_ => new ConfigurationSmtpSettingsProvider(configuration));

        var placeholder = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IEmailSender) && d.ImplementationType == typeof(PlaceholderEmailSender));
        if (placeholder is not null)
            services.Remove(placeholder);

        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }

    /// <summary>
    /// 注册附件存储服务（参考 Bluebird Attachment 机制）：
    /// 默认在未配置 MinIO 时使用本地磁盘（data/attachments），配置 MinIO 且 StorageMode=S3/Auto 时走 S3FileStorage。
    /// </summary>
    public static IServiceCollection AddBlazorBoilerplateAttachments(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        var options = configuration.GetSection(AttachmentStorageOptions.SectionName).Get<AttachmentStorageOptions>()
            ?? new AttachmentStorageOptions();
        services.AddSingleton(options);

        var minioConn = configuration["Infrastructure:MinIO"];
        var s3Enabled = configuration.GetValue<bool?>("ModuleStorage:Enabled") ?? false;
        var useS3 = string.Equals(options.StorageMode, "S3", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(options.StorageMode, "Auto", StringComparison.OrdinalIgnoreCase)
                && s3Enabled
                && !string.IsNullOrWhiteSpace(minioConn)
                && !string.Equals(minioConn, "not-configured", StringComparison.OrdinalIgnoreCase));

        var root = Path.IsPathRooted(options.LocalRootPath)
            ? options.LocalRootPath
            : Path.Combine(contentRootPath, options.LocalRootPath);
        services.AddSingleton<IAttachmentMetadataStore>(_ => new JsonAttachmentMetadataStore(root));

        if (useS3)
        {
            services.AddSingleton<IAttachmentStorage, S3AttachmentStorage>();
        }
        else
        {
            services.AddSingleton<IAttachmentStorage>(_ => new LocalAttachmentStorage(root));
        }

        return services;
    }
}
