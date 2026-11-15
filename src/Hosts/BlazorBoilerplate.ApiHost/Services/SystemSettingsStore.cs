using System.Text.Json;
using BlazorBoilerplate.ModuleRuntime.Runtime;
using BlazorBoilerplate.Shared.Features.SystemSettings;
using Microsoft.Extensions.Options;

namespace BlazorBoilerplate.ApiHost.Services;

/// <summary>
/// 平台系统设置持久化存储（<c>data/system-settings.json</c>）：
/// 首次读取时自动从 <see cref="IConfiguration"/> 继承默认值，并支持敏感字段脱敏与原子写入。
/// </summary>
public sealed class SystemSettingsStore
{
    public const string SecretMask = "******";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _filePath;
    private readonly IConfiguration _configuration;
    private readonly IOptions<ModuleRuntimeOptions> _runtimeOptions;
    private readonly ILogger<SystemSettingsStore> _logger;
    private readonly object _gate = new();
    private SystemSettingsDto? _cached;

    public SystemSettingsStore(
        IHostEnvironment env,
        IConfiguration configuration,
        IOptions<ModuleRuntimeOptions> runtimeOptions,
        ILogger<SystemSettingsStore> logger)
    {
        _filePath = Path.Combine(env.ContentRootPath, "data", "system-settings.json");
        _configuration = configuration;
        _runtimeOptions = runtimeOptions;
        _logger = logger;
    }

    public SystemSettingsDto GetRaw()
    {
        lock (_gate)
        {
            _cached ??= LoadOrCreateDefaults();
            return Clone(_cached);
        }
    }

    public SystemSettingsDto GetMasked()
    {
        var raw = GetRaw();
        if (!string.IsNullOrEmpty(raw.SmtpPassword))
            raw.SmtpPassword = SecretMask;
        if (!string.IsNullOrEmpty(raw.S3SecretKey))
            raw.S3SecretKey = SecretMask;
        return raw;
    }

    public PublicSystemSettingsDto GetPublic()
    {
        var raw = GetRaw();
        return new PublicSystemSettingsDto(
            raw.SystemName,
            raw.FooterText,
            raw.Notice,
            raw.RegisterEnabled,
            raw.DefaultCulture,
            raw.DefaultTimeZone,
            raw.MaxUploadSizeMb,
            raw.PasswordMinLength,
            raw.AllowedExtensions,
            raw.StorageProvider);
    }

