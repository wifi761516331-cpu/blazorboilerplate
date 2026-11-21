using System.Globalization;
using Bit.BlazorUI;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Application.OData;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.OData;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace BlazorBoilerplate.Modules.Demo.Pages;

public partial class Demo
{
    [Inject] private IServiceProvider Services { get; set; } = default!;
    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] private ILocalizationService L { get; set; } = default!;
    [Inject] private FrontendLocalizationService FrontendLoc { get; set; } = default!;
    [Inject] private ToastService Toast { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private static readonly ODataFieldMap NoteFieldMap = new(["id", "title", "createdatutc"]);

    private DemoClientService Client => Services.GetService<DemoClientService>() ?? new DemoClientService(Http);

    private int _gridVersion;
    private ODataServerSource<DemoNoteDto> _notesSource = default!;
    private BitDataGridStrings _gridStrings = new();
    private DemoOverviewModel? _overview;
    private DemoShowcaseResultModel? _result;
    private string? _error;
    private bool _busy;

    protected override async Task OnInitializedAsync()
    {
        await FrontendLoc.EnsureLoadedAsync();
        _notesSource = new ODataServerSource<DemoNoteDto>(Http, "api/demo/notes", NoteFieldMap);
        _gridStrings = L.CreateGridStrings();
        await LoadOverviewAsync();
    }

    private async Task<BitDataGridReadResult<DemoNoteDto>> ReadNotesAsync(BitDataGridReadRequest req)
    {
        var sorts = req.Sorts.Count > 0
            ? req.Sorts.Select(s => new ODataSort(s.ColumnId, s.Direction == BitDataGridSortDirection.Descending)).ToList()
            : [new ODataSort("createdatutc", true)];

        var filters = req.Filters
            .Where(f => f.Value is not null)
            .Select(f => new ODataFilter(f.ColumnId, "contains", Convert.ToString(f.Value, CultureInfo.InvariantCulture) ?? string.Empty))
            .Where(f => !string.IsNullOrWhiteSpace(f.Value))
            .ToList();

        try
        {
            _error = null;
            var page = await _notesSource.LoadAsync(new ODataServerRequest(req.Skip, req.Take, sorts, filters, null));
            return new BitDataGridReadResult<DemoNoteDto>(page.Items, (int)page.TotalCount);
        }
        catch (Exception ex)
        {
            _error = L.GetString("demo.toast.loadFailed", ex.Message);
            return new BitDataGridReadResult<DemoNoteDto>([], 0);
        }
    }

    private async Task LoadOverviewAsync()
    {
        _error = null;
        try
        {
            _overview = await Client.GetOverviewAsync();
        }
        catch (Exception ex)
        {
            _error = L.GetString("demo.toast.loadFailed", ex.Message);
        }
    }

    private async Task RunShowcaseAsync()
    {
        _busy = true;
        _error = null;
        try
        {
            _result = await Client.RunShowcaseAsync();
            await LoadOverviewAsync();
            _gridVersion++;
            Toast.Success(L["demo.toast.runSuccess"]);
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            Toast.Error(L.GetString("demo.toast.runFailed", ex.Message));
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task SaveSettingsAsync()
    {
        if (_overview is null) return;
        _busy = true;
        try
        {
            await Client.SaveSettingsAsync(_overview.Settings);
            await LoadOverviewAsync();
            Toast.Success(L["demo.toast.settingsSaved"]);
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("demo.toast.settingsSaveFailed", ex.Message));
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ExportCsvAsync()
    {
        try
        {
            await using var stream = await Http.GetStreamAsync("api/demo/export");
            using var streamRef = new DotNetStreamReference(stream: stream, leaveOpen: false);
            await JS.InvokeVoidAsync("blazorDownloadFile", "demo-export.csv", "text/csv; charset=utf-8", streamRef);
            Toast.Success(L["demo.toast.exportStarted"]);
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("demo.toast.exportFailed", ex.Message));
        }
    }
}
