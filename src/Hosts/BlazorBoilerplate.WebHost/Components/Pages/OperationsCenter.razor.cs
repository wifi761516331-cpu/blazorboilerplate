using System.Net.Http.Json;
using Bit.BlazorUI;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Application.Platform;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.WebHost.Components.Pages;

public partial class OperationsCenter
{
    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] private ILocalizationService L { get; set; } = default!;
    [Inject] private ToastService Toast { get; set; } = default!;

    public sealed class ProviderItemModel
    {
        public string SystemName { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public string ModuleId { get; set; } = string.Empty;
        public int Order { get; set; }
        public bool Enabled { get; set; }
    }

    private string _activeTab = "tasks";
    private bool _busy;
    private string? _error;
    private BitDataGridStrings _gridStrings = new();

    private List<ScheduleTaskDescriptor> _tasks = [];
    private List<QueuedEmailItem> _emails = [];
    private bool _pendingOnlyEmails;
    private List<ActivityLogEntry> _logs = [];
    private string _logKeyword = string.Empty;
    private List<MessageTemplateItem> _templates = [];
    private List<ProviderItemModel> _providers = [];

    private bool _tplModalOpen;
    private string _editingTplName = string.Empty;
    private string _editingTplSubject = string.Empty;
    private string _editingTplBody = string.Empty;
    private bool _editingTplActive = true;

    protected override async Task OnInitializedAsync()
    {
        _gridStrings = L.CreateGridStrings();
        await LoadTasksAsync();
    }

    private async Task OnTabChangedAsync(string key)
    {
        _activeTab = key;
        switch (key)
        {
            case "tasks": await LoadTasksAsync(); break;
            case "emails": await LoadEmailsAsync(); break;
            case "logs": await LoadLogsAsync(); break;
            case "templates": await LoadTemplatesAsync(); break;
            case "providers": await LoadProvidersAsync(); break;
        }
    }

    private async Task LoadTasksAsync() => await RunAsync(async () =>
    {
        _tasks = await Http.GetFromJsonAsync<List<ScheduleTaskDescriptor>>("api/v1/PlatformExtensions/GetScheduleTasks") ?? [];
    });

    private async Task ToggleTaskAsync(ScheduleTaskDescriptor task, bool enabled) => await RunAsync(async () =>
    {
        var resp = await Http.PutAsJsonAsync("api/v1/PlatformExtensions/UpdateScheduleTask", new { taskId = task.Id, enabled });
        resp.EnsureSuccessStatusCode();
        await LoadTasksAsync();
        Toast.Success("任务状态已更新。");
    });

    private async Task RunTaskNowAsync(ScheduleTaskDescriptor task) => await RunAsync(async () =>
    {
        var resp = await Http.PostAsync($"api/v1/PlatformExtensions/RunScheduleTask?taskId={Uri.EscapeDataString(task.Id)}", null);
        resp.EnsureSuccessStatusCode();
        await LoadTasksAsync();
        Toast.Success($"任务「{task.Name}」已执行完成。");
    });

    private async Task LoadEmailsAsync() => await RunAsync(async () =>
    {
        var url = _pendingOnlyEmails
            ? "api/v1/PlatformExtensions/GetQueuedEmails?pendingOnly=true"
            : "api/v1/PlatformExtensions/GetQueuedEmails";
        _emails = await Http.GetFromJsonAsync<List<QueuedEmailItem>>(url) ?? [];
    });

    private async Task OnPendingEmailsChangedAsync(bool value)
    {
        _pendingOnlyEmails = value;
        await LoadEmailsAsync();
    }

    private async Task ProcessEmailQueueAsync() => await RunAsync(async () =>
    {
        var resp = await Http.PostAsync("api/v1/PlatformExtensions/ProcessEmailQueue", null);
        resp.EnsureSuccessStatusCode();
        var count = await resp.Content.ReadFromJsonAsync<int>();
        await LoadEmailsAsync();
        Toast.Success($"队列处理完成，本次成功发送 {count} 封邮件。");
    });

    private async Task RequeueEmailAsync(QueuedEmailItem email) => await RunAsync(async () =>
    {
        var resp = await Http.PostAsync($"api/v1/PlatformExtensions/RequeueEmail?id={email.Id}", null);
        resp.EnsureSuccessStatusCode();
        await LoadEmailsAsync();
        Toast.Success("已重置为待发送状态。");
    });

    private async Task LoadLogsAsync() => await RunAsync(async () =>
    {
        var url = string.IsNullOrWhiteSpace(_logKeyword)
            ? "api/v1/PlatformExtensions/GetActivityLogs"
            : $"api/v1/PlatformExtensions/GetActivityLogs?keyword={Uri.EscapeDataString(_logKeyword)}";
        _logs = await Http.GetFromJsonAsync<List<ActivityLogEntry>>(url) ?? [];
    });

    private async Task OnLogKeywordChangedAsync(string value)
    {
        _logKeyword = value;
        await LoadLogsAsync();
    }

    private async Task LoadTemplatesAsync() => await RunAsync(async () =>
    {
        _templates = await Http.GetFromJsonAsync<List<MessageTemplateItem>>("api/v1/PlatformExtensions/GetMessageTemplates") ?? [];
    });

    private void OpenEditTemplate(MessageTemplateItem tpl)
    {
        _editingTplName = tpl.Name;
        _editingTplSubject = tpl.Subject;
        _editingTplBody = tpl.Body;
        _editingTplActive = tpl.IsActive;
        _tplModalOpen = true;
    }

    private async Task SaveTemplateAsync() => await RunAsync(async () =>
    {
        var payload = new MessageTemplateItem(_editingTplName, _editingTplSubject, _editingTplBody, _editingTplActive, DateTimeOffset.UtcNow);
        var resp = await Http.PutAsJsonAsync("api/v1/PlatformExtensions/SaveMessageTemplate", payload);
        resp.EnsureSuccessStatusCode();
        _tplModalOpen = false;
        await LoadTemplatesAsync();
        Toast.Success("消息模板已保存。");
    });

    private async Task LoadProvidersAsync() => await RunAsync(async () =>
    {
        _providers = await Http.GetFromJsonAsync<List<ProviderItemModel>>("api/v1/PlatformExtensions/GetProviders") ?? [];
    });

    private async Task ToggleProviderAsync(ProviderItemModel provider, bool enabled) => await RunAsync(async () =>
    {
        var resp = await Http.PutAsJsonAsync("api/v1/PlatformExtensions/SetProviderEnabled", new { systemName = provider.SystemName, enabled });
        resp.EnsureSuccessStatusCode();
        provider.Enabled = enabled;
        Toast.Success($"策略「{provider.FriendlyName}」已{(enabled ? "启用" : "停用")}。");
    });

    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
        _error = null;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }
}
