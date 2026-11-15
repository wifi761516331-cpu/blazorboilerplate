using BlazorBoilerplate.ApiHost.Services;
using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Infrastructure.Email;
using BlazorBoilerplate.ModuleRuntime.Runtime;
using BlazorBoilerplate.Shared.Features.SystemSettings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BlazorBoilerplate.ApiHost.Controllers;

/// <summary>平台系统设置接口（参考 new-api 分组配置）。</summary>
[ApiController]
[Route("api/v1/[controller]/[action]")]
public sealed class SystemSettingsController(
    SystemSettingsStore store,
    IEmailSender emailSender,
    IOptions<ModuleRuntimeOptions> runtimeOptions)
    : ControllerBase, ISystemSettingsController
{
    [HttpGet]
    [AllowAnonymous]
    public Task<PublicSystemSettingsDto> GetPublic(CancellationToken cancellationToken) =>
        Task.FromResult(store.GetPublic());

    [HttpGet]
    [Authorize]
    public Task<SystemSettingsDto> Get(CancellationToken cancellationToken) =>
        Task.FromResult(store.GetMasked());

    [HttpPut]
    [Authorize]
    public Task<SystemSettingsDto> Update([FromBody] SystemSettingsDto request, CancellationToken cancellationToken)
    {
        var updated = store.Update(request);
        var opts = runtimeOptions.Value;
        opts.AutoLoadOnStartup = updated.AutoLoadOnStartup;
        opts.VerifyChecksum = updated.VerifyChecksum;
        opts.DrainTimeoutSeconds = updated.DrainTimeoutSeconds;
        return Task.FromResult(updated);
    }

    [HttpPost]
    [Authorize]
    public async Task SendTestEmail([FromBody] SendTestSmtpRequestDto request, CancellationToken cancellationToken)
    {
        var to = (request.ToEmail ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(to) || !to.Contains('@'))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { error = "请填写有效的测试收件人邮箱。" }, cancellationToken);
            return;
        }

        var s = store.GetRaw();
        var subject = $"[{s.SystemName}] SMTP 测试邮件";
        var html = EmailTemplateBuilder.BuildNotificationEmail(
            "SMTP 邮件配置测试",
            $"这是一封来自「{s.SystemName}」系统设置的测试邮件，发送时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}。",
            null,
            s.SystemName);

        try
        {
            await emailSender.SendAsync(to, subject, html, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new
            {
                error = $"SMTP 发送失败：{ex.GetBaseException().Message}（请检查网络/代理是否拦截了 SMTP 端口、SSL 设置以及邮箱授权码）"
            }, cancellationToken);
        }
    }
}
