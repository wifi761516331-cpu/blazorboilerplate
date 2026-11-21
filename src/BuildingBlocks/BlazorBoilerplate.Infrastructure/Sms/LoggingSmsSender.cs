using BlazorBoilerplate.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.Infrastructure.Sms;

/// <summary>默认短信发送实现（未接入外部短信网关时记录日志，便于开发与本地验证）。</summary>
public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("SMS -> {PhoneNumber}: {Message}", phoneNumber, message);
        return Task.CompletedTask;
    }
}

