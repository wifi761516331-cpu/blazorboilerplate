using Microsoft.Extensions.Configuration;

namespace BlazorBoilerplate.Infrastructure.Email;

/// <summary>
/// Email / SMTP 配置选项（与 Bluebird 的 Email + ConnectionStrings:smtp 风格对齐）。
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>默认发件人邮箱（QQ 邮箱 SMTP 要求发件人与登录账号一致）。</summary>
    public string? DefaultFromEmail { get; set; }

    /// <summary>默认发件人显示名称。</summary>
    public string DefaultFromName { get; set; } = "BlazorBoilerplate";

    /// <summary>SMTP 超时时间（秒）。</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>瞬态故障最大重试次数。</summary>
    public int MaxRetries { get; set; } = 2;
}

/// <summary>解析后的强类型 SMTP 连接配置。</summary>
public sealed record SmtpSettings(
    string Host,
    int Port,
    string UserName,
    string Password,
    bool EnableSsl,
    string FromEmail,
    string FromName,
    int TimeoutSeconds = 30,
    int MaxRetries = 2)
{
    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(UserName) && !string.IsNullOrWhiteSpace(Password);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host);

    public static SmtpSettings Unconfigured { get; } =
        new(string.Empty, 25, string.Empty, string.Empty, false, string.Empty, "BlazorBoilerplate");
}

/// <summary>运行时 SMTP 配置提供器（支持启动加载与安装向导动态更新）。</summary>
public interface ISmtpSettingsProvider
{
    SmtpSettings GetSettings();
    void UpdateConnectionString(string? connectionString);
}

public sealed class ConfigurationSmtpSettingsProvider : ISmtpSettingsProvider
{
    private readonly Lock _gate = new();
    private SmtpSettings _current;

    public ConfigurationSmtpSettingsProvider(IConfiguration configuration)
    {
        var emailOptions = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
        var raw = ResolveRawConnectionString(configuration);
        _current = SmtpConnectionStringParser.Parse(raw, emailOptions);
    }

    public SmtpSettings GetSettings()
    {
        lock (_gate) return _current;
    }

    public void UpdateConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString) ||
            string.Equals(connectionString.Trim(), "not-configured", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_gate)
        {
            var options = new EmailOptions
            {
                DefaultFromEmail = _current.FromEmail,
                DefaultFromName = _current.FromName,
                TimeoutSeconds = _current.TimeoutSeconds,
                MaxRetries = _current.MaxRetries
            };
            _current = SmtpConnectionStringParser.Parse(connectionString, options);
        }
    }

    private static string? ResolveRawConnectionString(IConfiguration configuration)
    {
        string?[] candidates =
        [
            configuration.GetConnectionString("smtp"),
            configuration.GetConnectionString("SMTP"),
            configuration["Infrastructure:Smtp"],
            configuration["Infrastructure:SMTP"],
            configuration["Email:ConnectionString"]
        ];

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                !string.Equals(candidate.Trim(), "not-configured", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}

/// <summary>
/// 解析 Bluebird 风格 SMTP 连接串：
/// Endpoint=smtp://smtp.qq.com:587;UserName=xxx@qq.com;Password=xxx;EnableSsl=true
/// </summary>
public static class SmtpConnectionStringParser
{
    public static SmtpSettings Parse(string? connectionString, EmailOptions? emailOptions = null)
    {
        emailOptions ??= new EmailOptions();

        if (string.IsNullOrWhiteSpace(connectionString) ||
            string.Equals(connectionString.Trim(), "not-configured", StringComparison.OrdinalIgnoreCase))
        {
            return SmtpSettings.Unconfigured;
        }

        var map = ParseKeyValues(connectionString);
        var endpointRaw = GetValue(map, "Endpoint", "smtp://localhost:25");

        if (!endpointRaw.Contains("://", StringComparison.Ordinal))
            endpointRaw = "smtp://" + endpointRaw;

        if (!Uri.TryCreate(endpointRaw, UriKind.Absolute, out var endpoint) || string.IsNullOrWhiteSpace(endpoint.Host))
            throw new InvalidOperationException($"无效的 SMTP Endpoint 配置：{endpointRaw}");

        var port = endpoint.Port is -1
            ? (string.Equals(endpoint.Scheme, "smtps", StringComparison.OrdinalIgnoreCase) ? 465 : 25)
            : endpoint.Port;

        var userName = GetValue(map, "UserName", string.Empty);
        var password = GetValue(map, "Password", string.Empty);

        var defaultSsl = port == 465 || port == 587 ||
                         string.Equals(endpoint.Scheme, "smtps", StringComparison.OrdinalIgnoreCase);
        var enableSslRaw = GetValue(map, "EnableSsl", defaultSsl ? "true" : "false");
        var enableSsl = !string.Equals(enableSslRaw, "false", StringComparison.OrdinalIgnoreCase);

        var fromEmail = GetValue(map, "FromEmail",
            !string.IsNullOrWhiteSpace(emailOptions.DefaultFromEmail) ? emailOptions.DefaultFromEmail! : userName);
        var fromName = GetValue(map, "FromName",
            !string.IsNullOrWhiteSpace(emailOptions.DefaultFromName) ? emailOptions.DefaultFromName : "BlazorBoilerplate");

        return new SmtpSettings(
            Host: endpoint.Host,
            Port: port,
            UserName: userName,
            Password: password,
            EnableSsl: enableSsl,
            FromEmail: fromEmail,
            FromName: fromName,
            TimeoutSeconds: Math.Clamp(emailOptions.TimeoutSeconds, 5, 120),
            MaxRetries: Math.Clamp(emailOptions.MaxRetries, 0, 5));
    }

    private static Dictionary<string, string> ParseKeyValues(string connectionString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            var key = part[..idx].Trim();
            var val = part[(idx + 1)..].Trim();
            if (!string.IsNullOrEmpty(key))
                result[key] = val;
        }
        return result;
    }

    private static string GetValue(Dictionary<string, string> map, string key, string defaultValue) =>
        map.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val) ? val : defaultValue;
}

