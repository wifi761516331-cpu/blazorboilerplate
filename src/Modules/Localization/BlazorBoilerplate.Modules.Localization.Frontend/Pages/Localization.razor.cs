using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Bit.BlazorUI;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Modules.Localization.Models;
using BlazorBoilerplate.Web.Shared.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlazorBoilerplate.Modules.Localization.Pages;

public partial class Localization
{
    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] private ILocalizationService L { get; set; } = default!;
    [Inject] private FrontendLocalizationService FrontendLoc { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
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
    private int _totalResources;
    private List<ResourceRowModel> _resources = [];

    private bool _importDialogOpen;
    private string _importJson = string.Empty;

    private int TotalPages => Math.Max(1, (int)Math.Ceiling(_totalResources / (double)_pageSize));

    protected override async Task OnInitializedAsync()
    {
        await FrontendLoc.EnsureLoadedAsync();

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
            await LoadResourcesAsync();
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
            await LoadResourcesAsync();
        }
    }

    private async Task LoadLanguagesAsync()
    {
        _error = null;
        try
        {
            _languages = await Http.GetFromJsonAsync<List<LanguageDto>>("api/localization/admin/languages", JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            _error = ex.Message;
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

    private async Task LoadResourcesAsync()
    {
        _error = null;
        try
        {
            var url = $"api/localization/admin/resources?culture={Uri.EscapeDataString(_targetCulture)}&page={_page}&pageSize={_pageSize}&onlyMissing={_onlyMissing}";
            if (!string.IsNullOrWhiteSpace(_selectedModule))
                url += $"&moduleId={Uri.EscapeDataString(_selectedModule)}";
            if (!string.IsNullOrWhiteSpace(_keyword))
                url += $"&keyword={Uri.EscapeDataString(_keyword)}";

            var result = await Http.GetFromJsonAsync<PagedResourcesResult>(url, JsonOptions);
            _totalResources = result?.TotalCount ?? 0;
            _resources = result?.Items.ToList() ?? [];
        }
        catch (Exception ex)
        {
            _error = ex.Message;
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

    private async Task OnTargetCultureChangedAsync(ChangeEventArgs e)
    {
        _targetCulture = e.Value?.ToString() ?? LocalizationDefaults.DefaultChineseCulture;
        _page = 1;
        SyncUrlQuery();
        await LoadResourcesAsync();
    }

    private async Task OnModuleChangedAsync(ChangeEventArgs e)
    {
        _selectedModule = e.Value?.ToString() ?? string.Empty;
        _page = 1;
        SyncUrlQuery();
        await LoadResourcesAsync();
    }

    private async Task OnKeywordChangedAsync(string? value)
    {
        _keyword = value ?? string.Empty;
        _page = 1;
        await LoadResourcesAsync();
    }

    private async Task OnOnlyMissingChangedAsync(bool value)
    {
        _onlyMissing = value;
        _page = 1;
        SyncUrlQuery();
        await LoadResourcesAsync();
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

            await FrontendLoc.InvalidateAsync(_targetCulture);
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
            await LoadResourcesAsync();
            await FrontendLoc.InvalidateAsync(_targetCulture);
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
            await LoadResourcesAsync();
            await FrontendLoc.InvalidateAsync(_targetCulture);
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
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", json);
            _notice = $"{_targetCulture} JSON ({dict.Count})";
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
            await LoadResourcesAsync();
            await FrontendLoc.InvalidateAsync(_targetCulture);
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

    private async Task OnPageSizeChangedAsync(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var size) && size is >= 10 and <= 200)
        {
            _pageSize = size;
            _page = 1;
            await LoadResourcesAsync();
        }
    }

    private async Task PrevPageAsync()
    {
        if (_page <= 1) return;
        _page--;
        await LoadResourcesAsync();
    }

    private async Task NextPageAsync()
    {
        if (_page >= TotalPages) return;
        _page++;
        await LoadResourcesAsync();
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

