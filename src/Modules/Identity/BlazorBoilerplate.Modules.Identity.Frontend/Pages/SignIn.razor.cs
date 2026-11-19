using Bit.BlazorUI;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Shared.Features.Identity;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BlazorBoilerplate.Modules.Identity.Frontend.Pages;

public partial class SignIn
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private ILogger<SignIn> Logger { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private FrontendLocalizationService FrontendLoc { get; set; } = default!;

    private enum AccountTab { Email, Phone }

    private AccountTab _tab = AccountTab.Email;
    private string _account = string.Empty;
    private string _phoneNumber = string.Empty; // E.164 完整号码（含 + 与区号），来自 BitPhoneInput.Value
    private string _password = string.Empty;
    private bool _remember;
    private bool _busy;
    private bool _accountInvalid;
    private bool _passwordInvalid;
    private string? _message;
    private bool _messageIsError;
    private string? _returnUrl;
    private string _systemName = "BlazorBoilerplate";
    private bool _registerEnabled = true;

    protected override async Task OnInitializedAsync()
    {
        await FrontendLoc.EnsureLoadedAsync();
        _systemName = L["identity.common.defaultSystemName"];
        // 未安装（未配置数据）：登录页不允许访问，强制重定向到初始化向导 /setup。
        // 与 MainLayout 的未安装守卫对称：手动输入 /sign-in 在未安装时也只能落到 /setup，
        // 不能显示登录表单。已安装、未登录时（status.Installed=true）则正常显示登录页。
        SetupStatus? status = null;
        try { status = await Api.GetSetupStatusAsync(); }
        catch { /* ApiHost 不可达时按已安装处理，不阻断登录页（避免误拦截） */ }

        if (status is { Installed: false, DevBypass: false })
        {
            // SSR 预渲染阶段 NavigateTo 以 NavigationException 作为重定向信号抛出（与 MainLayout 一致），
            // 交互阶段则触发客户端导航；两种情况都在此 return，避免继续渲染登录表单。
            Navigation.NavigateTo("/setup");
            return;
        }

        // Bluebird 的 return-url：登录成功后跳回原页面。
        var query = new Uri(Navigation.Uri).Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0] == "return-url")
                _returnUrl = Uri.UnescapeDataString(pair[1].Replace("+", " "));
        }
        // 仅允许站内跳转，杜绝开放重定向；跨站或非法 return-url 回落到首页。
        _returnUrl = NormalizeReturnUrl(_returnUrl, Navigation.BaseUri);

        // 站点配置（匿名阶段可能不可用，失败时静默，不影响登录）。
        try
        {
            var pub = await Api.GetPublicSystemSettingsAsync();
            if (!string.IsNullOrWhiteSpace(pub.SystemName))
                _systemName = pub.SystemName;
            _registerEnabled = pub.RegisterEnabled;
        }
        catch { /* 忽略：失败时保留默认值 */ }
    }

    private string TabClass(AccountTab tab) => _tab == tab ? "active" : string.Empty;

    /// <summary>BitPivot 切换：Key 为 AccountTab 枚举名。切换时清掉上一 tab 遗留的校验态。</summary>
    private void OnTabChange(BitPivotItem item)
    {
        if (Enum.TryParse<AccountTab>(item.Key, ignoreCase: true, out var tab))
            _tab = tab;
        _accountInvalid = false;
    }

    private async Task SignInAsync()
    {
        // 当前提交的账号：邮箱 tab 用邮箱文本；电话 tab 用 BitPhoneInput 的 E.164 完整号码。
        var account = _tab == AccountTab.Email ? _account.Trim() : _phoneNumber.Trim();
        _accountInvalid = _tab == AccountTab.Email
            ? string.IsNullOrWhiteSpace(account) || !account.Contains('@')
            : string.IsNullOrWhiteSpace(account) || account.Length <= 1; // E.164 至少 "+" + 区号
        _passwordInvalid = _password.Length < 6;
        if (_accountInvalid || _passwordInvalid)
        {
            _message = L["identity.signin.validation.required"];
            _messageIsError = true;
            return;
        }

        _busy = true;
        _message = null;
        try
        {
            // 真实登录：WebHost 提供的 blazorAuth.login 返回 Promise<{ok,error}>，
            // 内部调用 POST /auth/login 写入鉴权 cookie。
            var result = await JS.InvokeAsync<LoginResult>("blazorAuth.login", new
            {
                userName = account,
                password = _password,
                rememberMe = _remember,
                tenantId = (string?)null
            });

            if (result is { Ok: true })
            {
                // 登录成功：用全局 blazorAuth.redirect（window.location.assign）做整页硬刷新，
                // 让服务器重新读取认证 Cookie 完成 SSR 鉴权。该方式与账户菜单的退出登录、切换账号
                // 完全一致，在 Blazor Server 电路下可靠提交跳转（退出登录 → /sign-in 已验证可用）。
                // 注意：此前用 Navigation.NavigateTo(forceLoad:true) 在「退出后再登录」场景下跳转不提交，
                // 故改回经实测可靠的 window.location.assign 路径。
                var target = NormalizeReturnUrl(_returnUrl, Navigation.BaseUri) ?? "/";
                await JS.InvokeVoidAsync("blazorAuth.redirect", target);
                return;
            }

            _message = string.IsNullOrWhiteSpace(result?.Error)
                ? L["identity.signin.error.invalidCredentials"]
                : L.GetString("identity.signin.error.withReason", result!.Error);
            _messageIsError = true;
        }
        catch (JSException)
        {
            _message = L["identity.signin.error.serviceUnavailable"];
            _messageIsError = true;
        }
        catch (Exception)
        {
            _message = L["identity.signin.error.generic"];
            _messageIsError = true;
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>blazorAuth.login 的返回结果（camelCase JSON 由 JS interop 默认值反序列化）。</summary>
    public sealed class LoginResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
    }

    // 以下方式当前尚未配置：明确提示，不假装成功、不跳转。
    private void MagicLinkAsync() => NotifyUnavailable();
    private void ExternalGoogleAsync() => NotifyUnavailable();
    private void ExternalMicrosoftAsync() => NotifyUnavailable();
    private void ExternalGitHubAsync() => NotifyUnavailable();
    private void ExternalAppleAsync() => NotifyUnavailable();
    private void ForgotPassword()
    {
        var url = string.IsNullOrWhiteSpace(_returnUrl)
            ? "/forgot-password"
            : $"/forgot-password?return-url={Uri.EscapeDataString(_returnUrl)}";
        Navigation.NavigateTo(url);
    }
    private void SignUp()
    {
        var url = string.IsNullOrWhiteSpace(_returnUrl)
            ? "/sign-up"
            : $"/sign-up?return-url={Uri.EscapeDataString(_returnUrl)}";
        Navigation.NavigateTo(url);
    }
    private void Passkey() => NotifyUnavailable();

    private void NotifyUnavailable()
    {
        _accountInvalid = false;
        _passwordInvalid = false;
        _message = L["identity.signin.error.methodUnavailable"];
        _messageIsError = false;
    }

    /// <summary>
    /// 规范化登录成功后的回跳地址，杜绝开放重定向。
    /// 仅允许与当前站点同域的相对/绝对站内路径；跨站或非法地址回落到首页（返回 null）。
    /// </summary>
    private static string? NormalizeReturnUrl(string? returnUrl, string baseUri)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) return null;

        // 绝对 URL：必须同域，否则拒绝（防止 return-url 指向外部钓鱼站点）。
        if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var abs))
        {
            if (!Uri.TryCreate(baseUri, UriKind.Absolute, out var origin) || abs.Host != origin.Host)
                return null;
            return abs.PathAndQuery.StartsWith("/") ? abs.PathAndQuery : "/";
        }

        // 相对 URL：标准化为以 "/" 开头，避免 "//evil.com" 这类协议相对地址逃逸。
        if (Uri.TryCreate(returnUrl, UriKind.Relative, out var rel) && !rel.IsAbsoluteUri)
        {
            var raw = rel.OriginalString;
            return raw.StartsWith("/") ? raw : "/" + raw.TrimStart('/');
        }

        return null;
    }
}
