using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using BlazorBoilerplate.Modules.Cms.Frontend.Services;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Pages;

public partial class CmsLayouts
{
    [Inject]
    private CmsApiClient CmsApi { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private FrontendLocalizationService FrontendLoc { get; set; } = default!;

    private List<CmsLayoutDto> _layouts = [];
    private Guid? _editingId;
    private bool _editingBuiltIn;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private List<CmsLayoutRow> _rows = [];
    private bool _saving;

    protected override async Task OnInitializedAsync()
    {
        await FrontendLoc.EnsureLoadedAsync();
        await LoadAsync();
        if (_layouts.Count > 0)
            SelectLayout(_layouts[0]);
        else
            NewLayout();
    }

    private async Task LoadAsync()
    {
        _layouts = await CmsApi.ListLayoutsAsync() ?? [];
    }

    private void SelectLayout(CmsLayoutDto layout)
    {
        _editingId = layout.Id;
        _editingBuiltIn = layout.IsBuiltIn;
        _name = layout.Name;
        _description = layout.Description ?? string.Empty;
        _rows = layout.Rows.Select(r => new CmsLayoutRow(
            r.Id, r.CssClass,
            r.Columns.Select(c => new CmsLayoutColumn(c.ZoneId, c.Label, c.Span, c.CssClass)).ToList())).ToList();
    }

    private void NewLayout()
    {
        _editingId = null;
        _editingBuiltIn = false;
        _name = "自定义布局";
        _description = string.Empty;
        _rows = [new("row-1", null, [new("main", "主内容区", 12)])];
    }

    private void AddPresetRow(params int[] spans)
    {
        var rowNum = _rows.Count + 1;
        var cols = spans.Select((span, idx) =>
            new CmsLayoutColumn($"r{rowNum}-col{idx + 1}", $"区域 {idx + 1}", span)).ToList();
        _rows.Add(new($"row-{rowNum}", null, cols));
    }

    private void AddColumn(int rowIndex)
    {
        var row = _rows[rowIndex];
        row.Columns.Add(new($"zone-{row.Columns.Count + 1}", $"区域 {row.Columns.Count + 1}", 6));
    }

    private void RemoveRow(int rowIndex) => _rows.RemoveAt(rowIndex);

    private void RemoveColumn(int rowIndex, int colIndex) => _rows[rowIndex].Columns.RemoveAt(colIndex);

    private void UpdateColumn(int rowIndex, int colIndex, string? zoneId = null, string? label = null)
    {
        var col = _rows[rowIndex].Columns[colIndex];
        _rows[rowIndex].Columns[colIndex] = col with
        {
            ZoneId = string.IsNullOrWhiteSpace(zoneId) ? col.ZoneId : zoneId.Trim(),
            Label = string.IsNullOrWhiteSpace(label) ? col.Label : label.Trim()
        };
    }

    private void UpdateColumnSpan(int rowIndex, int colIndex, string? raw)
    {
        if (int.TryParse(raw, out var span))
        {
            var clamped = Math.Clamp(span, 1, 12);
            var col = _rows[rowIndex].Columns[colIndex];
            _rows[rowIndex].Columns[colIndex] = col with { Span = clamped };
        }
    }

    private async Task SaveLayoutAsync()
    {
        if (string.IsNullOrWhiteSpace(_name) || _rows.Count == 0)
        {
            Toast.Warning("请填写布局名称并至少保留一行。");
            return;
        }
        _saving = true;
        try
        {
            var req = new UpsertCmsLayoutRequest(_name, _description, _rows);
            var saved = _editingId.HasValue
                ? await CmsApi.UpdateLayoutAsync(_editingId.Value, req)
                : await CmsApi.CreateLayoutAsync(req);
            await LoadAsync();
            SelectLayout(saved);
            Toast.Success("布局已保存");
        }
        catch (Exception ex)
        {
            Toast.Error($"保存失败：{ex.Message}");
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task DeleteLayoutAsync()
    {
        if (!_editingId.HasValue) return;
        try
        {
            await CmsApi.DeleteLayoutAsync(_editingId.Value);
            await LoadAsync();
            if (_layouts.Count > 0) SelectLayout(_layouts[0]);
            else NewLayout();
            Toast.Success("布局已删除");
        }
        catch (Exception ex)
        {
            Toast.Error($"删除失败：{ex.Message}");
        }
    }
}
