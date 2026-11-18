using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Web.Shared.Components;

// @implements IDisposable 留在 .razor；订阅生命周期与自动关闭逻辑在 .razor.cs。
public partial class BlazorBoilerplateToast
{
    [Inject]
    private ToastService Toast { get; set; } = default!;

    private bool _autoClosing;

    protected override void OnInitialized()
    {
        Toast.OnChange += HandleChange;
        _ = RunAutoCloseAsync(); // 历史遗留通知也纳入自动关闭
    }

    private void HandleChange()
    {
        InvokeAsync(StateHasChanged);
        _ = RunAutoCloseAsync();
    }

    private static TimeSpan Duration(ToastType t) => t switch
    {
        ToastType.Error => TimeSpan.FromSeconds(8),
        ToastType.Warning => TimeSpan.FromSeconds(6),
        _ => TimeSpan.FromSeconds(4)
    };

    /// <summary>单循环自动关闭：按最早到期的通知计算等待，避免多条通知并行定时器。</summary>
    private async Task RunAutoCloseAsync()
    {
        if (_autoClosing)
            return;
        _autoClosing = true;
        try
        {
            while (true)
            {
                var items = Toast.Items;
                if (items.Count == 0)
                    return;

                var now = DateTimeOffset.UtcNow;
                var expired = items.Where(i => now - i.CreatedAt >= Duration(i.Type)).ToList();
                if (expired.Count > 0)
                {
                    foreach (var item in expired)
                        Toast.Remove(item.Id);
                    continue;
                }

                var nextIn = items.Min(i => Duration(i.Type) - (now - i.CreatedAt));
                await Task.Delay(nextIn);
            }
        }
        finally
        {
            _autoClosing = false;
        }
    }

    public void Dispose() => Toast.OnChange -= HandleChange;

    private static string ToastClass(ToastType t) => t switch
    {
        ToastType.Success => "success",
        ToastType.Warning => "warning",
        ToastType.Error => "error",
        _ => "info"
    };

    private static string IconOf(ToastType t) => t switch
    {
        ToastType.Success => "Check",
        ToastType.Warning => "Warning",
        ToastType.Error => "Error",
        _ => "Info"
    };
}
