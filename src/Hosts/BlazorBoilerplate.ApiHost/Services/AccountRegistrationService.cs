using BlazorBoilerplate.ApiHost.Auth;
using BlazorBoilerplate.Application.Abstractions;
using BlazorBoilerplate.Infrastructure.Email;
using BlazorBoilerplate.Modules.Identity.Services;
using BlazorBoilerplate.Modules.Identity.Domain;
using BlazorBoilerplate.Modules.Identity.Persistence;
using BlazorBoilerplate.Shared.Features.Identity;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.ApiHost.Services;

/// <summary>
/// 账号自助注册与邮箱验证码找回密码应用服务：
/// 将业务校验、默认角色分配、会话撤销与审计日志从 Controller 抽离，保持 Endpoint 只做协议适配。
/// </summary>
public sealed class AccountRegistrationService(
    IdentityDbContext db,
    JwtTokenService tokens,
    CurrentUserFactory factory,
    SystemSettingsStore settings,
    EmailVerificationCodeStore emailCodes,
    IEmailSender emailSender,
    ISmtpSettingsProvider smtpSettingsProvider,
    ILogger<AccountRegistrationService> logger)
{
    public async Task SendEmailCodeAsync(SendEmailCodeRequestDto request, CancellationToken ct)
    {
        var email = (request.Email ?? string.Empty).Trim();
        var purpose = (request.Purpose ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new InvalidOperationException("请输入有效的邮箱地址。");

        if (!smtpSettingsProvider.GetSettings().IsConfigured)
            throw new InvalidOperationException("系统尚未配置 SMTP 邮件服务，请联系管理员在系统设置中配置。");

        var sys = settings.GetRaw();
        var sysName = string.IsNullOrWhiteSpace(sys.SystemName) ? "BlazorBoilerplate 平台" : sys.SystemName;
        string subject;
        string actionTitle;

        if (purpose == EmailVerificationCodeStore.PurposeRegister)
        {
            if (!sys.RegisterEnabled)
                throw new InvalidOperationException("当前系统已关闭用户自助注册。");
            if (await db.Users.AsNoTracking().AnyAsync(u => u.Email.ToLower() == email.ToLower(), ct))
                throw new InvalidOperationException("该邮箱已被注册。");
            subject = $"[{sysName}] 注册邮箱验证码";
            actionTitle = "账号注册邮箱验证";
        }
        else if (purpose == EmailVerificationCodeStore.PurposeResetPassword)
        {
            var user = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), ct)
                ?? throw new InvalidOperationException("该邮箱尚未注册账号。");
            if (!user.IsActive)
                throw new InvalidOperationException("该账号已被停用，请联系管理员。");
            subject = $"[{sysName}] 找回密码验证码";
            actionTitle = "重置登录密码验证";
        }
        else
        {
            throw new InvalidOperationException("不支持的验证码用途。");
        }

        var code = emailCodes.Issue(purpose, email);
        var html = EmailTemplateBuilder.BuildNotificationEmail(
            actionTitle,
            $"您的 6 位邮箱验证码为：{code}（10 分钟内有效，请勿泄露给他人）。",
            null,
            sysName);

        try
        {
            await emailSender.SendAsync(email, subject, html, ct);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // SMTP 客户端抛的是裸 SmtpException（如 "Failure sending mail."），不匹配 ApiExceptionFilter
            // 的业务异常分支，会变成 500 + 英文技术错误，前端拿不到可读信息。
            // 统一包装为带稳定错误码的业务异常（400）：前端按当前界面语言翻译 errorArgs[0] 里的技术原因。
            logger.LogError(ex, "邮箱验证码邮件发送失败：Purpose={Purpose}, Email={Email}", purpose, email);
            throw new BlazorBoilerplate.Application.Localization.LocalizedApiException(
                "identity.common.error.emailSendFailed",
                $"验证码邮件发送失败，请稍后重试或联系管理员。原因：{ex.Message}",
                ex.Message);
        }
        logger.LogInformation("Audit: Email verification code sent. Purpose={Purpose}, Email={Email}", purpose, email);
    }

    public async Task<SignInResponseDto> RegisterAsync(RegisterRequestDto request, string? clientIp, CancellationToken ct)
    {
        var sys = settings.GetRaw();
        if (!sys.RegisterEnabled)
            throw new InvalidOperationException("当前系统已关闭用户自助注册。");

        var userName = (request.UserName ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim();
        var password = request.Password ?? string.Empty;
        var minLen = Math.Max(6, sys.PasswordMinLength);

        if (string.IsNullOrWhiteSpace(userName) || userName.Length < 2)
            throw new InvalidOperationException("用户名至少需要 2 个字符。");
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new InvalidOperationException("请输入有效的邮箱地址。");
        if (password.Length < minLen)
            throw new InvalidOperationException($"密码至少需要 {minLen} 位。");

        if (await db.Users.AnyAsync(u => u.UserName.ToLower() == userName.ToLower(), ct))
            throw new InvalidOperationException("该用户名已被使用。");
        if (await db.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower(), ct))
            throw new InvalidOperationException("该邮箱已被注册。");

        emailCodes.VerifyAndConsume(EmailVerificationCodeStore.PurposeRegister, email, request.VerificationCode);

        var user = User.Create(userName, email, request.DisplayName ?? userName, PasswordHasher.Hash(password));
        db.Users.Add(user);

        var defaultRoleId = await db.Roles.AsNoTracking()
            .Where(r => r.IsSystem && r.Name == "普通用户")
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);
        if (defaultRoleId is { } roleId)
            db.UserRoles.Add(new UserRole(user.Id, roleId));

        var defaultCulture = string.IsNullOrWhiteSpace(sys.DefaultCulture) ? "zh-CN" : sys.DefaultCulture;
        var defaultTimeZone = string.IsNullOrWhiteSpace(sys.DefaultTimeZone) ? "Asia/Shanghai" : sys.DefaultTimeZone;
        var session = UserSession.Create(user.Id, null, clientIp, "Blazor Server", defaultCulture, defaultTimeZone, false, null);
        db.UserSessions.Add(session);

        var refresh = tokens.IssueRefreshToken(user.Id, session.Id, null);
        session.Renew(refresh);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Audit: User registered via email verification. UserId={UserId}, UserName={UserName}", user.Id, user.UserName);

        var current = await factory.BuildAsync(user, null, ct);
        var access = tokens.IssueAccessToken(user.Id, user.UserName, user.Email, session.Id, null, current.Roles, current.Permissions);
        return new SignInResponseDto(access, refresh, tokens.AccessTokenLifetimeSeconds, current);
    }

    public async Task ResetPasswordByEmailAsync(ResetPasswordByEmailRequestDto request, CancellationToken ct)
    {
        var email = (request.Email ?? string.Empty).Trim();
        var newPassword = request.NewPassword ?? string.Empty;
        var minLen = Math.Max(6, settings.GetRaw().PasswordMinLength);

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new InvalidOperationException("请输入有效的邮箱地址。");
        if (newPassword.Length < minLen)
            throw new InvalidOperationException($"新密码至少需要 {minLen} 位。");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), ct)
            ?? throw new InvalidOperationException("该邮箱尚未注册账号。");
        if (!user.IsActive)
            throw new InvalidOperationException("该账号已被停用，请联系管理员。");

        emailCodes.VerifyAndConsume(EmailVerificationCodeStore.PurposeResetPassword, email, request.VerificationCode);
        user.SetPassword(PasswordHasher.Hash(newPassword));

        // 重置密码后吊销该用户所有活跃会话，防止旧 Refresh Token 继续使用
        var activeSessions = await db.UserSessions
            .Where(s => s.UserId == user.Id && s.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var session in activeSessions)
            session.Revoke();

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Audit: Password reset via email verification. UserId={UserId}, RevokedSessions={Count}", user.Id, activeSessions.Count);
    }
}
