using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BlazorBoilerplate.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.Infrastructure.S3;

/// <summary>
/// 基于 AWS SigV4（零依赖 HttpClient）的 S3 / MinIO 对象存储实现，实现平台 IFileStorage 契约。
/// 使用 path-style 寻址（http://host/bucket/key），与 MinIO 默认部署兼容。
/// 模块包上传即写入此存储；桶不存在时在宿主启动时自动创建（best-effort）。
/// </summary>
public sealed class S3FileStorage : IFileStorage, IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly S3StorageOptions _options;
    private readonly ILogger<S3FileStorage> _logger;
    private readonly SemaphoreSlim _bucketGate = new(1, 1);
    private bool _bucketChecked;

    public S3FileStorage(S3StorageOptions options, ILogger<S3FileStorage> logger)
    {
        _options = options;
        _logger = logger;
        _http = new HttpClient { BaseAddress = new Uri(EnsureTrailingSlash(_options.Endpoint)) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "BlazorBoilerplate/1.0 S3FileStorage");
    }

    private string ObjectUrl(string key) => $"{_options.Bucket}/{EncodeKey(key)}";

    public async Task PutAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        var bytes = await ReadAllBytesAsync(content, cancellationToken);
        var url = new Uri(_http.BaseAddress!, ObjectUrl(key));
        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        AwsSigV4Signer.Sign(request, bytes, _options.Region, _options.AccessKey, _options.SecretKey);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"S3 PutObject 失败 ({response.StatusCode}) key={key}: {body}");
        }
        _logger.LogInformation("S3 object uploaded. Bucket={Bucket}, Key={Key}, Bytes={Bytes}",
            _options.Bucket, key, bytes.Length);
    }

    public async Task<Stream?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var url = new Uri(_http.BaseAddress!, ObjectUrl(key));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AwsSigV4Signer.Sign(request, [], _options.Region, _options.AccessKey, _options.SecretKey);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"S3 GetObject 失败 ({response.StatusCode}) key={key}: {body}");
        }

        var memory = new MemoryStream();
        await response.Content.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        return memory;
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var url = new Uri(_http.BaseAddress!, ObjectUrl(key));
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        AwsSigV4Signer.Sign(request, [], _options.Region, _options.AccessKey, _options.SecretKey);

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
            return;
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"S3 DeleteObject 失败 ({response.StatusCode}) key={key}: {body}");
        }
    }

    /// <summary>best-effort：确保目标桶存在，不存在则创建。失败仅告警，不阻断宿主启动。</summary>
    public async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        await _bucketGate.WaitAsync(cancellationToken);
        try
        {
            if (_bucketChecked) return;
            var exists = await BucketExistsAsync(cancellationToken);
            if (!exists)
            {
                _logger.LogInformation("S3 bucket {Bucket} 不存在，尝试创建…", _options.Bucket);
                await CreateBucketAsync(cancellationToken);
            }
            _bucketChecked = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "S3 桶检查/创建失败（已跳过，上传将失败）。Endpoint={Endpoint}, Bucket={Bucket}",
                _options.Endpoint, _options.Bucket);
        }
        finally
        {
            _bucketGate.Release();
        }
    }

    private async Task<bool> BucketExistsAsync(CancellationToken cancellationToken)
    {
        var url = new Uri(_http.BaseAddress!, _options.Bucket);
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        AwsSigV4Signer.Sign(request, [], _options.Region, _options.AccessKey, _options.SecretKey);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent)
            return true;
        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;
        // 其他错误（如 403）按“存在但需要权限”处理，避免误创建。
        _logger.LogWarning("S3 HeadBucket 返回 {Status}，假定桶已存在。", response.StatusCode);
        return true;
    }

    private async Task CreateBucketAsync(CancellationToken cancellationToken)
    {
        var url = new Uri(_http.BaseAddress!, _options.Bucket);
        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Content = new ByteArrayContent([]);
        AwsSigV4Signer.Sign(request, [], _options.Region, _options.AccessKey, _options.SecretKey);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Conflict)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"S3 CreateBucket 失败 ({response.StatusCode}): {body}");
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream content, CancellationToken cancellationToken)
    {
        if (content is MemoryStream ms)
        {
            if (ms.TryGetBuffer(out var seg))
                return seg.Array!.Length == seg.Count && seg.Offset == 0
                    ? seg.Array
                    : ms.ToArray();
            return ms.ToArray();
        }
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static string EncodeKey(string key)
    {
        var segments = key.Split('/');
        var sb = new StringBuilder(key.Length + 8);
        for (var i = 0; i < segments.Length; i++)
        {
            if (i > 0) sb.Append('/');
            sb.Append(Uri.EscapeDataString(segments[i]));
        }
        return sb.ToString();
    }

    private static string EnsureTrailingSlash(string endpoint) =>
        endpoint.EndsWith('/') ? endpoint : endpoint + "/";

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        _bucketGate.Dispose();
        await ValueTask.CompletedTask;
    }
}
