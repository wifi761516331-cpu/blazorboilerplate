using BlazorBoilerplate.WebHost.Services;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.WebHost.Components.Shared;

// @implements IDisposable 留在 .razor；权限评估与订阅逻辑在 .razor.cs。
public partial class PermissionView
{
    [Inject]
    private AccountState Account { get; set; } = default!;

    [Parameter] public string Code { get; set; } = string.Empty;
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public RenderFragment? Fallback { get; set; }

    private bool _allowed;

    protected override void OnInitialized()
    {
        Account.Changed += OnAccountChanged;
    }

    protected override async Task OnParametersSetAsync()
    {
        await Account.EnsureLoadedAsync();
        _allowed = Account.HasPermission(Code);
    }

    private void OnAccountChanged()
    {
        _allowed = Account.HasPermission(Code);
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        Account.Changed -= OnAccountChanged;
    }
}
