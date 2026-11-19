using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Web;
using Bit.BlazorUI;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Modules.Localization.Models;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlazorBoilerplate.Modules.Localization.Pages;

public partial class Localization
{
    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] private ILocalizationService L { get; set; } = default!;
    [Inject] private FrontendLocalizationService FrontendLoc { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private ToastService Toast { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private string _activeTabKey = "languages";
    private bool _busy;
    private string? _error;
    private string? _modalError;
    private string? _notice;

    // 语言管理状态
    private List<LanguageDto> _languages = [];
    private List<BitDropdownItem<string>> _cultureDropdownItems = [];
    private List<BitDropdownItem<string>> _filteredCultureItems = [];
    private static readonly List<BitDropdownItem<string>> _countryFlagItems = BuildCountryFlagItems();
    private List<BitDropdownItem<string>> _filteredCountryFlagItems = [.. _countryFlagItems];
    private bool _langDialogOpen;
    private Guid? _editingLangId;
    private bool _editingProtected;
    private string _langCulture = string.Empty;
    private string _langName = string.Empty;
    private string _langDisplayName = string.Empty;
    private string? _langFlagCode;
    private int _langOrder = 30;
    private bool _langRtl;
    private bool _langDefault;
    private bool _langEnabled = true;
    private LanguageDto? _deletingLang;

    // 词条管理状态
    private List<string> _modules = [];
    private Dictionary<string, string> _moduleDisplayNames = new(StringComparer.OrdinalIgnoreCase);
    private string _targetCulture = LocalizationDefaults.DefaultChineseCulture;
    private string _selectedModule = string.Empty;
    private string _keyword = string.Empty;
    private bool _onlyMissing;
    private int _page = 1;
    private int _pageSize = 10;

    // 资源页工具栏下拉项。
    private List<BitDropdownItem<string>> TargetLanguageItems =>
        _languages.Select(l => new BitDropdownItem<string> { Text = $"{l.DisplayName} ({l.Culture})", Value = l.Culture }).ToList();

    private List<BitDropdownItem<string>> ModuleItems =>
    [
        new() { Text = L["localization.res.allModules"], Value = string.Empty },
        .. _modules.Select(m => new BitDropdownItem<string> { Text = GetModuleDisplayName(m), Value = m })
    ];
    private int _gridVersion;
    private BitDataGridStrings _gridStrings = new();
    private int _totalResources;
    private List<ResourceRowModel> _resources = [];

    private bool _importDialogOpen;
    private string _importJson = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        await FrontendLoc.EnsureLoadedAsync();
        _gridStrings = L.CreateGridStrings();

        var query = HttpUtility.ParseQueryString(new Uri(Nav.Uri).Query);
        var tab = query["tab"];
        if (tab is "languages" or "resources")
            _activeTabKey = tab;
        if (!string.IsNullOrWhiteSpace(query["culture"]))
            _targetCulture = query["culture"]!;
        if (!string.IsNullOrWhiteSpace(query["module"]))
            _selectedModule = query["module"]!;
        if (int.TryParse(query["page"], out var p) && p >= 1)
            _page = p;
        if (int.TryParse(query["pageSize"], out var ps) && ps is >= 10 and <= 200)
            _pageSize = ps;
        if (bool.TryParse(query["onlyMissing"], out var om))
            _onlyMissing = om;

        await LoadLanguagesAsync();
        if (_languages.Count > 0 && _languages.All(l => !string.Equals(l.Culture, _targetCulture, StringComparison.OrdinalIgnoreCase)))
            _targetCulture = _languages[0].Culture;

        if (_activeTabKey == "resources")
        {
            await LoadModulesAsync();
        }
    }

    private void SyncUrlQuery()
    {
        var uri = Nav.GetUriWithQueryParameters(new Dictionary<string, object?>
        {
            ["tab"] = _activeTabKey,
            ["culture"] = _activeTabKey == "resources" ? _targetCulture : null,
            ["module"] = _activeTabKey == "resources" && !string.IsNullOrWhiteSpace(_selectedModule) ? _selectedModule : null,
            ["page"] = _activeTabKey == "resources" && _page > 1 ? _page : null,
            ["pageSize"] = _activeTabKey == "resources" && _pageSize != 10 ? _pageSize : null,
            ["onlyMissing"] = _activeTabKey == "resources" && _onlyMissing ? true : null
        });
        Nav.NavigateTo(uri, replace: true);
    }

    private async Task OnTabChangedAsync(string key)
    {
        _activeTabKey = key;
        _error = null;
        _notice = null;
        SyncUrlQuery();
        if (key == "languages")
        {
            await LoadLanguagesAsync();
        }
        else
        {
            await LoadModulesAsync();
        }
    }

    private async Task LoadLanguagesAsync()
    {
        _error = null;
        _busy = true;
        StateHasChanged();
        try
        {
            _languages = await Http.GetFromJsonAsync<List<LanguageDto>>("api/localization/admin/languages", JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _busy = false;
            StateHasChanged();
        }
    }

    private async Task LoadModulesAsync()
    {
        try
        {
            _modules = await Http.GetFromJsonAsync<List<string>>("api/localization/admin/modules", JsonOptions) ?? [];
        }
        catch
        {
            _modules = [];
        }

        try
        {
            var descriptors = await Http.GetFromJsonAsync<List<BlazorBoilerplate.ModuleAbstractions.ModuleDescriptor>>("api/modules", JsonOptions);
            if (descriptors is not null)
            {
                _moduleDisplayNames = descriptors
                    .Where(d => !string.IsNullOrWhiteSpace(d.Id))
                    .ToDictionary(
                        d => d.Id,
                        d => string.IsNullOrWhiteSpace(d.DisplayName) ? d.Name : d.DisplayName,
                        StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // 获取模块展示名失败时回退为 moduleId
        }
    }

    private string GetModuleDisplayName(string moduleId)
    {
        if (string.Equals(moduleId, LocalizationDefaults.PlatformModuleId, StringComparison.OrdinalIgnoreCase))
            return L["localization.res.platformModule"];

        return _moduleDisplayNames.TryGetValue(moduleId, out var displayName) && !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : moduleId;
    }

    private Task RefreshGridAsync()
    {
        _gridVersion++;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task<BitDataGridReadResult<ResourceRowModel>> ReadResourcesAsync(BitDataGridReadRequest req)
    {
        _error = null;
        var take = Math.Clamp(req.Take is > 0 ? req.Take.Value : _pageSize, 10, 200);
        var page = (req.Skip / take) + 1;
        _page = page;
        _pageSize = take;

        var moduleFilter = _selectedModule;
        var keywordFilter = _keyword;
        foreach (var filter in req.Filters)
        {
            var val = Convert.ToString(filter.Value, CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrWhiteSpace(val))
                continue;

            if (string.Equals(filter.ColumnId, "moduleId", StringComparison.OrdinalIgnoreCase))
                moduleFilter = val;
            else
                keywordFilter = val;
        }

        try
        {
            var url = $"api/localization/admin/resources?culture={Uri.EscapeDataString(_targetCulture)}&page={page}&pageSize={take}&onlyMissing={_onlyMissing}";
            if (!string.IsNullOrWhiteSpace(moduleFilter))
                url += $"&moduleId={Uri.EscapeDataString(moduleFilter)}";
            if (!string.IsNullOrWhiteSpace(keywordFilter))
                url += $"&keyword={Uri.EscapeDataString(keywordFilter)}";

            var result = await Http.GetFromJsonAsync<PagedResourcesResult>(url, JsonOptions);
            _totalResources = result?.TotalCount ?? 0;
            _resources = result?.Items.ToList() ?? [];

            if (req.Sorts.FirstOrDefault() is { } sort)
            {
                var desc = sort.Direction == BitDataGridSortDirection.Descending;
                _resources = sort.ColumnId switch
                {
                    "moduleId" => (desc ? _resources.OrderByDescending(r => r.ModuleId) : _resources.OrderBy(r => r.ModuleId)).ToList(),
                    "key" => (desc ? _resources.OrderByDescending(r => r.Key) : _resources.OrderBy(r => r.Key)).ToList(),
                    _ => _resources
                };
            }

            return new BitDataGridReadResult<ResourceRowModel>(_resources, _totalResources);
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            return new BitDataGridReadResult<ResourceRowModel>([], 0);
        }
    }

    private void OpenCreateLanguage()
    {
        _editingLangId = null;
        _editingProtected = false;
        _modalError = null;
        RebuildCultureDropdownItems();
        _filteredCountryFlagItems = [.. _countryFlagItems];
        _langCulture = string.Empty;
        _langName = string.Empty;
        _langDisplayName = string.Empty;
        _langFlagCode = null;
        _langOrder = (_languages.Count + 1) * 10;
        _langRtl = false;
        _langDefault = false;
        _langEnabled = true;
        _langDialogOpen = true;
    }

    private void OpenEditLanguage(LanguageDto lang)
    {
        _editingLangId = lang.Id;
        _editingProtected = lang.IsFallbackProtected;
        _modalError = null;
        _filteredCountryFlagItems = [.. _countryFlagItems];
        _langCulture = lang.Culture;
        _langName = lang.Name;
        _langDisplayName = lang.DisplayName;
        _langFlagCode = lang.FlagCode;
        _langOrder = lang.DisplayOrder;
        _langRtl = lang.IsRtl;
        _langDefault = lang.IsDefault;
        _langEnabled = lang.IsEnabled;
        _langDialogOpen = true;
    }

    private void OnCultureItemSelected(BitDropdownItem<string> item)
    {
        _langCulture = item.Value ?? string.Empty;
        _modalError = null;
        if (_editingLangId is not null || string.IsNullOrWhiteSpace(_langCulture))
            return;

        try
        {
            var info = CultureInfo.GetCultureInfo(_langCulture.Trim());
            if (!string.IsNullOrWhiteSpace(info.Name))
            {
                _langName = info.NativeName;
                _langDisplayName = info.DisplayName;
                _langRtl = info.TextInfo.IsRightToLeft;

                string? iso2 = null;
                if (!info.IsNeutralCulture)
                {
                    try
                    {
                        var region = new RegionInfo(info.LCID);
                        if (region.TwoLetterISORegionName.Length == 2)
                            iso2 = region.TwoLetterISORegionName.ToUpperInvariant();
                    }
                    catch
                    {
                        // 忽略无 RegionInfo 的特殊文化
                    }
                }

                if (iso2 is null)
                {
                    var lastPart = info.Name.Split('-').LastOrDefault();
                    if (lastPart is { Length: 2 } && lastPart.All(char.IsLetter))
                        iso2 = lastPart.ToUpperInvariant();
                }

                _langFlagCode = iso2;
            }
        }
        catch
        {
            // 忽略无法解析的区域信息
        }

        StateHasChanged();
    }

    private void RebuildCultureDropdownItems()
    {
        var existing = new HashSet<string>(_languages.Select(l => l.Culture), StringComparer.OrdinalIgnoreCase);
        _cultureDropdownItems = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Where(c => !string.IsNullOrWhiteSpace(c.Name) && !existing.Contains(c.Name))
            .OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new BitDropdownItem<string>
            {
                Value = c.Name,
                Text = $"{c.DisplayName} ({c.Name})"
            })
            .ToList();
        _filteredCultureItems = [.. _cultureDropdownItems];
    }

    private void OnCultureSearch(string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            _filteredCultureItems = [.. _cultureDropdownItems];
            return;
        }

        var kw = searchText.Trim();
        _filteredCultureItems = _cultureDropdownItems
            .Where(i => (i.Text?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (i.Value?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

    private void OnFlagSearch(string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            _filteredCountryFlagItems = [.. _countryFlagItems];
            return;
        }

        var kw = searchText.Trim();
        _filteredCountryFlagItems = _countryFlagItems
            .Where(i => (i.Text?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (i.Value?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

    private static List<BitDropdownItem<string>> BuildCountryFlagItems()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.LCID);
                if (region.TwoLetterISORegionName.Length == 2 && !map.ContainsKey(region.TwoLetterISORegionName))
                    map[region.TwoLetterISORegionName] = $"{region.DisplayName} ({region.TwoLetterISORegionName})";
            }
            catch
            {
                // 跳过无地区映射的特殊文化
            }
        }

        return map
            .OrderBy(kv => kv.Value, StringComparer.CurrentCultureIgnoreCase)
            .Select(kv => new BitDropdownItem<string> { Value = kv.Key, Text = kv.Value })
            .ToList();
    }

    private async Task SaveLanguageAsync()
    {
        _busy = true;
        _error = null;
        _modalError = null;
        _notice = null;
        try
        {
            if (string.IsNullOrWhiteSpace(_langCulture))
                throw new InvalidOperationException("请先选择一种语言。");

            HttpResponseMessage resp;
            if (_editingLangId is null)
            {
                var req = new CreateLanguageRequest(
                    _langCulture.Trim(),
                    _langName.Trim(),
                    _langDisplayName.Trim(),
                    _langFlagCode,
                    _langRtl,
                    _langDefault,
                    _langEnabled,
                    _langOrder);
                resp = await Http.PostAsJsonAsync("api/localization/admin/languages", req, JsonOptions);
            }
            else
            {
                var req = new UpdateLanguageRequest(
                    _langName.Trim(),
                    _langDisplayName.Trim(),
                    _langFlagCode,
                    _langRtl,
                    _langDefault,
                    _langEnabled,
                    _langOrder);
                resp = await Http.PutAsJsonAsync($"api/localization/admin/languages/{_editingLangId}", req, JsonOptions);
            }

            await EnsureSuccessAsync(resp);
            _langDialogOpen = false;
            await LoadLanguagesAsync();
            await FrontendLoc.InvalidateAsync();
            Toast.Success(L["localization.toast.langSaved"]);
        }
        catch (Exception ex)
        {
            _modalError = ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task SetDefaultLanguageAsync(LanguageDto lang)
    {
        _busy = true;
        _error = null;
        try
        {
            var req = new UpdateLanguageRequest(
                lang.Name,
                lang.DisplayName,
                lang.FlagCode,
                lang.IsRtl,
                IsDefault: true,
                IsEnabled: true,
                lang.DisplayOrder);
            var resp = await Http.PutAsJsonAsync($"api/localization/admin/languages/{lang.Id}", req, JsonOptions);
            await EnsureSuccessAsync(resp);
            await LoadLanguagesAsync();
            await FrontendLoc.InvalidateAsync();
            Toast.Success(L["localization.toast.langSaved"]);
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

    private void AskDeleteLanguage(LanguageDto lang) => _deletingLang = lang;

    private async Task ConfirmDeleteLanguageAsync()
    {
        if (_deletingLang is null) return;
        _busy = true;
        _error = null;
        try
        {
            var resp = await Http.DeleteAsync($"api/localization/admin/languages/{_deletingLang.Id}");
            await EnsureSuccessAsync(resp);
            _deletingLang = null;
            await LoadLanguagesAsync();
            await FrontendLoc.InvalidateAsync();
            Toast.Success(L["localization.toast.langDeleted"]);
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

    private async Task OnTargetCultureChangedAsync()
    {
        _page = 1;
        SyncUrlQuery();
        await RefreshGridAsync();
    }

    private async Task OnModuleChangedAsync()
    {
        _page = 1;
        SyncUrlQuery();
        await RefreshGridAsync();
    }

    private async Task OnKeywordChangedAsync(string? value)
    {
        _keyword = value ?? string.Empty;
        _page = 1;
        await RefreshGridAsync();
    }

    private async Task OnOnlyMissingChangedAsync(bool value)
    {
        _onlyMissing = value;
        _page = 1;
        SyncUrlQuery();
        await RefreshGridAsync();
    }

    private static void OnRowValueInput(ResourceRowModel row, string? value)
    {
        row.TargetValue = value;
        row.IsDirty = true;
    }

    private async Task SaveSingleRowAsync(ResourceRowModel row)
    {
        await SaveRowsAsync([row]);
    }

    private async Task SaveAllDirtyAsync()
    {
        var dirty = _resources.Where(r => r.IsDirty).ToList();
        if (dirty.Count > 0)
            await SaveRowsAsync(dirty);
    }

    private async Task SaveRowsAsync(List<ResourceRowModel> rows)
    {
        _busy = true;
        _error = null;
        try
        {
            var req = new UpsertResourcesRequest(
                _targetCulture,
                rows.Select(r => new UpsertResourceItem(r.ModuleId, r.Key, r.TargetValue ?? string.Empty)).ToList());
            var resp = await Http.PutAsJsonAsync("api/localization/admin/resources", req, JsonOptions);
            await EnsureSuccessAsync(resp);

            foreach (var r in rows)
            {
                r.IsDirty = false;
                r.IsOverridden = true;
                r.IsMissing = string.IsNullOrWhiteSpace(r.TargetValue);
            }

            await FrontendLoc.RefreshCacheAsync(_targetCulture);
            Toast.Success(L.GetString("localization.toast.saved", rows.Count));
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

    private async Task ResetRowAsync(ResourceRowModel row)
    {
        _busy = true;
        _error = null;
        try
        {
            var resp = await Http.PostAsJsonAsync(
                "api/localization/admin/resources/reset",
                new ResetResourceRequest(row.ModuleId, _targetCulture, row.Key),
                JsonOptions);
            await EnsureSuccessAsync(resp);

            var checkUrl = $"api/localization/admin/resources?culture={Uri.EscapeDataString(_targetCulture)}&moduleId={Uri.EscapeDataString(row.ModuleId)}&keyword={Uri.EscapeDataString(row.Key)}&page=1&pageSize=10";
            var refreshed = await Http.GetFromJsonAsync<PagedResourcesResult>(checkUrl, JsonOptions);
            var latest = refreshed?.Items.FirstOrDefault(i =>
                string.Equals(i.ModuleId, row.ModuleId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(i.Key, row.Key, StringComparison.OrdinalIgnoreCase));

            row.TargetValue = latest?.TargetValue;
            row.IsOverridden = latest?.IsOverridden ?? false;
            row.IsMissing = latest?.IsMissing ?? true;
            row.IsDirty = false;

            await FrontendLoc.RefreshCacheAsync(_targetCulture);
            Toast.Success(L["localization.toast.reset"]);
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

    private async Task SyncBuiltInAsync()
    {
        _busy = true;
        _error = null;
        try
        {
            var resp = await Http.PostAsync("api/localization/admin/resources/sync", null);
            await EnsureSuccessAsync(resp);
            await LoadModulesAsync();
            await RefreshGridAsync();
            await FrontendLoc.InvalidateAsync(_targetCulture);
            Toast.Success(L["localization.toast.synced"]);
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

    private async Task ExportJsonAsync()
    {
        _busy = true;
        _error = null;
        try
        {
            var url = $"api/localization/admin/resources/export?culture={Uri.EscapeDataString(_targetCulture)}";
            if (!string.IsNullOrWhiteSpace(_selectedModule))
                url += $"&moduleId={Uri.EscapeDataString(_selectedModule)}";

            var dict = await Http.GetFromJsonAsync<Dictionary<string, string>>(url, JsonOptions) ?? [];
            var json = JsonSerializer.Serialize(dict, JsonOptions);
            var fileName = string.IsNullOrWhiteSpace(_selectedModule)
                ? $"{_targetCulture}.json"
                : $"{_targetCulture}.{_selectedModule}.json";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            using var streamRef = new DotNetStreamReference(stream);
            await JS.InvokeVoidAsync("blazorDownloadFile", fileName, "application/json", streamRef);
            Toast.Success(L.GetString("localization.toast.exported", _targetCulture, dict.Count));
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

    private void OpenImportDialog()
    {
        _importJson = string.Empty;
        _importDialogOpen = true;
    }

    private async Task ImportJsonAsync()
    {
        _busy = true;
        _error = null;
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(_importJson, JsonOptions);
            if (dict is null || dict.Count == 0)
                throw new InvalidOperationException("JSON 内容为空。");

            var mod = string.IsNullOrWhiteSpace(_selectedModule) ? LocalizationDefaults.PlatformModuleId : _selectedModule;
            var resp = await Http.PostAsJsonAsync(
                "api/localization/admin/resources/import",
                new ImportResourcesRequest(_targetCulture, mod, dict),
                JsonOptions);
            await EnsureSuccessAsync(resp);

            _importDialogOpen = false;
            await RefreshGridAsync();
            await FrontendLoc.InvalidateAsync(_targetCulture);
            Toast.Success(L["localization.toast.imported"]);
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

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode)
            return;

        var body = await resp.Content.ReadAsStringAsync();
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var msg))
                    throw new InvalidOperationException(msg.GetString());
            }
            catch (JsonException)
            {
                throw new InvalidOperationException(body);
            }
        }
        resp.EnsureSuccessStatusCode();
    }
}

