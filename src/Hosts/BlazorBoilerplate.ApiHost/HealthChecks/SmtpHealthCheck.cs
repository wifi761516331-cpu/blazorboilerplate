using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using BlazorBoilerplate.Infrastructure.Email;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BlazorBoilerplate.ApiHost.HealthChecks;

/// <summary>
/// SMTP 连通性与认证健康检查（参考 Bluebird SmtpHealthCheck）：
/// 连接 SMTP 端口，按 EnableSsl 升级 STARTTLS 并执行 AUTH LOGIN，然后 QUIT（不实际发信）。
/// </summary>
public sealed class SmtpHealthCheck(ISmtpSettingsProvider settingsProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var settings = settingsProvider.GetSettings();
        if (!settings.IsConfigured)
            return HealthCheckResult.Degraded("SMTP 未配置。");

        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(settings.Host, settings.Port, cancellationToken);
            Stream stream = tcpClient.GetStream();

            await ExpectReplyAsync(stream, "220", cancellationToken);
            await SendCommandAsync(stream, $"EHLO {Dns.GetHostName()}", "250", cancellationToken);

            if (settings.EnableSsl)
            {
                await SendCommandAsync(stream, "STARTTLS", "220", cancellationToken);
                var sslStream = new SslStream(stream);
                await sslStream.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions { TargetHost = settings.Host }, cancellationToken);
                stream = sslStream;
                await SendCommandAsync(stream, $"EHLO {Dns.GetHostName()}", "250", cancellationToken);
            }

            if (settings.HasCredentials)
            {
                await SendCommandAsync(stream, "AUTH LOGIN", "334", cancellationToken);
                await SendCommandAsync(stream,
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.UserName)),
                    "334", cancellationToken, "AUTH LOGIN user name");
                await SendCommandAsync(stream,
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.Password)),
                    "235", cancellationToken, "AUTH LOGIN password");
            }

            await SendCommandAsync(stream, "QUIT", "221", cancellationToken);

            return HealthCheckResult.Healthy($"SMTP 服务正常 ({settings.Host}:{settings.Port})");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "SMTP 健康检查失败", ex);
        }
    }

    private static async Task SendCommandAsync(
        Stream stream, string command, string expectedCode, CancellationToken cancellationToken, string? commandForErrors = null)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"{command}\r\n"), cancellationToken);
        await ExpectReplyAsync(stream, expectedCode, cancellationToken, commandForErrors ?? command);
    }

    private static async Task ExpectReplyAsync(
        Stream stream, string expectedCode, CancellationToken cancellationToken, string? command = null)
    {
        string line;
        do
        {
            line = await ReadLineAsync(stream, cancellationToken);
        }
        while (line.Length > 3 && line[3] is '-');

        if (!line.StartsWith(expectedCode, StringComparison.Ordinal))
            throw new InvalidOperationException($"SMTP 服务器对 {command ?? "连接"} 返回 '{line}'，期望 '{expectedCode}'。");
    }

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var line = new StringBuilder();

        while (await stream.ReadAsync(buffer, cancellationToken) is 1)
        {
            if (buffer[0] is (byte)'\n')
                return line.ToString().TrimEnd('\r');

            line.Append((char)buffer[0]);
        }

        throw new IOException("SMTP 服务器关闭了连接。");
    }
}

