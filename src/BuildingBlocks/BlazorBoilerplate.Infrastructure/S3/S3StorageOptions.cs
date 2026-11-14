namespace BlazorBoilerplate.Infrastructure.S3;

/// <summary>
/// S3 / MinIO 连接配置。默认值直接对接本机 MinIO 默认实例
/// （端点 http://127.0.0.1:9000，密钥 minioadmin/minioadmin，桶 blazorboilerplate-modules）。
/// 不改任何配置即可在「电脑装了默认 MinIO」的环境下工作；生产环境在 appsettings 覆盖即可。
/// </summary>
public sealed class S3StorageOptions
{
    public const string SectionName = "ModuleStorage";

    /// <summary>S3 兼容端点（含协议，不含桶名）。MinIO 默认 http://127.0.0.1:9000。</summary>
    public string Endpoint { get; set; } = "http://127.0.0.1:9000";

    /// <summary>访问密钥。MinIO 默认 minioadmin。</summary>
    public string AccessKey { get; set; } = "minioadmin";

    /// <summary>私密密钥。MinIO 默认 minioadmin。</summary>
    public string SecretKey { get; set; } = "minioadmin";

    /// <summary>目标桶名。不存在时会在宿主启动时自动创建（best-effort）。</summary>
    public string Bucket { get; set; } = "blazorboilerplate-modules";

    /// <summary>区域串。MinIO 不强制校验区域，惯例填 us-east-1。</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>是否启用 S3 存储。false 时回退到内存占位实现（仅测试/离线）。</summary>
    public bool Enabled { get; set; } = true;
}
