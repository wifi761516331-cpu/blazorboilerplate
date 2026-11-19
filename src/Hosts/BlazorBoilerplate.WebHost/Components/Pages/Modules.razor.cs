using Bit.BlazorUI;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Web.Shared.Services;
using BlazorBoilerplate.WebHost.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BlazorBoilerplate.WebHost.Components.Pages;

public partial class Modules
{
    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private ILogger<Modules> Logger { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private NavRefreshService NavRefresh { get; set; } = default!;

    [Inject]
    private ModuleAssemblyCatalog ModuleCatalog { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    private List<ModuleDescriptor> _modules = [];
    private bool _loading = true;
    private bool _refreshing;
    private string? _loadError;
    private readonly HashSet<string> _busy = [];
    private bool _loadAllBusy;
    private string _search = string.Empty;

    private int RunningCount => _modules.Count(m => m.State == ModuleState.Running);
    private int EssentialCount => _modules.Count(m => m.IsEssential);

    private IEnumerable<ModuleDescriptor> FilteredModules =>
        string.IsNullOrWhiteSpace(_search)
            ? _modules
            : _modules.Where(m => $"{m.Name}|{m.Id}|{m.Publisher}".Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase));

    protected override async Task OnInitializedAsync()
    {
        await RefreshAsync();
        await LoadPackInfoAsync();
    }

    private async Task RefreshAsync()
    {
        _loadError = null;
        _refreshing = true;
        StateHasChanged();
        try
        {
            // 先重新扫描安装目录：手动放入的新模块只有 rescan 后才会被登记进目录，
            // 否则「刷新」只返回启动时已发现的旧列表，看起来像按钮不起作用。
            var (_, modules) = await Api.RescanModulesAsync();
            _modules = modules.OrderBy(m => m.Order).ThenBy(m => m.Name).ToList();
            await LoadVersionItemsAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "刷新模块列表失败");
            _loadError = ex.Message;
        }
        finally
        {
            _loading = false;
            _refreshing = false;
            StateHasChanged();
        }
    }

    // ---- 卡片内版本下拉（VersionCount > 1 时显示）----

    private Dictionary<string, List<BitDropdownItem<string>>> _versionItems = [];

