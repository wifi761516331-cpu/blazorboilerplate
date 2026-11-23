using System.Text.Json;
using Bit.BlazorUI;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using BlazorBoilerplate.Modules.Cms.Frontend.Services;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Pages;

public partial class CmsDesigner
{
    [Parameter]
    public Guid SiteId { get; set; }

    [Inject]
    private CmsApiClient CmsApi { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private FrontendLocalizationService FrontendLoc { get; set; } = default!;

    private CmsSiteDesignDto? _design;
    private bool _loading = true;
    private bool _busy;
    private string _viewport = "desktop";
    private string _leftTab = "widgets";
    private string _widgetFilter = string.Empty;
    private string? _activeCulture;
    private bool _isLivePreview;

    private bool IsDefaultTemplate => _design is not null && CmsBuiltInTemplates.IsReadOnly(_design.Site.SiteKey);
    private bool EffectiveLivePreview => _isLivePreview || IsDefaultTemplate;

    private enum UndoKind
    {
        UpdatePropsOrStyle,
        DeleteCreatedWidget,
        RestoreDeletedWidget,
        RestoreMove
    }

    private sealed record UndoEntry(
        UndoKind Kind,
        Guid WidgetId,
        string Title,
        string PropsJson,
        string? StyleJson,
        Guid SiteId = default,
        Guid SectionId = default,
        string ZoneId = "",
        string WidgetType = "",
        int SortOrder = 0);

    private readonly List<UndoEntry> _undoStack = [];

    private Guid? _activeSectionId;
    private CmsWidgetDto? _selectedWidget;
    private string _editWidgetTitle = string.Empty;
    private string _editWidgetPropsJson = "{}";
    private string _editWidgetStyleJson = string.Empty;
    private string? _replacingImageUrl;
    private bool _pickingSiteLogo;
    private bool _editingSiteTitle;
    private string _editSiteTitleValue = string.Empty;
    private Guid? _editingNavSectionId;
    private string _editNavSectionTitle = string.Empty;
    private bool _editingNavbar;
    private string _navSiteTitle = string.Empty;
    private string? _navSiteLogoUrl;
    private readonly Dictionary<Guid, string> _navSectionTitles = [];
    private readonly Dictionary<Guid, bool> _navSectionVisible = [];

    private string? _draggingNewWidgetType;
    private Guid? _draggingExistingWidgetId;

    private string _themePrimary = "#0f6cbd";
    private string _themeAccent = "#115ea3";
    private string _themeBg = "#ffffff";
    private string _themeSurface = "#f5f7fa";
    private string _themeText = "#1f2328";
    private string _themeRadius = "8px";

    private bool _addSectionOpen;
    private string _newSecTitle = string.Empty;
    private string _newSecSlug = string.Empty;
    private string? _newSecLayoutId;
    private bool _newSecNavVisible = true;
    private List<BitDropdownItem<string>> _layoutDropdownItems = [];

    private bool _aiOpen;
    private bool _aiBusy;
    private string _aiBrand = string.Empty;
    private string _aiIndustry = string.Empty;
    private string _aiPrompt = string.Empty;
    private string _aiColor = "#0f6cbd";

    private bool _mediaOpen;

    private string ThemeVariablesStyle =>
        $"--cms-pri:{_themePrimary};--cms-acc:{_themeAccent};--cms-bg:{_themeBg};--cms-surf:{_themeSurface};--cms-fg:{_themeText};--cms-radius:{_themeRadius};";

    protected override async Task OnInitializedAsync()
    {
        await FrontendLoc.EnsureLoadedAsync();
        await LoadDesignAsync();
    }

    private async Task LoadDesignAsync()
    {
        _loading = true;
        try
        {
            _design = await CmsApi.GetDesignAsync(SiteId, _activeCulture);
            if (_design is not null)
            {
                _activeCulture = _design.ActiveCulture;
                var t = _design.Site.Theme;
                _themePrimary = t.PrimaryColor;
                _themeAccent = t.AccentColor;
                _themeBg = t.BackgroundColor;
                _themeSurface = t.SurfaceColor;
                _themeText = t.TextColor;
                _themeRadius = t.BorderRadius;

                _layoutDropdownItems = _design.Layouts
                    .Select(l => new BitDropdownItem<string> { Text = l.Name, Value = l.Id.ToString("D") })
                    .ToList();
                _newSecLayoutId ??= _layoutDropdownItems.FirstOrDefault()?.Value;

                if (_activeSectionId is null || _design.Sections.All(s => s.Id != _activeSectionId))
                    _activeSectionId = _design.Sections.FirstOrDefault()?.Id;
            }
        }
        catch (Exception ex)
        {
            Toast.Error($"加载设计器失败：{ex.Message}");
        }
        finally
        {
            _loading = false;
        }
    }

    private CmsPublishedSection ToPublishedSection(CmsSectionDto sec)
    {
        var layout = _design!.Layouts.FirstOrDefault(l => l.Id == sec.LayoutId) ?? _design.Layouts.FirstOrDefault();
        var rows = layout?.Rows ?? [new("row-1", null, [new("main", "主内容区", 12)])];
        var widgets = _design.Widgets
            .Where(w => w.SectionId == sec.Id && !w.IsTemplate)
            .OrderBy(w => w.SortOrder)
            .Select(w => new CmsPublishedWidget(w.Id, w.ZoneId, w.WidgetType, w.Title, w.PropsJson, w.StyleJson, w.SortOrder))
            .ToList();

        return new CmsPublishedSection(sec.Id, sec.Title, sec.AnchorSlug, sec.NavVisible, sec.SortOrder, sec.BackgroundStyle, rows, layout?.CustomCss, widgets);
    }

    private void SelectSection(CmsSectionDto sec) => _activeSectionId = sec.Id;

    private void SelectWidget(Guid widgetId)
    {
        var w = _design?.Widgets.FirstOrDefault(x => x.Id == widgetId);
        if (w is null) return;
        _selectedWidget = w;
        _activeSectionId = w.SectionId;
        _editWidgetTitle = w.Title;
        _editWidgetPropsJson = FormatReadableJson(w.PropsJson);
        _editWidgetStyleJson = string.IsNullOrWhiteSpace(w.StyleJson) ? string.Empty : FormatReadableJson(w.StyleJson);
    }

    private static string FormatReadableJson(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions(CmsJson.Options) { WriteIndented = true });
        }
        catch
        {
            return raw;
        }
    }

    private void ClearWidgetSelection()
    {
        _selectedWidget = null;
        _editingNavbar = false;
    }

    private void StartDragNewWidget(string widgetType)
    {
        _draggingNewWidgetType = widgetType;
        _draggingExistingWidgetId = null;
    }

    private void StartDragExistingWidget(Guid widgetId)
    {
        _draggingExistingWidgetId = widgetId;
        _draggingNewWidgetType = null;
    }

    private async Task HandleDropToZoneAsync((Guid SectionId, string ZoneId) target)
    {
        if (_design is null) return;
        if (!string.IsNullOrWhiteSpace(_draggingNewWidgetType))
        {
            var def = CmsWidgetCatalog.Find(_draggingNewWidgetType);
            _draggingNewWidgetType = null;
            if (def is not null)
                await InsertWidgetAsync(target.SectionId, target.ZoneId, def);
        }
        else if (_draggingExistingWidgetId.HasValue)
        {
            var id = _draggingExistingWidgetId.Value;
            _draggingExistingWidgetId = null;
            var moving = _design.Widgets.FirstOrDefault(w => w.Id == id);
            if (moving is not null)
                PushUndoEntry(new UndoEntry(UndoKind.RestoreMove, moving.Id, moving.Title, moving.PropsJson, moving.StyleJson, moving.SiteId, moving.SectionId, moving.ZoneId, moving.WidgetType, moving.SortOrder));
            var nextOrder = _design.Widgets.Count(w => w.SectionId == target.SectionId && w.ZoneId == target.ZoneId);
            try
            {
                await CmsApi.MoveWidgetAsync(id, new(target.SectionId, target.ZoneId, nextOrder));
                await LoadDesignAsync();
                SelectWidget(id);
            }
            catch (Exception ex)
            {
                Toast.Error($"移动失败：{ex.Message}");
            }
        }
    }

    private async Task HandleDropOnWidgetAsync((Guid SectionId, string ZoneId, Guid TargetWidgetId) target)
    {
        if (_design is null) return;
        if (!string.IsNullOrWhiteSpace(_draggingNewWidgetType))
        {
            var def = CmsWidgetCatalog.Find(_draggingNewWidgetType);
            _draggingNewWidgetType = null;
            if (def is not null)
                await InsertWidgetAsync(target.SectionId, target.ZoneId, def);
            return;
        }
        if (!_draggingExistingWidgetId.HasValue || _draggingExistingWidgetId.Value == target.TargetWidgetId)
        {
            _draggingExistingWidgetId = null;
            return;
        }

        var movingId = _draggingExistingWidgetId.Value;
        _draggingExistingWidgetId = null;
        var zoneList = _design.Widgets
            .Where(w => w.SectionId == target.SectionId && string.Equals(w.ZoneId, target.ZoneId, StringComparison.OrdinalIgnoreCase) && w.Id != movingId)
            .OrderBy(w => w.SortOrder)
            .ToList();
        var insertIdx = zoneList.FindIndex(w => w.Id == target.TargetWidgetId);
        if (insertIdx < 0) insertIdx = zoneList.Count;

        var moving = _design.Widgets.FirstOrDefault(w => w.Id == movingId);
        if (moving is null) return;

        PushUndoEntry(new UndoEntry(UndoKind.RestoreMove, moving.Id, moving.Title, moving.PropsJson, moving.StyleJson, moving.SiteId, moving.SectionId, moving.ZoneId, moving.WidgetType, moving.SortOrder));
        zoneList.Insert(insertIdx, moving);
        try
        {
            for (var i = 0; i < zoneList.Count; i++)
                await CmsApi.MoveWidgetAsync(zoneList[i].Id, new(target.SectionId, target.ZoneId, i));

            await LoadDesignAsync();
            SelectWidget(movingId);
        }
        catch (Exception ex)
        {
            Toast.Error($"排序失败：{ex.Message}");
        }
    }

    private async Task MoveWidgetOrderAsync((Guid WidgetId, int Delta) args)
    {
        if (_design is null) return;
        var current = _design.Widgets.FirstOrDefault(w => w.Id == args.WidgetId);
        if (current is null) return;

        var zoneList = _design.Widgets
            .Where(w => w.SectionId == current.SectionId && string.Equals(w.ZoneId, current.ZoneId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => w.SortOrder)
            .ToList();
        var idx = zoneList.FindIndex(w => w.Id == current.Id);
        var nextIdx = idx + args.Delta;
        if (idx < 0 || nextIdx < 0 || nextIdx >= zoneList.Count) return;

        PushUndoEntry(new UndoEntry(UndoKind.RestoreMove, current.Id, current.Title, current.PropsJson, current.StyleJson, current.SiteId, current.SectionId, current.ZoneId, current.WidgetType, current.SortOrder));
        (zoneList[idx], zoneList[nextIdx]) = (zoneList[nextIdx], zoneList[idx]);
        try
        {
            for (var i = 0; i < zoneList.Count; i++)
                await CmsApi.MoveWidgetAsync(zoneList[i].Id, new(current.SectionId, current.ZoneId, i));

            await LoadDesignAsync();
            SelectWidget(current.Id);
        }
        catch (Exception ex)
        {
            Toast.Error($"移动失败：{ex.Message}");
        }
    }

    private async Task QuickAddWidgetAsync(CmsWidgetDefinition def)
    {
        if (_design is null || !_activeSectionId.HasValue)
        {
            Toast.Warning("请先新增或选择一个页面区块。");
            return;
        }
        var sec = _design.Sections.FirstOrDefault(s => s.Id == _activeSectionId.Value);
        var layout = _design.Layouts.FirstOrDefault(l => l.Id == sec?.LayoutId);
        var firstZone = layout?.Rows.FirstOrDefault()?.Columns.FirstOrDefault()?.ZoneId ?? "main";
        await InsertWidgetAsync(_activeSectionId.Value, firstZone, def);
    }

    private async Task InsertWidgetAsync(Guid sectionId, string zoneId, CmsWidgetDefinition def)
    {
        if (_design is null) return;
        try
        {
            var nextOrder = _design.Widgets.Count(w => w.SectionId == sectionId && w.ZoneId == zoneId);
            var created = await CmsApi.CreateWidgetAsync(new(
                _design.Site.Id, sectionId, zoneId, def.Type, def.Name, def.DefaultPropsJson, null, nextOrder, _activeCulture));
            PushUndoEntry(new UndoEntry(UndoKind.DeleteCreatedWidget, created.Id, created.Title, created.PropsJson, created.StyleJson));
            await LoadDesignAsync();
            SelectWidget(created.Id);
            Toast.Success($"已添加 {def.Name}");
        }
        catch (Exception ex)
        {
            Toast.Error($"添加微件失败：{ex.Message}");
        }
    }

    private async Task SaveWidgetAsync()
    {
        if (_selectedWidget is null) return;
        try
        {
            using var _ = JsonDocument.Parse(string.IsNullOrWhiteSpace(_editWidgetPropsJson) ? "{}" : _editWidgetPropsJson);
        }
        catch
        {
            Toast.Error("属性 JSON 格式无效，请检查后再保存");
            return;
        }

        var safeTitle = string.IsNullOrWhiteSpace(_editWidgetTitle) ? _selectedWidget.Title : _editWidgetTitle.Trim();
        _busy = true;
        try
        {
            await CmsApi.UpdateWidgetAsync(_selectedWidget.Id, new(
                _selectedWidget.SiteId,
                _selectedWidget.SectionId,
                _selectedWidget.ZoneId,
                _selectedWidget.WidgetType,
                safeTitle,
                _editWidgetPropsJson,
                string.IsNullOrWhiteSpace(_editWidgetStyleJson) ? null : _editWidgetStyleJson,
                _selectedWidget.SortOrder,
                _activeCulture));
            await LoadDesignAsync();
            SelectWidget(_selectedWidget.Id);
            Toast.Success("微件已更新");
        }
        catch (Exception ex)
        {
            Toast.Error($"保存失败：{ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DeleteWidgetAsync(Guid widgetId)
    {
        var target = _design?.Widgets.FirstOrDefault(w => w.Id == widgetId);
        try
        {
            await CmsApi.DeleteWidgetAsync(widgetId);
            if (target is not null)
                PushUndoEntry(new UndoEntry(UndoKind.RestoreDeletedWidget, target.Id, target.Title, target.PropsJson, target.StyleJson, target.SiteId, target.SectionId, target.ZoneId, target.WidgetType, target.SortOrder));
            if (_selectedWidget?.Id == widgetId)
                _selectedWidget = null;
            await LoadDesignAsync();
        }
        catch (Exception ex)
        {
            Toast.Error($"删除微件失败：{ex.Message}");
        }
    }

    private async Task DuplicateWidgetAsync(Guid widgetId)
    {
        if (_design is null) return;
        var src = _design.Widgets.FirstOrDefault(w => w.Id == widgetId);
        if (src is null) return;

        try
        {
            var created = await CmsApi.CreateWidgetAsync(new UpsertCmsWidgetRequest(
                src.SiteId,
                src.SectionId,
                src.ZoneId,
                src.WidgetType,
                $"{src.Title} (副本)",
                src.PropsJson,
                src.StyleJson,
                src.SortOrder + 1,
                _activeCulture));
            PushUndoEntry(new UndoEntry(UndoKind.DeleteCreatedWidget, created.Id, created.Title, created.PropsJson, created.StyleJson));
            await LoadDesignAsync();
            SelectWidget(created.Id);
            Toast.Success("已复制组件");
        }
        catch (Exception ex)
        {
            Toast.Error($"复制微件失败：{ex.Message}");
        }
    }

    private void PushUndoSnapshot(CmsWidgetDto w)
        => PushUndoEntry(new UndoEntry(UndoKind.UpdatePropsOrStyle, w.Id, w.Title, w.PropsJson, w.StyleJson));

    private void PushUndoEntry(UndoEntry entry)
    {
        _undoStack.Add(entry);
        if (_undoStack.Count > 30)
            _undoStack.RemoveAt(0);
    }

    private async Task UndoAsync()
    {
        if (_design is null || _undoStack.Count == 0) return;
        var entry = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        try
        {
            switch (entry.Kind)
            {
                case UndoKind.UpdatePropsOrStyle:
                {
                    var idx = _design.Widgets.FindIndex(x => x.Id == entry.WidgetId);
                    if (idx < 0) return;

                    var restored = _design.Widgets[idx] with
                    {
                        Title = entry.Title,
                        PropsJson = entry.PropsJson,
                        StyleJson = entry.StyleJson
                    };
                    _design.Widgets[idx] = restored;
                    _selectedWidget = restored;
                    _editWidgetTitle = restored.Title;
                    _editWidgetPropsJson = restored.PropsJson;
                    _editWidgetStyleJson = restored.StyleJson ?? string.Empty;
                    Toast.Info("已撤销上一步修改（点击「应用更改」生效）");
                    break;
                }
                case UndoKind.DeleteCreatedWidget:
                {
                    await CmsApi.DeleteWidgetAsync(entry.WidgetId);
                    if (_selectedWidget?.Id == entry.WidgetId)
                        _selectedWidget = null;
                    await LoadDesignAsync();
                    Toast.Info($"已撤销添加「{entry.Title}」");
                    break;
                }
                case UndoKind.RestoreDeletedWidget:
                {
                    var restored = await CmsApi.CreateWidgetAsync(new UpsertCmsWidgetRequest(
                        entry.SiteId,
                        entry.SectionId,
                        entry.ZoneId,
                        entry.WidgetType,
                        entry.Title,
                        entry.PropsJson,
                        entry.StyleJson,
                        entry.SortOrder,
                        _activeCulture));
                    await LoadDesignAsync();
                    SelectWidget(restored.Id);
                    Toast.Info($"已恢复微件「{entry.Title}」");
                    break;
                }
                case UndoKind.RestoreMove:
                {
                    await CmsApi.MoveWidgetAsync(entry.WidgetId, new(entry.SectionId, entry.ZoneId, entry.SortOrder));
                    await LoadDesignAsync();
                    SelectWidget(entry.WidgetId);
                    Toast.Info($"已撤销移动「{entry.Title}」");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Toast.Error($"撤销失败：{ex.Message}");
        }
    }

    private async Task DiscardWidgetChangesAsync()
    {
        if (_selectedWidget is null) return;
        var id = _selectedWidget.Id;
        await LoadDesignAsync();
        SelectWidget(id);
        Toast.Info("已恢复为上次保存的状态");
    }

    private void ResizeWidgetAsync((Guid WidgetId, string? Width, string? Height) args)
    {
        if (_design is null) return;
        var w = _design.Widgets.FirstOrDefault(x => x.Id == args.WidgetId);
        if (w is null) return;

        PushUndoSnapshot(w);
        var current = CmsJson.DeserializeOrDefault(w.StyleJson, new CmsWidgetStyle());
        var updatedStyle = current with
        {
            Width = args.Width ?? current.Width,
            Height = args.Height ?? current.Height
        };
        var styleJson = JsonSerializer.Serialize(updatedStyle, new JsonSerializerOptions(CmsJson.Options) { WriteIndented = true });

        _selectedWidget = w;
        _editingNavbar = false;
        _editWidgetTitle = w.Title;
        _editWidgetPropsJson = w.PropsJson;
        _editWidgetStyleJson = styleJson;
        SyncLiveWidgetPreview();
        Toast.Info($"已调整尺寸为 {updatedStyle.Width ?? "自适应"} × {updatedStyle.Height ?? "自适应"}（未保存，可「撤销」或点「应用更改」）");
    }

    private void InlineEditWidgetTextAsync((Guid WidgetId, string OldText, string NewText) args)
    {
        if (_design is null || string.IsNullOrWhiteSpace(args.NewText)) return;
        var w = _design.Widgets.FirstOrDefault(x => x.Id == args.WidgetId);
        if (w is null) return;

        try
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(w.PropsJson);
            if (root is null || !TryReplaceWidgetText(w, root, args.OldText, args.NewText))
            {
                Toast.Warning("该节点为固定标签或复合内容，无法直接保存，请在右侧属性面板修改。");
                return;
            }

            PushUndoSnapshot(w);
            var updatedJson = root.ToJsonString(new JsonSerializerOptions(CmsJson.Options) { WriteIndented = true });
            _selectedWidget = w;
            _editWidgetTitle = w.Title;
            _editWidgetPropsJson = updatedJson;
            _editWidgetStyleJson = w.StyleJson ?? string.Empty;
            SyncLiveWidgetPreview();
            Toast.Info("已修改画布文案（未保存，可「撤销」或点「应用更改」）");
        }
        catch (Exception ex)
        {
            Toast.Error($"更新失败：{ex.Message}");
        }
    }

    private bool TryReplaceWidgetText(CmsWidgetDto widget, System.Text.Json.Nodes.JsonNode root, string oldText, string newText)
    {
        if (TryReplaceJsonString(root, oldText, newText))
            return true;

        var trimmedOld = oldText.Trim();
        var trimmedNew = newText.Trim();

        // 1. 价格卡片特性列表前缀 "✓ "
        if (trimmedOld.StartsWith("✓ ", StringComparison.Ordinal))
        {
            var cleanOld = trimmedOld[2..].Trim();
            var cleanNew = trimmedNew.StartsWith("✓ ", StringComparison.Ordinal) ? trimmedNew[2..].Trim() : trimmedNew;
            if (TryReplaceJsonString(root, cleanOld, cleanNew))
                return true;
        }

        // 2. 客户评价外层双引号 “...”
        if (trimmedOld.Length >= 2 && trimmedOld.StartsWith('“') && trimmedOld.EndsWith('”'))
        {
            var cleanOld = trimmedOld[1..^1].Trim();
            var cleanNew = trimmedNew.Trim('“', '”').Trim();
            if (TryReplaceJsonString(root, cleanOld, cleanNew))
                return true;
        }

        // 3. 联系我们微件缺省兜底文案（JSON 中尚未显式配置时直接写入对应字段）
        if (string.Equals(widget.WidgetType, "cms.contact", StringComparison.OrdinalIgnoreCase)
            && root is System.Text.Json.Nodes.JsonObject contactObj)
        {
            if (string.Equals(trimmedOld, "contact@blazorboilerplate.dev", StringComparison.OrdinalIgnoreCase))
            {
                contactObj["email"] = trimmedNew;
                return true;
            }
            if (string.Equals(trimmedOld, "400-800-8888", StringComparison.OrdinalIgnoreCase))
            {
                contactObj["phone"] = trimmedNew;
                return true;
            }
            if (string.Equals(trimmedOld, L["cms.contact.defaultAddress"].Trim(), StringComparison.Ordinal))
            {
                contactObj["address"] = trimmedNew;
                return true;
            }
            if (string.Equals(trimmedOld, L["cms.contact.defaultHours"].Trim(), StringComparison.Ordinal))
            {
                contactObj["hours"] = trimmedNew;
                return true;
            }
        }

        // 4. 自定义 HTML 微件：替换 html 字符串中的匹配片段
        if (string.Equals(widget.WidgetType, "cms.html", StringComparison.OrdinalIgnoreCase)
            && root is System.Text.Json.Nodes.JsonObject htmlObj
            && htmlObj["html"] is System.Text.Json.Nodes.JsonValue htmlVal
            && htmlVal.TryGetValue<string>(out var htmlStr)
            && htmlStr.Contains(trimmedOld, StringComparison.Ordinal))
        {
            htmlObj["html"] = htmlStr.Replace(trimmedOld, trimmedNew, StringComparison.Ordinal);
            return true;
        }

        return false;
    }

    private static string NormalizeInlineText(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static bool TryReplaceJsonString(System.Text.Json.Nodes.JsonNode node, string oldText, string newText)
    {
        var normalizedOld = NormalizeInlineText(oldText);
        switch (node)
        {
            case System.Text.Json.Nodes.JsonObject obj:
                foreach (var kv in obj.ToList())
                {
                    if (kv.Value is System.Text.Json.Nodes.JsonValue val && val.TryGetValue<string>(out var str)
                        && string.Equals(NormalizeInlineText(str), normalizedOld, StringComparison.Ordinal))
                    {
                        obj[kv.Key] = newText;
                        return true;
                    }
                    if (kv.Value is not null && TryReplaceJsonString(kv.Value, oldText, newText))
                        return true;
                }
                break;
            case System.Text.Json.Nodes.JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    var item = arr[i];
                    if (item is System.Text.Json.Nodes.JsonValue val && val.TryGetValue<string>(out var str)
                        && string.Equals(NormalizeInlineText(str), normalizedOld, StringComparison.Ordinal))
                    {
                        arr[i] = newText;
                        return true;
                    }
                    if (item is not null && TryReplaceJsonString(item, oldText, newText))
                        return true;
                }
                break;
        }
        return false;
    }

    private async Task SaveThemeAsync()
    {
        if (_design is null) return;
        _busy = true;
        try
        {
            await CmsApi.UpdateThemeAsync(_design.Site.Id, new CmsThemeConfig(
                _themePrimary, _themeAccent, _themeBg, _themeSurface, _themeText, BorderRadius: _themeRadius));
            Toast.Success("主题配色已保存");
        }
        finally
        {
            _busy = false;
        }
    }

    private void OpenAddSectionModal()
    {
        _newSecTitle = string.Empty;
        _newSecSlug = string.Empty;
        _newSecNavVisible = true;
        _addSectionOpen = true;
    }

    private async Task CreateSectionAsync()
    {
        if (_design is null || string.IsNullOrWhiteSpace(_newSecTitle) || string.IsNullOrWhiteSpace(_newSecSlug) || !Guid.TryParse(_newSecLayoutId, out var layoutId))
        {
            Toast.Warning("请填写区块名称、锚点标识并选择布局。");
            return;
        }
        _busy = true;
        try
        {
            var created = await CmsApi.CreateSectionAsync(_design.Site.Id, new(layoutId, _newSecTitle, _newSecSlug, _newSecNavVisible, Culture: _activeCulture));
            _addSectionOpen = false;
            await LoadDesignAsync();
            _activeSectionId = created.Id;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task MoveSectionAsync(int index, int delta)
    {
        if (_design is null) return;
        var ordered = _design.Sections.OrderBy(s => s.SortOrder).ToList();
        var target = index + delta;
        if (target < 0 || target >= ordered.Count) return;

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        try
        {
            await CmsApi.ReorderSectionsAsync(_design.Site.Id, ordered.Select(s => s.Id).ToList());
            await LoadDesignAsync();
        }
        catch (Exception ex)
        {
            Toast.Error($"排序失败：{ex.Message}");
        }
    }

    private async Task DeleteSectionAsync(Guid sectionId)
    {
        try
        {
            await CmsApi.DeleteSectionAsync(sectionId);
            if (_activeSectionId == sectionId)
                _activeSectionId = null;
            await LoadDesignAsync();
        }
        catch (Exception ex)
        {
            Toast.Error($"删除区块失败：{ex.Message}");
        }
    }

    private void OpenAiModal()
    {
        _aiBrand = _design?.Site.Name ?? string.Empty;
        _aiOpen = true;
    }

    private async Task RunAiGenerateAsync()
    {
        if (_design is null || string.IsNullOrWhiteSpace(_aiBrand))
        {
            Toast.Warning("请填写品牌名称。");
            return;
        }
        _aiBusy = true;
        try
        {
            var draft = await CmsApi.AiGenerateSiteAsync(new(_aiBrand, _aiIndustry, _aiPrompt, _aiColor, _activeCulture ?? "zh-CN"));
            _design = await CmsApi.AiApplySiteAsync(_design.Site.Id, draft, _activeCulture);
            _aiOpen = false;
            _selectedWidget = null;
            await LoadDesignAsync();
            Toast.Success("AI 已生成官网草稿，可在画布中继续拖拽微调！");
        }
        catch (Exception ex)
        {
            Toast.Error($"AI 生成失败：{ex.Message}");
        }
        finally
        {
            _aiBusy = false;
        }
    }

    private async Task PublishAsync()
    {
        if (_design is null) return;
        _busy = true;
        try
        {
            await CmsApi.PublishSiteAsync(_design.Site.Id);
            await LoadDesignAsync();
            Toast.Success("官网已发布上线！");
        }
        catch (Exception ex)
        {
            Toast.Error($"发布失败：{ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task OnCultureChangedAsync(ChangeEventArgs e)
    {
        var next = e.Value?.ToString();
        if (string.IsNullOrWhiteSpace(next)) return;
        _activeCulture = next;
        _selectedWidget = null;
        _activeSectionId = null;
        await LoadDesignAsync();
    }

    private async Task CloneFromDefaultCultureAsync()
    {
        if (_design is null || string.IsNullOrWhiteSpace(_activeCulture)) return;
        _busy = true;
        try
        {
            _design = await CmsApi.CloneCultureAsync(_design.Site.Id, _design.Site.DefaultCulture, _activeCulture);
            Toast.Success($"已从 {_design.Site.DefaultCulture} 复制结构到 {_activeCulture}");
        }
        finally
        {
            _busy = false;
        }
    }

    private void ToggleLivePreview() => _isLivePreview = !_isLivePreview;

    private async Task CloneCurrentSiteAsync()
    {
        if (_design is null) return;
        _busy = true;
        try
        {
            var cloned = await CmsApi.CloneSiteAsync(_design.Site.Id);
            Toast.Success($"已复制为新站点「{cloned.Name}」，正在打开设计器...");
            Navigation.NavigateTo($"/cms/designer/{cloned.Id}");
        }
        catch (Exception ex)
        {
            Toast.Error($"复制失败：{ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private void OnEditorPropsChanged(string json)
    {
        if (_selectedWidget is not null) PushUndoSnapshot(_selectedWidget);
        _editWidgetPropsJson = json;
        SyncLiveWidgetPreview();
    }

    private void OnEditorStyleChanged(string json)
    {
        if (_selectedWidget is not null) PushUndoSnapshot(_selectedWidget);
        _editWidgetStyleJson = json;
        SyncLiveWidgetPreview();
    }

    private void SyncLiveWidgetPreview()
    {
        if (_design is null || _selectedWidget is null)
            return;

        var idx = _design.Widgets.FindIndex(w => w.Id == _selectedWidget.Id);
        if (idx < 0)
            return;

        var updated = _selectedWidget with
        {
            Title = _editWidgetTitle,
            PropsJson = _editWidgetPropsJson,
            StyleJson = string.IsNullOrWhiteSpace(_editWidgetStyleJson) ? null : _editWidgetStyleJson
        };
        _design.Widgets[idx] = updated;
        _selectedWidget = updated;
    }

    private Task OpenPublishedSite() =>
        _design is null
            ? Task.CompletedTask
            : JS.InvokeVoidAsync("open", $"/site?siteKey={Uri.EscapeDataString(_design.Site.SiteKey)}&culture={Uri.EscapeDataString(_activeCulture ?? _design.Site.DefaultCulture)}", "_blank", "noopener").AsTask();

    private static string NormalizeHex(string? raw, string fallback)
    {
        var v = raw?.Trim();
        if (string.IsNullOrWhiteSpace(v) || !v.StartsWith('#')) return fallback;
        if (v.Length == 7) return v;
        if (v.Length == 4) return $"#{v[1]}{v[1]}{v[2]}{v[2]}{v[3]}{v[3]}";
        return fallback;
    }

    private void OpenMediaPickerForWidget()
    {
        _pickingSiteLogo = false;
        _replacingImageUrl = null;
        _mediaOpen = true;
    }

    private void StartInlineImagePick((Guid WidgetId, string OldImageUrl) args)
    {
        _pickingSiteLogo = false;
        SelectWidget(args.WidgetId);
        _replacingImageUrl = args.OldImageUrl;
        _mediaOpen = true;
    }

    private void StartPickSiteLogo()
    {
        if (EffectiveLivePreview || _design is null) return;
        _pickingSiteLogo = true;
        _replacingImageUrl = null;
        _mediaOpen = true;
    }

    private void OpenNavbarEditor()
    {
        if (_design is null || EffectiveLivePreview) return;
        _selectedWidget = null;
        _editingNavbar = true;
        _navSiteTitle = _design.Site.Title;
        _navSiteLogoUrl = _design.Site.LogoUrl;
        _navSectionTitles.Clear();
        _navSectionVisible.Clear();
        foreach (var sec in _design.Sections)
        {
            _navSectionTitles[sec.Id] = sec.Title;
            _navSectionVisible[sec.Id] = sec.NavVisible;
        }
    }

    private void CloseNavbarEditor() => _editingNavbar = false;

    private async Task ClearSiteLogoAsync()
    {
        if (_design is null) return;
        _busy = true;
        try
        {
            var s = _design.Site;
            await CmsApi.UpdateSiteAsync(s.Id, new UpsertCmsSiteRequest(
                s.Name, s.SiteKey, s.Title, s.Description, null, s.FaviconUrl, s.DefaultCulture, s.SupportedCultures));
            await LoadDesignAsync();
            if (_editingNavbar) OpenNavbarEditor();
            Toast.Success("已清除站点 Logo");
        }
        catch (Exception ex)
        {
            Toast.Error($"清除 Logo 失败：{ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task SaveNavbarBrandAsync()
    {
        if (_design is null || string.IsNullOrWhiteSpace(_navSiteTitle)) return;
        _busy = true;
        try
        {
            var s = _design.Site;
            await CmsApi.UpdateSiteAsync(s.Id, new UpsertCmsSiteRequest(
                s.Name, s.SiteKey, _navSiteTitle.Trim(), s.Description, _navSiteLogoUrl, s.FaviconUrl, s.DefaultCulture, s.SupportedCultures));
            await LoadDesignAsync();
            if (_editingNavbar) OpenNavbarEditor();
            Toast.Success("站点标题与品牌已更新");
        }
        catch (Exception ex)
        {
            Toast.Error($"保存失败：{ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task SaveNavbarSectionsAsync()
    {
        if (_design is null) return;
        _busy = true;
        try
        {
            foreach (var sec in _design.Sections)
            {
                var nextTitle = _navSectionTitles.TryGetValue(sec.Id, out var t) && !string.IsNullOrWhiteSpace(t) ? t.Trim() : sec.Title;
                var nextVisible = _navSectionVisible.TryGetValue(sec.Id, out var v) ? v : sec.NavVisible;
                if (!string.Equals(nextTitle, sec.Title, StringComparison.Ordinal) || nextVisible != sec.NavVisible)
                {
                    await CmsApi.UpdateSectionAsync(sec.Id, new UpsertCmsSectionRequest(
                        sec.LayoutId, nextTitle, sec.AnchorSlug, nextVisible, sec.SortOrder, sec.BackgroundStyle, sec.IsEnabled, _activeCulture));
                }
            }
            await LoadDesignAsync();
            if (_editingNavbar) OpenNavbarEditor();
            Toast.Success("导航菜单已更新");
        }
        catch (Exception ex)
        {
            Toast.Error($"保存导航失败：{ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private void BeginEditSiteTitle()
    {
        if (_design is null || EffectiveLivePreview) return;
        _editingNavSectionId = null;
        _editSiteTitleValue = _design.Site.Title;
        _editingSiteTitle = true;
    }

    private async Task CommitSiteTitleAsync()
    {
        if (!_editingSiteTitle || _design is null) return;
        _editingSiteTitle = false;
        var next = _editSiteTitleValue.Trim();
        if (string.IsNullOrWhiteSpace(next) || string.Equals(next, _design.Site.Title, StringComparison.Ordinal))
            return;
        try
        {
            var s = _design.Site;
            await CmsApi.UpdateSiteAsync(s.Id, new UpsertCmsSiteRequest(
                s.Name, s.SiteKey, next, s.Description, s.LogoUrl, s.FaviconUrl, s.DefaultCulture, s.SupportedCultures));
            await LoadDesignAsync();
            Toast.Success("站点标题已更新");
        }
        catch (Exception ex)
        {
            Toast.Error($"保存站点标题失败：{ex.Message}");
        }
    }

    private async Task OnSiteTitleKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await CommitSiteTitleAsync();
        else if (e.Key == "Escape") _editingSiteTitle = false;
    }

    private void BeginEditNavSection(CmsSectionDto sec)
    {
        if (EffectiveLivePreview) return;
        _editingSiteTitle = false;
        _editingNavSectionId = sec.Id;
        _editNavSectionTitle = sec.Title;
    }

    private async Task CommitNavSectionAsync(CmsSectionDto sec)
    {
        if (_editingNavSectionId != sec.Id) return;
        _editingNavSectionId = null;
        var next = _editNavSectionTitle.Trim();
        if (string.IsNullOrWhiteSpace(next) || string.Equals(next, sec.Title, StringComparison.Ordinal))
            return;
        try
        {
            await CmsApi.UpdateSectionAsync(sec.Id, new UpsertCmsSectionRequest(
                sec.LayoutId, next, sec.AnchorSlug, sec.NavVisible, sec.SortOrder, sec.BackgroundStyle, sec.IsEnabled, _activeCulture));
            await LoadDesignAsync();
            Toast.Success("导航名称已更新");
        }
        catch (Exception ex)
        {
            Toast.Error($"保存导航名称失败：{ex.Message}");
        }
    }

    private async Task OnNavSectionKeyDownAsync(KeyboardEventArgs e, CmsSectionDto sec)
    {
        if (e.Key == "Enter") await CommitNavSectionAsync(sec);
        else if (e.Key == "Escape") _editingNavSectionId = null;
    }

    private async Task SwitchCultureAsync(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang) || string.Equals(_activeCulture, lang, StringComparison.OrdinalIgnoreCase))
            return;
        _activeCulture = lang;
        _selectedWidget = null;
        _activeSectionId = null;
        await LoadDesignAsync();
    }

    private async Task HandleMediaSelected(string url)
    {
        if (_pickingSiteLogo && _design is not null)
        {
            _pickingSiteLogo = false;
            try
            {
                var s = _design.Site;
                await CmsApi.UpdateSiteAsync(s.Id, new UpsertCmsSiteRequest(
                    s.Name, s.SiteKey, s.Title, s.Description, url, s.FaviconUrl, s.DefaultCulture, s.SupportedCultures));
                await LoadDesignAsync();
                if (_editingNavbar) _navSiteLogoUrl = url;
                Toast.Success("站点 Logo 已更新");
            }
            catch (Exception ex)
            {
                Toast.Error($"更新 Logo 失败：{ex.Message}");
            }
            return;
        }
        if (_selectedWidget is null) return;
        try
        {
            PushUndoSnapshot(_selectedWidget);

            if (!string.IsNullOrWhiteSpace(_replacingImageUrl))
            {
                if (_replacingImageUrl.StartsWith("__carousel_slide__:", StringComparison.Ordinal)
                    && int.TryParse(_replacingImageUrl["__carousel_slide__:".Length..], out var slideIdx))
                {
                    var carouselNode = JsonSerializer.Deserialize<Dictionary<string, object?>>(_editWidgetPropsJson, CmsJson.Options) ?? [];
                    var slides = carouselNode.TryGetValue("slides", out var rawSlides) && rawSlides is JsonElement el && el.ValueKind == JsonValueKind.Array
                        ? JsonSerializer.Deserialize<List<Dictionary<string, string?>>>(el.GetRawText(), CmsJson.Options) ?? []
                        : [];
                    if (slideIdx >= 0 && slideIdx < slides.Count)
                        slides[slideIdx]["imageUrl"] = url;
                    else
                        slides.Add(new() { ["imageUrl"] = url, ["title"] = "新幻灯片", ["subtitle"] = "双击此处编辑文案", ["buttonText"] = "了解更多", ["buttonHref"] = "#contact" });
                    carouselNode["slides"] = slides;
                    _editWidgetPropsJson = JsonSerializer.Serialize(carouselNode, new JsonSerializerOptions(CmsJson.Options) { WriteIndented = true });
                    _replacingImageUrl = null;
                    SyncLiveWidgetPreview();
                    Toast.Info("已更新轮播图图片（未保存，可「撤销」或点「应用更改」）");
                    return;
                }

                var root = System.Text.Json.Nodes.JsonNode.Parse(_editWidgetPropsJson);
                if (root is not null && TryReplaceJsonString(root, _replacingImageUrl, url))
                {
                    _editWidgetPropsJson = root.ToJsonString(new JsonSerializerOptions(CmsJson.Options) { WriteIndented = true });
                    _replacingImageUrl = null;
                    SyncLiveWidgetPreview();
                    Toast.Info("已替换画布图片（未保存，可「撤销」或点「应用更改」）");
                    return;
                }
                _replacingImageUrl = null;
            }

            var node = JsonSerializer.Deserialize<Dictionary<string, object?>>(_editWidgetPropsJson, CmsJson.Options) ?? [];
            if (string.Equals(_selectedWidget.WidgetType, "cms.gallery", StringComparison.OrdinalIgnoreCase))
            {
                var list = node.TryGetValue("items", out var raw) && raw is JsonElement el && el.ValueKind == JsonValueKind.Array
                    ? JsonSerializer.Deserialize<List<Dictionary<string, string?>>>(el.GetRawText(), CmsJson.Options) ?? []
                    : [];
                list.Add(new() { ["url"] = url, ["caption"] = "新图片" });
                node["items"] = list;
            }
            else if (string.Equals(_selectedWidget.WidgetType, "cms.carousel", StringComparison.OrdinalIgnoreCase))
            {
                var slides = node.TryGetValue("slides", out var raw) && raw is JsonElement el && el.ValueKind == JsonValueKind.Array
                    ? JsonSerializer.Deserialize<List<Dictionary<string, string?>>>(el.GetRawText(), CmsJson.Options) ?? []
                    : [];
                slides.Add(new() { ["imageUrl"] = url, ["title"] = "新幻灯片", ["subtitle"] = "点击右侧编辑文案", ["buttonText"] = "了解更多", ["buttonHref"] = "#contact" });
                node["slides"] = slides;
            }
            else
            {
                node["imageUrl"] = url;
            }
            _editWidgetPropsJson = JsonSerializer.Serialize(node, new JsonSerializerOptions(CmsJson.Options) { WriteIndented = true });
            SyncLiveWidgetPreview();
            Toast.Success("已插入媒体链接，画布已实时更新，记得点击「应用更改」保存");
        }
        catch
        {
            Toast.Info($"已复制媒体地址：{url}");
        }
    }
}
