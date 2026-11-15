using BlazorBoilerplate.Shared.Infrastructure.Services.Contracts;

namespace BlazorBoilerplate.Shared.Features.SystemSettings;

/// <summary>系统设置完整配置（管理员读取/保存）。敏感字段（密码/密钥）返回脱敏占位符。</summary>
public sealed class SystemSettingsDto
{
    // 1. 基础与站点
    public string SystemName { get; set; } = "BlazorBoilerplate 平台";
    public string FooterText { get; set; } = ".NET 10 · Bit.BlazorUI · 动态模块运行时";
    public string Notice { get; set; } = string.Empty;
    public string DefaultCulture { get; set; } = "zh-CN";
    public string DefaultTimeZone { get; set; } = "Asia/Shanghai";

    // 2. 登录与安全
    public bool RegisterEnabled { get; set; } = true;
    public bool EmailVerificationRequired { get; set; }
    public int PasswordMinLength { get; set; } = 6;
    public int MaxSessionsPerUser { get; set; } = 10;
    public int AccessTokenMinutes { get; set; } = 60;

    // 3. 邮件服务 (SMTP)
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool SmtpEnableSsl { get; set; } = true;
    public string SmtpUserName { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public string SmtpFromEmail { get; set; } = string.Empty;
    public string SmtpFromName { get; set; } = "BlazorBoilerplate";

    // 4. 存储与附件
    public string StorageProvider { get; set; } = "Local";
    public string S3Endpoint { get; set; } = string.Empty;
    public string S3Bucket { get; set; } = string.Empty;
    public string S3Region { get; set; } = "us-east-1";
    public string S3AccessKey { get; set; } = string.Empty;
    public string S3SecretKey { get; set; } = string.Empty;
    public int MaxUploadSizeMb { get; set; } = 11;
    public string AllowedExtensions { get; set; } = ".jpg,.jpeg,.png,.webp,.gif,.bmp,.pdf,.txt,.md,.json,.csv,.zip,.doc,.docx,.xls,.xlsx";

    // 5. 模块运行时
    public bool AutoLoadOnStartup { get; set; } = true;
    public bool VerifyChecksum { get; set; } = true;
    public int DrainTimeoutSeconds { get; set; } = 15;

    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

/// <summary>公开系统设置（匿名/普通用户可读，不含任何密钥）。</summary>
public sealed record PublicSystemSettingsDto(
    string SystemName,
    string FooterText,
    string Notice,
    bool RegisterEnabled,
    string DefaultCulture,
    string DefaultTimeZone,
    int MaxUploadSizeMb,
    int PasswordMinLength,
    string AllowedExtensions = ".jpg,.jpeg,.png,.webp,.gif,.bmp,.pdf,.txt,.md,.json,.csv,.zip,.doc,.docx,.xls,.xlsx",
    string StorageProvider = "Local");

public sealed record SendTestSmtpRequestDto(string ToEmail);

[Route("api/v1/[controller]/[action]/")]
public interface ISystemSettingsController : IAppController
{
    [HttpGet] Task<PublicSystemSettingsDto> GetPublic(CancellationToken cancellationToken) => default!;
    [HttpGet] Task<SystemSettingsDto> Get(CancellationToken cancellationToken) => default!;
    [HttpPut] Task<SystemSettingsDto> Update(SystemSettingsDto request, CancellationToken cancellationToken) => default!;
    [HttpPost] Task SendTestEmail(SendTestSmtpRequestDto request, CancellationToken cancellationToken) => Task.CompletedTask;
}

