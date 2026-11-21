using System.Globalization;
using System.Net.Http.Json;
using Bit.BlazorUI;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Application.OData;
using BlazorBoilerplate.Application.Platform;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.OData;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.WebHost.Components.Pages;

public partial class OperationsCenter
{
    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] private ILocalizationService L { get; set; } = default!;
    [Inject] private ToastService Toast { get; set; } = default!;

    public sealed record ScheduleTaskDescriptor(
        string Id,
        string Name,
        string ModuleId,
        int Seconds,
        bool Enabled,
        bool StopOnError,
        bool IsRunning,
        DateTimeOffset? LastStartUtc,
        DateTimeOffset? LastEndUtc,
        DateTimeOffset? LastSuccessUtc,
        string? LastError);

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

    private static readonly ODataFieldMap EmailFieldMap = new(["to", "subject", "priority", "senttries", "createdonutc", "sentonutc"]);
    private static readonly ODataFieldMap LogFieldMap = new(["createdatutc", "systemkeyword", "comment", "username", "entityname"]);

    private List<ScheduleTaskDescriptor> _tasks = [];
    private int _emailGridVersion;
    private bool _pendingOnlyEmails;
    private int _logGridVersion;
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
        Toast.Success(L["platform.ops.tasks.toast.updated"]);
    });

    private async Task RunTaskNowAsync(ScheduleTaskDescriptor task) => await RunAsync(async () =>
    {
        var resp = await Http.PostAsync($"api/v1/PlatformExtensions/RunScheduleTask?taskId={Uri.EscapeDataString(task.Id)}", null);
        resp.EnsureSuccessStatusCode();
        await LoadTasksAsync();
        Toast.Success(L.GetString("platform.ops.tasks.toast.executed", task.Name));
    });

    private Task LoadEmailsAsync()
    {
        _emailGridVersion++;
        return Task.CompletedTask;
    }

    private async Task<BitDataGridReadResult<QueuedEmailItem>> ReadEmailsAsync(BitDataGridReadRequest req)
    {
        var sorts = req.Sorts.Count > 0
            ? req.Sorts.Select(s => new ODataSort(s.ColumnId, s.Direction == BitDataGridSortDirection.Descending)).ToList()
            : [new ODataSort("createdonutc", true)];
        var filters = BuildODataFilters(req.Filters);

        try
        {
            _error = null;
            var endpoint = _pendingOnlyEmails
                ? "api/v1/PlatformExtensions/QueryQueuedEmailsOData?pendingOnly=true"
                : "api/v1/PlatformExtensions/QueryQueuedEmailsOData";
            var source = new ODataServerSource<QueuedEmailItem>(Http, endpoint, EmailFieldMap);
            var page = await source.LoadAsync(new ODataServerRequest(req.Skip, req.Take, sorts, filters, null));
            return new BitDataGridReadResult<QueuedEmailItem>(page.Items, (int)page.TotalCount);
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            Toast.Error(ex.Message);
            return new BitDataGridReadResult<QueuedEmailItem>([], 0);
        }
    }

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
        Toast.Success(L.GetString("platform.ops.emails.toast.processed", count));
    });

    private async Task RequeueEmailAsync(QueuedEmailItem email) => await RunAsync(async () =>
    {
        var resp = await Http.PostAsync($"api/v1/PlatformExtensions/RequeueEmail?id={email.Id}", null);
        resp.EnsureSuccessStatusCode();
        await LoadEmailsAsync();
        Toast.Success(L["platform.ops.emails.toast.requeued"]);
    });

    private Task LoadLogsAsync()
    {
        _logGridVersion++;
        return Task.CompletedTask;
    }

    private async Task<BitDataGridReadResult<ActivityLogEntry>> ReadLogsAsync(BitDataGridReadRequest req)
    {
        var sorts = req.Sorts.Count > 0
            ? req.Sorts.Select(s => new ODataSort(s.ColumnId, s.Direction == BitDataGridSortDirection.Descending)).ToList()
            : [new ODataSort("createdatutc", true)];
        var filters = BuildODataFilters(req.Filters);

        try
        {
            _error = null;
            var endpoint = string.IsNullOrWhiteSpace(_logKeyword)
                ? "api/v1/PlatformExtensions/QueryActivityLogsOData"
                : $"api/v1/PlatformExtensions/QueryActivityLogsOData?keyword={Uri.EscapeDataString(_logKeyword)}";
            var source = new ODataServerSource<ActivityLogEntry>(Http, endpoint, LogFieldMap);
            var page = await source.LoadAsync(new ODataServerRequest(req.Skip, req.Take, sorts, filters, null));
            return new BitDataGridReadResult<ActivityLogEntry>(page.Items, (int)page.TotalCount);
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            Toast.Error(ex.Message);
            return new BitDataGridReadResult<ActivityLogEntry>([], 0);
        }
    }

    private static List<ODataFilter> BuildODataFilters(IEnumerable<BitDataGridFilterDescriptor> descriptors)
    {
        var list = new List<ODataFilter>();
        foreach (var filter in descriptors)
        {
            if (filter.Value is null) continue;
            var op = filter.Operator switch
            {
                BitDataGridFilterOperator.Equals => "eq",
                BitDataGridFilterOperator.NotEquals => "ne",
                BitDataGridFilterOperator.GreaterThan => "gt",
                BitDataGridFilterOperator.GreaterThanOrEqual => "ge",
                BitDataGridFilterOperator.LessThan => "lt",
                BitDataGridFilterOperator.LessThanOrEqual => "le",
                _ => "contains"
            };
            var value = Convert.ToString(filter.Value, CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(value))
                list.Add(new ODataFilter(filter.ColumnId, op, value));
        }
        return list;
    }

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
        Toast.Success(L["platform.ops.templates.toast.saved"]);
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
        Toast.Success(L.GetString(enabled ? "platform.ops.providers.toast.enabled" : "platform.ops.providers.toast.disabled", provider.FriendlyName));
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
