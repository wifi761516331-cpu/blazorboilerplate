using BlazorBoilerplate.Infrastructure.Email;

namespace BlazorBoilerplate.ApiHost.Services;

/// <summary>
/// 将系统设置（<see cref="SystemSettingsStore"/>）作为优先的运行时 SMTP 配置源；
/// 当系统设置尚未填写 SMTP 服务器时，自动回退到配置文件 / 初始化向导提供的配置。
/// </summary>
public sealed class SystemSettingsSmtpProvider(
    SystemSettingsStore store,
    IConfiguration configuration) : ISmtpSettingsProvider
{
    private readonly ConfigurationSmtpSettingsProvider _fallback = new(configuration);

    public SmtpSettings GetSettings()
    {
        var s = store.GetRaw();
        if (string.IsNullOrWhiteSpace(s.SmtpHost))
            return _fallback.GetSettings();

        var userName = (s.SmtpUserName ?? string.Empty).Trim();
        var fromEmail = string.IsNullOrWhiteSpace(s.SmtpFromEmail) ? userName : s.SmtpFromEmail.Trim();
        var fromName = string.IsNullOrWhiteSpace(s.SmtpFromName) ? s.SystemName : s.SmtpFromName.Trim();

        return new SmtpSettings(
            Host: s.SmtpHost.Trim(),
            Port: s.SmtpPort > 0 ? s.SmtpPort : 587,
            UserName: userName,
            Password: s.SmtpPassword ?? string.Empty,
            EnableSsl: s.SmtpEnableSsl,
            FromEmail: fromEmail,
            FromName: fromName);
    }

    public void UpdateConnectionString(string? connectionString) =>
        _fallback.UpdateConnectionString(connectionString);
}

