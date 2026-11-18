using BlazorBoilerplate.Application.Localization;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Web.Shared.Components;

// @inject 已由 [Inject] 取代；接口/基类声明留在 .razor。
public partial class BlazorBoilerplateModal
{
    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback<bool> IsOpenChanged { get; set; }
    [Parameter] public string? Title { get; set; }
    [Parameter] public string? Message { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    // EventCallback（而非 Func<Task>）：父组件的方法经由其 IHandleEvent 管线执行，
    // 完成后父组件自动 StateHasChanged —— 绑定的 IsOpen=false 才能传导到本组件并关闭弹框。
    [Parameter] public EventCallback OnOk { get; set; }
    [Parameter] public string? OkText { get; set; }
    [Parameter] public string? CancelText { get; set; }
    [Parameter] public bool ShowCancel { get; set; } = true;
    [Parameter] public bool ShowClose { get; set; } = true;
    [Parameter] public bool Danger { get; set; }
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public bool CloseOnBackdrop { get; set; } = true;
    [Parameter] public string? ModalClass { get; set; }

    private bool _busy;

    private async Task HandleOk()
    {
        if (!OnOk.HasDelegate)
        {
            await CloseAsync();
            return;
        }
        _busy = true;
        try
        {
            await OnOk.InvokeAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    private Task Cancel() => CloseAsync();

    private void OnBackdropClick()
    {
        if (CloseOnBackdrop)
            _ = CloseAsync();
    }

    private async Task CloseAsync()
    {
        IsOpen = false;
        await IsOpenChanged.InvokeAsync(false);
    }
}
