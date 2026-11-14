namespace BlazorBoilerplate.Web.Shared.Services;

/// <summary>全局通知类型。</summary>
public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>一条通知。</summary>
public sealed record ToastItem(Guid Id, string Message, ToastType Type, DateTimeOffset CreatedAt);

/// <summary>
/// 轻量级全局通知服务（scoped）。任意组件注入后调用 Show，BlazorBoilerplateToast 组件订阅并渲染。
/// 每个操作完成后都应给出一条通知，便于用户感知结果。
/// 注意：本服务只负责增删与事件通知；延迟自动关闭由 BlazorBoilerplateToast 组件在渲染同步上下文内完成
/// （定时回调若在线程池线程直接触发 StateHasChanged 会抛异常且被吞掉，导致通知永不关闭）。
/// </summary>
public sealed class ToastService
{
    private readonly object _gate = new();
    private readonly List<ToastItem> _items = [];

    public IReadOnlyList<ToastItem> Items
    {
        get
        {
            lock (_gate)
                return _items.ToArray();
        }
    }

    public event Action? OnChange;

    public void Show(string message, ToastType type = ToastType.Info)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        lock (_gate)
            _items.Add(new ToastItem(Guid.NewGuid(), message, type, DateTimeOffset.UtcNow));
        OnChange?.Invoke();
    }

    public void Success(string message) => Show(message, ToastType.Success);
    public void Info(string message) => Show(message, ToastType.Info);
    public void Warning(string message) => Show(message, ToastType.Warning);
    public void Error(string message) => Show(message, ToastType.Error);

    public void Remove(Guid id)
    {
        bool removed;
        lock (_gate)
            removed = _items.RemoveAll(i => i.Id == id) > 0;
        if (removed)
            OnChange?.Invoke();
    }
}
