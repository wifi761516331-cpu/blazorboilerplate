using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text;
using BlazorBoilerplate.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.Infrastructure.Email;

/// <summary>
/// 基于 SMTP 的 <see cref="IEmailSender"/> 实现（参考 Bluebird SMTP 契约）。
/// 支持 STARTTLS/SSL、账号密码认证、超时控制与瞬态异常有限重试。
/// </summary>
public sealed class SmtpEmailSender(
    ISmtpSettingsProvider settingsProvider,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(1500)
    ];

    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(to))
            throw new ArgumentException("收件人邮箱不能为空。", nameof(to));
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("邮件主题不能为空。", nameof(subject));

        var settings = settingsProvider.GetSettings();
        if (!settings.IsConfigured)
            throw new InvalidOperationException("SMTP 服务尚未配置，请在配置文件或初始化向导中设置 SMTP 连接串。");

        if (string.IsNullOrWhiteSpace(settings.FromEmail))
            throw new InvalidOperationException("SMTP 发件人邮箱（FromEmail / DefaultFromEmail）不能为空。");

        var maxAttempts = 1 + Math.Min(settings.MaxRetries, RetryDelays.Length);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await SendOnceAsync(settings, to.Trim(), subject.Trim(), body, cancellationToken)
                    .ConfigureAwait(false);

                logger.LogInformation("邮件发送成功：To={To}, Subject={Subject}, Host={Host}:{Port}",
                    to.Trim(), subject.Trim(), settings.Host, settings.Port);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransient(ex))
            {
                var delay = RetryDelays[attempt - 1];
                logger.LogWarning(ex,
                    "SMTP 发送出现瞬态错误（第 {Attempt}/{Max} 次尝试），{DelayMs}ms 后重试。To={To}",
                    attempt, maxAttempts, delay.TotalMilliseconds, to.Trim());
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SMTP 邮件发送失败：To={To}, Subject={Subject}, Host={Host}:{Port}",
                    to.Trim(), subject.Trim(), settings.Host, settings.Port);
                throw;
            }
        }
    }

    private static async Task SendOnceAsync(
        SmtpSettings settings,
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromEmail, settings.FromName, Encoding.UTF8),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            Body = body ?? string.Empty,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true
        };
        message.To.Add(new MailAddress(to));

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Timeout = settings.TimeoutSeconds * 1000
        };

        if (settings.HasCredentials)
            client.Credentials = new NetworkCredential(settings.UserName, settings.Password);

        await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsTransient(Exception ex) =>
        ex is IOException or SocketException
        || (ex is SmtpException smtp && smtp.StatusCode is
            SmtpStatusCode.ServiceNotAvailable or
            SmtpStatusCode.MailboxBusy or
            SmtpStatusCode.LocalErrorInProcessing or
            SmtpStatusCode.TransactionFailed);
}

/// <summary>HTML 邮件模板构建器（参考 Bluebird EmailTokenTemplate 风格，所有动态文本均做 HTML 编码）。</summary>
public static class EmailTemplateBuilder
{
    public static string BuildNotificationEmail(
        string title,
        string message,
        string? recipientName = null,
        string? actionUrl = null,
        string? actionText = null,
        string appName = "BlazorBoilerplate 平台")
    {
        var encodedTitle = WebUtility.HtmlEncode(title);
        var encodedMessage = WebUtility.HtmlEncode(message).Replace("\n", "<br />");
        var encodedApp = WebUtility.HtmlEncode(appName);
        var greeting = string.IsNullOrWhiteSpace(recipientName)
            ? "您好，"
            : $"您好，{WebUtility.HtmlEncode(recipientName)}：";

        var actionBlock = string.IsNullOrWhiteSpace(actionUrl)
            ? string.Empty
            : $"""
               <tr>
                 <td style="padding-top:24px;">
                   <a href="{WebUtility.HtmlEncode(actionUrl)}"
                      style="display:inline-block;padding:10px 22px;background:#0f6cbd;color:#ffffff;text-decoration:none;border-radius:4px;font-size:14px;font-weight:600;">
                     {WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(actionText) ? "查看详情" : actionText)}
                   </a>
                 </td>
               </tr>
               """;

        return $$"""
                 <!DOCTYPE html>
                 <html lang="zh-CN">
                 <head>
                   <meta charset="utf-8" />
                   <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                   <title>{{encodedTitle}}</title>
                 </head>
                 <body style="margin:0;padding:0;background:#f5f5f5;font-family:'Segoe UI',Arial,sans-serif;color:#242424;">
                   <main style="padding:36px 16px;">
                     <table style="max-width:600px;width:100%;margin:0 auto;background:#ffffff;border:1px solid #EDEBE9;border-radius:6px;padding:36px 32px;border-collapse:separate;">
                       <tr>
                         <td style="color:#0D2960;font-weight:600;font-size:20px;padding-bottom:18px;border-bottom:1px solid #f0f0f0;">
                           {{encodedTitle}}
                         </td>
                       </tr>
                       <tr>
                         <td style="padding-top:20px;font-size:14px;color:#424242;line-height:1.7;">
                           <p style="margin:0 0 12px 0;">{{greeting}}</p>
                           <p style="margin:0;">{{encodedMessage}}</p>
                         </td>
                       </tr>
                       {{actionBlock}}
                     </table>
                     <table style="max-width:600px;width:100%;margin:16px auto 0 auto;text-align:center;">
                       <tr>
                         <td style="font-size:12px;color:#6b7280;">
                           此邮件由 {{encodedApp}} 自动发送，请勿直接回复。
                         </td>
                       </tr>
                     </table>
                   </main>
                 </body>
                 </html>
                 """;
    }
}

