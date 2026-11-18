using Bit.BlazorUI;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BlazorBoilerplate.WebHost.Components.Pages;

public partial class Setup
{
    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private ILogger<Setup> Logger { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private FrontendLocalizationService FrontendLoc { get; set; } = default!;

    private SetupStatus? _status;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string? _systemName;
    private string _provider = "PostgreSQL";
    private string? _postgreSql;
    private string? _mySql;
    private string? _mssql;
    private string? _sqlite;
    private string? _redis = "localhost:6379";
    private bool _submitting;
    private string? _error;

    private List<BitDropdownItem<string>> ProviderItems =>
    [
        new() { Text = L["platform.setup.provider.pg"], Value = "PostgreSQL" },
        new() { Text = "MySQL", Value = "MySQL" },
        new() { Text = "MSSQL (SQL Server)", Value = "MSSQL" },
        new() { Text = L["platform.setup.provider.sqlite"], Value = "SQLite" }
    ];

    protected override async Task OnInitializedAsync()
    {
        await FrontendLoc.EnsureLoadedAsync();
        // NavigateTo 置于 try/catch 之外：预渲染阶段 NavigationException 是重定向信号，不能被吞。
        SetupStatus? status;
        try
        {
            status = await Api.GetSetupStatusAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "读取安装状态失败");
            _error = L.GetString("platform.setup.error.apiHost", ex.Message);
            return;
        }

        _status = status;
        if (status.Installed)
            // 已安装时直接去登录页：跳 "/" 会先渲染后台首页，再被 MainLayout 的
            // 未登录守卫踢回 /sign-in，用户看到「先闪首页再退回登录页」。
            Navigation.NavigateTo("/sign-in");
    }

    private async Task SubmitAsync()
    {
        _submitting = true;
        _error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(_email))
            {
                _error = L["platform.setup.error.emailRequired"];
                return;
            }
            if (_password.Length < 6)
            {
                _error = L["platform.setup.error.passwordMin"];
                return;
            }

            var config = new SetupConfiguration(
                _email, _password, _systemName,
                _provider,
                _provider == "PostgreSQL" ? _postgreSql : null,
                _provider == "SQLite" ? _sqlite : null,
                _redis,
                _provider == "MySQL" ? _mySql : null,
                _provider == "MSSQL" ? _mssql : null);

            _status = await Api.CompleteSetupAsync(config);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "初始化提交失败");
            _error = ex.Message;
            return;
        }
        finally
        {
            _submitting = false;
        }

        if (!_status.Installed)
        {
            _error = L["platform.setup.error.incomplete"];
            return;
        }

        // 初始化完成后直接用向导里填写的管理员账号登录，再整页跳首页。
        // 旧逻辑 NavigateTo("/") 时浏览器还没有认证 Cookie，首页必然被 MainLayout
        // 的未登录守卫踢回 /sign-in?return-url=...，形成「先闪后台首页再退回登录页」。
        // SetupInitializer 创建的管理员 UserName 固定为 "admin"（邮箱是另一个字段）。
        try
        {
            var result = await JS.InvokeAsync<LoginResult>("blazorAuth.login", new
            {
                userName = "admin",
                password = _password,
                rememberMe = true,
                tenantId = (string?)null
            });

            if (result is { Ok: true })
            {
                await JS.InvokeVoidAsync("blazorAuth.redirect", "/");
                return;
            }
        }
        catch
        {
            // 自动登录失败不阻断安装结果，回落到登录页让用户手动登录。
        }

        Navigation.NavigateTo("/sign-in");
    }

    /// <summary>blazorAuth.login 的返回结果（camelCase JSON 由 JS interop 默认值反序列化）。</summary>
    private sealed class LoginResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
    }
}
