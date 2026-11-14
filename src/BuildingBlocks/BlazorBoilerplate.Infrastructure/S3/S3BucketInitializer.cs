using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.Infrastructure.S3;

/// <summary>宿主启动时 best-effort 确保模块存储桶存在。失败仅告警，不阻断启动。</summary>
public sealed class S3BucketInitializer : IHostedService
{
    private readonly S3FileStorage _storage;
    private readonly ILogger<S3BucketInitializer> _logger;

    public S3BucketInitializer(S3FileStorage storage, ILogger<S3BucketInitializer> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _storage.EnsureBucketAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "S3 存储桶初始化失败（模块上传功能将不可用，宿主继续运行）。");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
