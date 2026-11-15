using BlazorBoilerplate.Infrastructure.Attachments;
using BlazorBoilerplate.Infrastructure.S3;

namespace BlazorBoilerplate.ApiHost.Services;

/// <summary>
/// 动态附件存储路由：根据系统设置（<see cref="SystemSettingsStore"/>）实时选择
/// 本地磁盘（<see cref="LocalAttachmentStorage"/>）或 S3 / MinIO（<see cref="S3FileStorage"/>），
/// 无需重启宿主即可在保存系统设置后切换存储后端与凭据。
/// </summary>
public sealed class DynamicAttachmentStorage : IAttachmentStorage, IAsyncDisposable
{
    private readonly SystemSettingsStore _store;
    private readonly LocalAttachmentStorage _local;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Lock _gate = new();

    private string? _s3Signature;
    private S3FileStorage? _s3;

    public DynamicAttachmentStorage(
        SystemSettingsStore store,
        AttachmentStorageOptions options,
        IHostEnvironment env,
        ILoggerFactory loggerFactory)
    {
        _store = store;
        _loggerFactory = loggerFactory;

        var root = Path.IsPathRooted(options.LocalRootPath)
            ? options.LocalRootPath
            : Path.Combine(env.ContentRootPath, options.LocalRootPath);
        _local = new LocalAttachmentStorage(root);
    }

    public async Task PutAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        var s3 = GetConfiguredS3();
        if (s3 is not null)
        {
            await s3.EnsureBucketAsync(cancellationToken).ConfigureAwait(false);
            await s3.PutAsync(key, content, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _local.PutAsync(key, content, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Stream?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var s3 = GetConfiguredS3();
        if (s3 is not null)
        {
            var stream = await s3.GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (stream is not null) return stream;
            return await _local.GetAsync(key, cancellationToken).ConfigureAwait(false);
        }

        return await _local.GetAsync(key, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var s3 = GetConfiguredS3();
        if (s3 is not null)
            await s3.DeleteAsync(key, cancellationToken).ConfigureAwait(false);

        await _local.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        var s3 = GetConfiguredS3();
        if (s3 is not null)
        {
            var urlStream = await s3.GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (urlStream is not null)
            {
                await urlStream.DisposeAsync().ConfigureAwait(false);
                return true;
            }
        }

        return await _local.ExistsAsync(key, cancellationToken).ConfigureAwait(false);
    }

    private S3FileStorage? GetConfiguredS3()
    {
        var s = _store.GetRaw();
        if (!string.Equals(s.StorageProvider, "S3", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(s.S3Endpoint)
            || string.IsNullOrWhiteSpace(s.S3Bucket))
        {
            return null;
        }

        var endpoint = s.S3Endpoint.Trim();
        var bucket = s.S3Bucket.Trim();
        var region = string.IsNullOrWhiteSpace(s.S3Region) ? "us-east-1" : s.S3Region.Trim();
        var accessKey = (s.S3AccessKey ?? string.Empty).Trim();
        var secretKey = s.S3SecretKey ?? string.Empty;
        var sig = $"{endpoint}|{bucket}|{region}|{accessKey}|{secretKey}";

        lock (_gate)
        {
            if (_s3 is not null && _s3Signature == sig)
                return _s3;

            var old = _s3;
            _s3 = new S3FileStorage(new S3StorageOptions
            {
                Endpoint = endpoint,
                Bucket = bucket,
                Region = region,
                AccessKey = accessKey,
                SecretKey = secretKey
            }, _loggerFactory.CreateLogger<S3FileStorage>());
            _s3Signature = sig;

            if (old is not null)
                _ = old.DisposeAsync();

            return _s3;
        }
    }

    public ValueTask DisposeAsync() =>
        _s3 is not null ? _s3.DisposeAsync() : ValueTask.CompletedTask;
}

