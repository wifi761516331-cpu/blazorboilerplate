using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Shared.Features.Identity;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlazorBoilerplate.Modules.Identity.Frontend.Pages;

public partial class SignUp
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private FrontendLocalizationService FrontendLoc { get; set; } = default!;

    private string _userName = string.Empty;
    private string _email = string.Empty;
    private string _verificationCode = string.Empty;
    private string _displayName = string.Empty;
    private string _password = string.Empty;
    private string _confirmPassword = string.Empty;
    private bool _revealPassword;
    private bool _revealConfirm;
    private bool _busy;
    private bool _sendingCode;
    private int _cooldownSeconds;
    private CancellationTokenSource? _cooldownCts;
    private bool _userNameInvalid;
    private bool _emailInvalid;
    private bool _codeInvalid;
    private bool _passwordInvalid;
    private bool _confirmInvalid;
    private string? _message;
    private bool _messageIsError;
    private string? _returnUrl;
    private string _systemName = "BlazorBoilerplate";
    private bool _registerEnabled = true;
    private int _passwordMinLength = 6;

    protected override async Task OnInitializedAsync()
    {
        await FrontendLoc.EnsureLoadedAsync();
        _systemName = L["identity.common.defaultSystemName"];
        SetupStatus? status = null;
        try { status = await Api.GetSetupStatusAsync(); }
        catch { /* ApiHost 不可达时不阻断页面 */ }

        if (status is { Installed: false, DevBypass: false })
        {
            Navigation.NavigateTo("/setup");
            return;
        }

        var query = new Uri(Navigation.Uri).Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0] == "return-url")
                _returnUrl = Uri.UnescapeDataString(pair[1].Replace("+", " "));
        }
        _returnUrl = NormalizeReturnUrl(_returnUrl, Navigation.BaseUri);

        try
        {
            var pub = await Api.GetPublicSystemSettingsAsync();
            if (!string.IsNullOrWhiteSpace(pub.SystemName))
                _systemName = pub.SystemName;
            _registerEnabled = pub.RegisterEnabled;
            if (pub.PasswordMinLength >= 6)
                _passwordMinLength = pub.PasswordMinLength;
        }
        catch { /* 忽略：失败时保留默认值 */ }
    }

    private async Task SendCodeAsync()
    {
        var email = _email.Trim();
        _emailInvalid = string.IsNullOrWhiteSpace(email) || !email.Contains('@');
        if (_emailInvalid)
        {
            _message = L["identity.signup.error.invalidEmail"];
            _messageIsError = true;
            return;
        }

        _sendingCode = true;
        _message = null;
        try
        {
            await Api.SendEmailCodeAsync(new SendEmailCodeRequestDto(email, "register"));
            _message = L["identity.signup.codeSent"];
            _messageIsError = false;
            StartCooldown(60);
        }
        catch (Exception ex)
        {
            _message = ex.Message;
            _messageIsError = true;
        }
        finally
        {
            _sendingCode = false;
        }
    }

    private async Task RegisterAsync()
    {
        var userName = _userName.Trim();
        var email = _email.Trim();
        var code = _verificationCode.Trim();
        _userNameInvalid = string.IsNullOrWhiteSpace(userName) || userName.Length < 2;
        _emailInvalid = string.IsNullOrWhiteSpace(email) || !email.Contains('@');
        _codeInvalid = string.IsNullOrWhiteSpace(code) || code.Length < 6;
        _passwordInvalid = _password.Length < _passwordMinLength;
        _confirmInvalid = !string.Equals(_password, _confirmPassword, StringComparison.Ordinal);

        if (_userNameInvalid || _emailInvalid || _codeInvalid || _passwordInvalid || _confirmInvalid)
        {
            _message = L["identity.signup.error.checkForm"];
            _messageIsError = true;
            return;
        }

        _busy = true;
        _message = null;
        try
        {
            var result = await JS.InvokeAsync<SignIn.LoginResult>("blazorAuth.register", new
            {
                userName,
                email,
                password = _password,
                displayName = string.IsNullOrWhiteSpace(_displayName) ? null : _displayName.Trim(),
                verificationCode = code
            });

            if (result is { Ok: true })
            {
                var target = NormalizeReturnUrl(_returnUrl, Navigation.BaseUri) ?? "/";
                await JS.InvokeVoidAsync("blazorAuth.redirect", target);
                return;
            }

            _message = string.IsNullOrWhiteSpace(result?.Error)
                ? L["identity.signup.error.generic"]
                : L.GetString("identity.signup.error.withReason", result!.Error);
            _messageIsError = true;
        }
        catch (JSException)
        {
            _message = L["identity.signup.error.serviceUnavailable"];
            _messageIsError = true;
        }
        catch (Exception)
        {
            _message = L["identity.signup.error.generic"];
            _messageIsError = true;
        }
        finally
        {
            _busy = false;
        }
    }

    private void StartCooldown(int seconds)
    {
        _cooldownCts?.Cancel();
        _cooldownCts?.Dispose();
        _cooldownCts = new CancellationTokenSource();
        var ct = _cooldownCts.Token;
        _cooldownSeconds = seconds;
        _ = RunCooldownAsync(ct);
    }

    private async Task RunCooldownAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (_cooldownSeconds > 0 && await timer.WaitForNextTickAsync(ct))
            {
                _cooldownSeconds--;
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
            // 组件释放或重新发送时取消计时器
        }
    }

    private void GoToSignIn()
    {
        var url = string.IsNullOrWhiteSpace(_returnUrl)
            ? "/sign-in"
            : $"/sign-in?return-url={Uri.EscapeDataString(_returnUrl)}";
        Navigation.NavigateTo(url);
    }

    private void ToggleRevealPassword() => _revealPassword = !_revealPassword;
    private void ToggleRevealConfirm() => _revealConfirm = !_revealConfirm;

    private static string? NormalizeReturnUrl(string? returnUrl, string baseUri)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) return null;
        if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var abs))
        {
            if (Uri.TryCreate(baseUri, UriKind.Absolute, out var baseAbs) &&
                string.Equals(abs.Authority, baseAbs.Authority, StringComparison.OrdinalIgnoreCase))
                return abs.PathAndQuery;
            return null;
        }
        return returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") ? returnUrl : null;
    }

    public void Dispose()
    {
        _cooldownCts?.Cancel();
        _cooldownCts?.Dispose();
    }
}
