using System.Collections.Concurrent;
using BlazorBoilerplate.Application.Abstractions;
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

public sealed class PlaceholderIntegrationEventBus(ILogger<PlaceholderIntegrationEventBus> logger) : IIntegrationEventBus
{
    private readonly ConcurrentQueue<IntegrationEvent> _pending = new();
    public IReadOnlyCollection<IntegrationEvent> Pending => _pending;

    public Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _pending.Enqueue(integrationEvent);
        logger.LogWarning("Outbox/Transport 尚未接入（第二批）；IntegrationEvent 仅在内存中排队。Type={Type}",
            integrationEvent.GetType().Name);
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
        services.AddSingleton<IIntegrationEventBus, PlaceholderIntegrationEventBus>();
        services.AddSingleton<IUseCaseDispatcher, UseCaseDispatcher>();
        return services;
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
}
