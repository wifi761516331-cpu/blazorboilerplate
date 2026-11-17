using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BlazorBoilerplate.Infrastructure.Caching;

/// <summary>
/// 延迟创建 ConnectionMultiplexer。Redis 未配置时不建立连接；
/// 配置发生变化（安装向导完成或热切换）后立即释放旧连接，下次访问按新连接串重建。
/// </summary>
public sealed class RedisConnectionProvider : IDisposable
{
    private readonly ILogger<RedisConnectionProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private ConnectionMultiplexer? _multiplexer;
    private string? _configuredConnectionString;
    private bool _disposed;

    public RedisConnectionProvider(ILogger<RedisConnectionProvider> logger)
    {
        _logger = logger;
        RedisCacheState.ConnectionChanged += OnConnectionChanged;
    }

    public async Task<IDatabase> GetDatabaseAsync(CancellationToken cancellationToken)
    {
        var multiplexer = await GetMultiplexerAsync(cancellationToken).ConfigureAwait(false);
        return multiplexer.GetDatabase();
    }

    public async Task<ISubscriber> GetSubscriberAsync(CancellationToken cancellationToken)
    {
        var multiplexer = await GetMultiplexerAsync(cancellationToken).ConfigureAwait(false);
        return multiplexer.GetSubscriber();
    }

    public async Task<IReadOnlyCollection<IServer>> GetServersAsync(CancellationToken cancellationToken)
    {
        var multiplexer = await GetMultiplexerAsync(cancellationToken).ConfigureAwait(false);
        return multiplexer.GetEndPoints()
            .Select(endpoint => multiplexer.GetServer(endpoint))
            .ToArray();
    }

    private async Task<ConnectionMultiplexer> GetMultiplexerAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var desired = RedisCacheState.ConnectionString
            ?? throw new InvalidOperationException("Redis cache is not configured.");

        if (CanReuse(desired))
            return _multiplexer!;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (CanReuse(desired))
                return _multiplexer!;

            lock (_sync)
            {
                _multiplexer?.Dispose();
                _multiplexer = null;
                _configuredConnectionString = null;
            }

            var options = ConfigurationOptions.Parse(desired);
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 5000;
            options.SyncTimeout = 5000;
            options.ConnectRetry = 1;

            var multiplexer = await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
            lock (_sync)
            {
                _multiplexer = multiplexer;
                _configuredConnectionString = desired;
            }
            _logger.LogInformation("Redis cache connection initialized.");
            return multiplexer;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool CanReuse(string desired)
    {
        lock (_sync)
        {
            return _multiplexer is not null
                && string.Equals(_configuredConnectionString, desired, StringComparison.Ordinal);
        }
    }

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        lock (_sync)
        {
            _multiplexer?.Dispose();
            _multiplexer = null;
            _configuredConnectionString = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        RedisCacheState.ConnectionChanged -= OnConnectionChanged;
        lock (_sync)
        {
            _multiplexer?.Dispose();
            _multiplexer = null;
        }
        _gate.Dispose();
    }
}
