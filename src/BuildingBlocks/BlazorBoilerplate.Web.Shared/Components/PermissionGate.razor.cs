using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Web.Shared.Components;

// @implements IDisposable 留在 .razor；权限评估与订阅逻辑在 .razor.cs。
public partial class PermissionGate
{
    [Inject]
    private ICurrentUserPermissionService Perms { get; set; } = default!;

    [Parameter] public string? Code { get; set; }
    [Parameter] public string[]? Codes { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public RenderFragment? Fallback { get; set; }

    private bool _allowed;

    protected override void OnInitialized() => Perms.Changed += OnPermissionsChanged;

    protected override async Task OnParametersSetAsync()
    {
        await Perms.EnsureLoadedAsync();
        Evaluate();
    }

    private void OnPermissionsChanged() => _ = InvokeAsync(() =>
    {
        Evaluate();
        StateHasChanged();
    });

    private void Evaluate()
    {
        if (Codes is { Length: > 0 })
            _allowed = Perms.HasAnyPermission(Codes);
        else if (!string.IsNullOrWhiteSpace(Code))
            _allowed = Perms.HasPermission(Code);
        else
            _allowed = Perms.IsAuthenticated;
    }

    public void Dispose() => Perms.Changed -= OnPermissionsChanged;
}