    public SystemSettingsDto Update(SystemSettingsDto incoming)
    {
        lock (_gate)
        {
            var current = _cached ?? LoadOrCreateDefaults();
            var next = Clone(incoming);

            next.SystemName = string.IsNullOrWhiteSpace(next.SystemName) ? "BlazorBoilerplate 平台" : next.SystemName.Trim();
            next.FooterText = string.IsNullOrWhiteSpace(next.FooterText) ? ".NET 10 · Bit.BlazorUI · 动态模块运行时" : next.FooterText.Trim();
            next.Notice = next.Notice?.Trim() ?? string.Empty;
            next.DefaultCulture = string.IsNullOrWhiteSpace(next.DefaultCulture) ? "zh-CN" : next.DefaultCulture.Trim();
            next.DefaultTimeZone = string.IsNullOrWhiteSpace(next.DefaultTimeZone) ? "Asia/Shanghai" : next.DefaultTimeZone.Trim();

            next.PasswordMinLength = Math.Clamp(next.PasswordMinLength, 4, 64);
            next.MaxSessionsPerUser = Math.Clamp(next.MaxSessionsPerUser, 1, 100);
            next.AccessTokenMinutes = Math.Clamp(next.AccessTokenMinutes, 5, 60 * 24 * 30);

            next.SmtpHost = next.SmtpHost?.Trim() ?? string.Empty;
            next.SmtpPort = Math.Clamp(next.SmtpPort, 1, 65535);
            next.SmtpUserName = next.SmtpUserName?.Trim() ?? string.Empty;
            next.SmtpFromEmail = next.SmtpFromEmail?.Trim() ?? string.Empty;
            next.SmtpFromName = string.IsNullOrWhiteSpace(next.SmtpFromName) ? next.SystemName : next.SmtpFromName.Trim();

            if (string.IsNullOrWhiteSpace(next.SmtpPassword) || next.SmtpPassword == SecretMask)
                next.SmtpPassword = current.SmtpPassword;

            next.StorageProvider = string.Equals(next.StorageProvider, "S3", StringComparison.OrdinalIgnoreCase) ? "S3" : "Local";
            next.S3Endpoint = next.S3Endpoint?.Trim() ?? string.Empty;
            next.S3Bucket = next.S3Bucket?.Trim() ?? string.Empty;
            next.S3Region = string.IsNullOrWhiteSpace(next.S3Region) ? "us-east-1" : next.S3Region.Trim();
            next.S3AccessKey = next.S3AccessKey?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(next.S3SecretKey) || next.S3SecretKey == SecretMask)
                next.S3SecretKey = current.S3SecretKey;

            next.MaxUploadSizeMb = Math.Clamp(next.MaxUploadSizeMb, 1, 512);
            next.AllowedExtensions = next.AllowedExtensions?.Trim() ?? string.Empty;
            next.DrainTimeoutSeconds = Math.Clamp(next.DrainTimeoutSeconds, 1, 300);
            next.UpdatedAtUtc = DateTimeOffset.UtcNow;

            _cached = next;
            WriteAtomic(next);

            // 同步更新运行时选项（即时生效）
            _runtimeOptions.Value.AutoLoadOnStartup = next.AutoLoadOnStartup;
            _runtimeOptions.Value.VerifyChecksum = next.VerifyChecksum;
            _runtimeOptions.Value.DrainTimeoutSeconds = next.DrainTimeoutSeconds;

            return GetMasked();
        }
    }

    private SystemSettingsDto LoadOrCreateDefaults()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                var parsed = JsonSerializer.Deserialize<SystemSettingsDto>(json, JsonOptions);
                if (parsed is not null)
                    return parsed;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取系统设置文件 '{Path}' 失败，将使用默认配置。", _filePath);
            }
        }

        var defaults = BuildDefaultsFromConfiguration();
        WriteAtomic(defaults);
        return defaults;
    }

    private SystemSettingsDto BuildDefaultsFromConfiguration()
    {
        var dto = new SystemSettingsDto
        {
            AutoLoadOnStartup = _runtimeOptions.Value.AutoLoadOnStartup,
            VerifyChecksum = _runtimeOptions.Value.VerifyChecksum,
            DrainTimeoutSeconds = _runtimeOptions.Value.DrainTimeoutSeconds,
            SmtpFromEmail = _configuration["Email:DefaultFromEmail"] ?? string.Empty,
            SmtpFromName = _configuration["Email:DefaultFromName"] ?? "BlazorBoilerplate",
            StorageProvider = _configuration.GetValue<bool>("ModuleStorage:Enabled") ? "S3" : "Local",
            S3Endpoint = _configuration["ModuleStorage:Endpoint"] ?? string.Empty,
            S3Bucket = _configuration["ModuleStorage:Bucket"] ?? string.Empty,
            S3Region = _configuration["ModuleStorage:Region"] ?? "us-east-1",
            S3AccessKey = _configuration["ModuleStorage:AccessKey"] ?? string.Empty,
            S3SecretKey = _configuration["ModuleStorage:SecretKey"] ?? string.Empty
        };

        var smtpConn = _configuration.GetConnectionString("smtp") ?? _configuration["Infrastructure:Smtp"];
        if (!string.IsNullOrWhiteSpace(smtpConn))
            ParseSmtpConnectionString(smtpConn, dto);

        return dto;
    }

    private static void ParseSmtpConnectionString(string conn, SystemSettingsDto dto)
    {
        foreach (var part in conn.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            var k = part[..idx].Trim();
            var v = part[(idx + 1)..].Trim();
            if (k.Equals("Endpoint", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(v, UriKind.Absolute, out var uri))
            {
                dto.SmtpHost = uri.Host;
                if (uri.Port > 0) dto.SmtpPort = uri.Port;
            }
            else if (k.Equals("UserName", StringComparison.OrdinalIgnoreCase))
            {
                dto.SmtpUserName = v;
            }
            else if (k.Equals("Password", StringComparison.OrdinalIgnoreCase))
            {
                dto.SmtpPassword = v;
            }
            else if (k.Equals("EnableSsl", StringComparison.OrdinalIgnoreCase) && bool.TryParse(v, out var ssl))
            {
                dto.SmtpEnableSsl = ssl;
            }
        }
    }

    private void WriteAtomic(SystemSettingsDto dto)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var tmp = _filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(dto, JsonOptions));
        File.Move(tmp, _filePath, overwrite: true);
    }

    private static SystemSettingsDto Clone(SystemSettingsDto s) =>
        JsonSerializer.Deserialize<SystemSettingsDto>(JsonSerializer.Serialize(s, JsonOptions), JsonOptions)!;
}