    private async Task LoadVersionItemsAsync()
    {
        _versionItems = [];
        foreach (var m in _modules.Where(x => x.VersionCount > 1))
        {
            try
            {
                var versions = await Api.GetModuleVersionsAsync(m.Id);
                _versionItems[m.Id] = versions
                    .OrderByDescending(v => v.Version, StringComparer.OrdinalIgnoreCase)
                    .Select(v => new BitDropdownItem<string> { Text = $"v{v.Version}", Value = v.Version })
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "加载模块 {Id} 版本清单失败", m.Id);
            }
        }
    }

    private async Task SwitchCardVersionAsync(string id, string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return;
        _busy.Add(id);
        StateHasChanged();
        try
        {
            var descriptor = await Api.LoadModuleAsync(id, version);
            await ModuleCatalog.RefreshModuleAsync(id); // 运行时注入新版本前端程序集
            Toast.Success(L.GetString("platform.modules.toast.switched", descriptor.DisplayTitle, version));
            await RefreshAsync();
            NavRefresh.Notify();
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.modules.toast.switchFailed", version, ex.Message));
            await RefreshAsync();
        }
        finally
        {
            _busy.Remove(id);
            StateHasChanged();
        }
    }

    private async Task LoadAsync(string id)
    {
        _busy.Add(id);
        try
        {
            var descriptor = await Api.LoadModuleAsync(id);
            await ModuleCatalog.RefreshModuleAsync(id); // 运行时注入前端程序集
            Toast.Success(L.GetString("platform.modules.toast.loaded", descriptor.Name));
            await RefreshAsync();
            NavRefresh.Notify();
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
            Toast.Error(L.GetString("platform.modules.toast.loadFailed", id, ex.Message));
        }
        finally
        {
            _busy.Remove(id);
        }
    }

    private async Task UnloadAsync(string id)
    {
        _busy.Add(id);
        try
        {
            await Api.UnloadModuleAsync(id);
            Toast.Success(L.GetString("platform.modules.toast.unloaded", id));
            await RefreshAsync();
            NavRefresh.Notify();
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
            Toast.Error(L.GetString("platform.modules.toast.unloadFailed", id, ex.Message));
        }
        finally
        {
            _busy.Remove(id);
        }
    }

    private async Task LoadAllAsync()
    {
        _loadAllBusy = true;
        try
        {
            var result = await Api.LoadAllModulesAsync();
            var running = result.Count(m => m.State == ModuleState.Running);
            Toast.Success(L.GetString("platform.modules.toast.loadAllSuccess", running, result.Count));
            _modules = result.OrderBy(m => m.Order).ThenBy(m => m.Name).ToList();
            NavRefresh.Notify();
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
            Toast.Error(L.GetString("platform.modules.toast.loadAllFailed", ex.Message));
        }
        finally
        {
            _loadAllBusy = false;
            StateHasChanged();
        }
    }

    // ---- 开发打包（ApiHost Development 环境启用）----

    private bool _packEnabled;
    private bool _packOpen;
    private string? _packSource;
    private List<BitDropdownItem<string>> _packSources = [];
    private Dictionary<string, ModuleSourceDto> _packSourceMap = [];
    private bool _packBusy;

    private async Task LoadPackInfoAsync()
    {
        try
        {
            var info = await Api.GetPackInfoAsync();
            _packEnabled = info.Enabled;
            _packSourceMap = info.Sources.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
            _packSources = info.Sources
                .Select(s => new BitDropdownItem<string> { Text = $"{s.Name}（{s.Id} v{s.Version}）", Value = s.Name })
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "读取打包能力信息失败");
            _packEnabled = false;
        }
    }

    private async Task PackAndDownloadAsync()
    {
        if (string.IsNullOrWhiteSpace(_packSource)) return;
        _packBusy = true;
        StateHasChanged();
        try
        {
            var stream = await Api.PackModuleAsync(_packSource);
            // 命名与卡片「下载」一致：{id}-{version}.zip（如 catalog-1.0.0.zip）。
            var source = _packSourceMap.GetValueOrDefault(_packSource);
            var fileName = $"{source?.Id ?? _packSource}-{source?.Version ?? "0"}.zip";
            // 与下载模块包同理：Stream 必须包 DotNetStreamReference 再交给 JS interop。
            using var streamRef = new DotNetStreamReference(stream: stream, leaveOpen: false);
            await JS.InvokeVoidAsync("blazorDownloadFile", fileName, "application/zip", streamRef);
            Toast.Success(L.GetString("platform.modules.toast.packed", _packSource, fileName));
            _packOpen = false;
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.modules.toast.packFailed", ex.Message));
        }
        finally
        {
            _packBusy = false;
            StateHasChanged();
        }
    }

    // ---- 上传模块（S3/MinIO 归档 + 安装）----

    private bool _uploadOpen;
    private IBrowserFile? _uploadFile;
    private string? _uploadHint;

    private void OnFileSelected(InputFileChangeEventArgs e)
    {
        _uploadFile = e.File;
        if (!_uploadFile.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            _uploadHint = L.GetString("platform.modules.uploadNotZip", _uploadFile.Name);
            return;
        }
        _uploadHint = $"{_uploadFile.Name}（{_uploadFile.Size / 1024.0:F0} KB）";
    }

    private async Task UploadAsyncAsync()
    {
        if (_uploadFile is null || !_uploadFile.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            Toast.Warning(L["platform.modules.selectZipWarning"]);
            return;
        }
        try
        {
            await using var stream = _uploadFile.OpenReadStream(maxAllowedSize: 256 * 1024 * 1024);
            var result = await Api.UploadModuleAsync(stream, _uploadFile.Name);
            await ModuleCatalog.RefreshModuleAsync(result.Descriptor.Id); // 运行时注入新版本前端程序集
            Toast.Success(L.GetString("platform.modules.toast.uploaded", result.Descriptor.Name, result.Descriptor.Version));
            _uploadOpen = false;
            _uploadFile = null;
            _uploadHint = null;
            await RefreshAsync();
            NavRefresh.Notify();
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.modules.toast.uploadFailed", ex.Message));
        }
    }

    // ---- 下载模块包（.zip）----

    private readonly HashSet<string> _downloading = [];

    private async Task DownloadAsync(ModuleDescriptor m)
    {
        _downloading.Add(m.Id);
        StateHasChanged();
        try
        {
            var stream = await Api.DownloadModuleAsync(m.Id);
            var fileName = $"{m.Id}-{m.Version}.zip";
            // Stream 参数必须包 DotNetStreamReference，否则会被 JS interop 的 JSON 序列化器
            // 当普通对象处理（读 ReadTimeout 抛 "Timeouts are not supported on this stream."）。
            using var streamRef = new DotNetStreamReference(stream: stream, leaveOpen: false);
            await JS.InvokeVoidAsync("blazorDownloadFile", fileName, "application/zip", streamRef);
            Toast.Success(L.GetString("platform.modules.toast.downloading", fileName));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "下载模块包失败 {Id}", m.Id);
            Toast.Error(L.GetString("platform.modules.toast.downloadFailed", ex.Message));
        }
        finally
        {
            _downloading.Remove(m.Id);
            StateHasChanged();
        }
    }

    // ---- 版本历史与版本切换 ----

    private bool _versionsOpen;
    private string? _versionsModuleId;
    private string _versionsModuleName = string.Empty;
    private List<ModuleVersionDto> _versions = [];
    private readonly HashSet<string> _versionBusy = [];

    private async Task OpenVersionsAsync(ModuleDescriptor m)
    {
        _versionsModuleId = m.Id;
        _versionsModuleName = m.Name;
        _versionsOpen = true;
        await LoadVersionsAsync();
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            _versions = (await Api.GetModuleVersionsAsync(_versionsModuleId!)).ToList();
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.modules.toast.loadVersionsFailed", ex.Message));
        }
    }

    private async Task SwitchVersionAsync(string version)
    {
        var key = $"{_versionsModuleId}@{version}";
        _versionBusy.Add(key);
        try
        {
            var descriptor = await Api.LoadModuleAsync(_versionsModuleId!, version);
            await ModuleCatalog.RefreshModuleAsync(_versionsModuleId!); // 运行时注入新版本前端程序集
            Toast.Success(L.GetString("platform.modules.toast.versionSwitched", descriptor.Name, version));
            _versionsOpen = false; // 切换成功后关闭版本历史弹框
            await RefreshAsync();
            NavRefresh.Notify();
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("platform.modules.toast.switchFailed", version, ex.Message));
        }
        finally
        {
            _versionBusy.Remove(key);
            StateHasChanged();
        }
    }

    private static string CardClass(ModuleDescriptor m)
    {
        var state = m.State switch
        {
            ModuleState.Running => "is-running",
            ModuleState.Failed or ModuleState.Quarantined => "is-failed",
            _ => string.Empty
        };
        return m.IsEssential ? $"{state} is-essential".Trim() : state;
    }

    private static string ModuleIcon(string id) =>
        id.Contains("identity", StringComparison.OrdinalIgnoreCase) ? "Shield" :
        id.Contains("catalog", StringComparison.OrdinalIgnoreCase) ? "Shop" :
        id.Contains("attachment", StringComparison.OrdinalIgnoreCase) ? "Attach" :
        id.Contains("localization", StringComparison.OrdinalIgnoreCase) ? "Globe" : "Puzzle";

    private static BitColor StateColor(ModuleState state) => state switch
    {
        ModuleState.Running => BitColor.Success,
        ModuleState.Failed or ModuleState.Quarantined => BitColor.Error,
        ModuleState.Loading or ModuleState.Unloading or ModuleState.Draining => BitColor.Warning,
        _ => BitColor.Info
    };

    private string StateText(ModuleState state) => state switch
    {
        ModuleState.Running => L["platform.modules.state.running"],
        ModuleState.Installed => L["platform.modules.state.installed"],
        ModuleState.Disabled => L["platform.modules.state.disabled"],
        ModuleState.Loading => L["platform.modules.state.loading"],
        ModuleState.Unloading or ModuleState.Draining => L["platform.modules.state.unloading"],
        ModuleState.Updating => L["platform.modules.state.updating"],
        ModuleState.Failed => L["platform.modules.state.failed"],
        ModuleState.Quarantined => L["platform.modules.state.quarantined"],
        _ => state.ToString()
    };

    private static string StateIcon(ModuleState state) => state switch
    {
        ModuleState.Running => BitIconName.CompletedSolid,
        ModuleState.Installed or ModuleState.Disabled => BitIconName.Pause,
        ModuleState.Failed or ModuleState.Quarantined => BitIconName.ErrorBadge,
        ModuleState.Loading or ModuleState.Unloading or ModuleState.Draining or ModuleState.Updating => BitIconName.Sync,
        _ => BitIconName.Info
    };
}
