using System.Net.Http.Json;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace BlazorBoilerplate.Modules.Domains.Pages;

public partial class Domains
{
    public sealed record DomainRouteBindingItem(
        Guid Id,
        string Host,
        string? ModuleId,
        string? SiteKey,
        string EntryPath,
        bool AllowAdminRoutes,
        bool Enabled,
        string? Description,
        Guid? TenantId,
        DateTimeOffset CreatedAtUtc);

    public sealed record DomainResolveItem(
        bool Found,
        string? Host,
        string? ModuleId,
        string? SiteKey,
        string? EntryPath,
        bool AllowAdminRoutes,
        Guid? TenantId);

    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] private BlazorBoilerplateApiClient Api { get; set; } = default!;
    [Inject] private ILocalizationService L { get; set; } = default!;
    [Inject] private FrontendLocalizationService FrontendLoc { get; set; } = default!;
    [Inject] private ToastService Toast { get; set; } = default!;

    private List<DomainRouteBindingItem> _bindings = [];
    private List<Bit.BlazorUI.BitDropdownItem<string>> _moduleOptions = [];
    private Dictionary<string, string> _moduleDefaultRoutes = new(StringComparer.OrdinalIgnoreCase);
    private bool _loading = true;
    private bool _busy;
    private string _testHost = string.Empty;

    private bool _editOpen;
    private Guid? _editingId;
    private string _formHost = string.Empty;
    private string _formModuleId = string.Empty;
    private string _formSiteKey = string.Empty;
    private string _formEntryPath = string.Empty;
    private string _formDescription = string.Empty;
    private bool _formEnabled = true;
    private bool _formAllowAdminRoutes;

    private bool _deleteOpen;
    private DomainRouteBindingItem? _deleting;

    protected override async Task OnInitializedAsync()
    {
        await FrontendLoc.EnsureLoadedAsync();
        await LoadModulesAsync();
        await RefreshAsync();
    }

    private async Task LoadModulesAsync()
    {
        try
        {
            var modules = await Api.GetModulesAsync();
            var routableModules = modules
                .Where(m => m.DomainRoutable)
                .OrderBy(m => m.Order)
                .ToList();

            var options = new List<Bit.BlazorUI.BitDropdownItem<string>>
            {
                new() { Text = L["domains.field.moduleIdPlaceholder"], Value = string.Empty }
            };
            options.AddRange(routableModules.Select(m => new Bit.BlazorUI.BitDropdownItem<string>
            {
                Text = $"{m.DisplayTitle} ({m.Id})",
                Value = m.Id
            }));
            _moduleOptions = options;

            _moduleDefaultRoutes = routableModules
                .Select(m => new
                {
                    m.Id,
                    Entry = !string.IsNullOrWhiteSpace(m.DefaultSiteEntry)
                        ? m.DefaultSiteEntry
                        : m.Routes.FirstOrDefault()
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Entry))
                .ToDictionary(x => x.Id, x => x.Entry!, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // 模块列表读取失败不阻断域名管理页
        }
    }

    private void OnModuleSelected(string? moduleId)
    {
        _formModuleId = moduleId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_formModuleId))
        {
            _formEntryPath = "/";
            return;
        }

        if (_moduleDefaultRoutes.TryGetValue(_formModuleId, out var defaultRoute) &&
            !string.IsNullOrWhiteSpace(defaultRoute))
        {
            _formEntryPath = defaultRoute;
        }
        else
        {
            _formEntryPath = "/";
        }
    }

    private async Task RefreshAsync()
    {
        _loading = true;
        try
        {
            _bindings = await Http.GetFromJsonAsync<List<DomainRouteBindingItem>>("api/domains/bindings") ?? [];
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("domains.toast.loadFailed", ex.GetUserMessage(L)));
        }
        finally
        {
            _loading = false;
        }
    }

    private void OpenCreateModal()
    {
        _editingId = null;
        _formHost = string.Empty;
        _formModuleId = string.Empty;
        _formSiteKey = string.Empty;
        _formEntryPath = "/";
        _formDescription = string.Empty;
        _formEnabled = true;
        _formAllowAdminRoutes = false;
        _editOpen = true;
    }

    private void OpenEditModal(DomainRouteBindingItem item)
    {
        _editingId = item.Id;
        _formHost = item.Host;
        _formModuleId = item.ModuleId ?? string.Empty;
        _formSiteKey = item.SiteKey ?? string.Empty;
        _formEntryPath = item.EntryPath;
        _formDescription = item.Description ?? string.Empty;
        _formEnabled = item.Enabled;
        _formAllowAdminRoutes = item.AllowAdminRoutes;
        _editOpen = true;
    }

    private async Task SaveAsync()
    {
        var host = _formHost.Trim();
        var entryPath = _formEntryPath.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            Toast.Warning(L["domains.error.hostRequired"]);
            return;
        }
        if (string.IsNullOrWhiteSpace(entryPath))
        {
            Toast.Warning(L["domains.error.entryPathRequired"]);
            return;
        }

        _busy = true;
        try
        {
            var payload = new
            {
                Host = host,
                ModuleId = string.IsNullOrWhiteSpace(_formModuleId) ? null : _formModuleId.Trim(),
                SiteKey = string.IsNullOrWhiteSpace(_formSiteKey) ? null : _formSiteKey.Trim(),
                EntryPath = entryPath,
                AllowAdminRoutes = _formAllowAdminRoutes,
                Enabled = _formEnabled,
                Description = string.IsNullOrWhiteSpace(_formDescription) ? null : _formDescription.Trim()
            };

            HttpResponseMessage response;
            if (_editingId is null)
            {
                response = await Http.PostAsJsonAsync("api/domains/bindings", payload);
                await BlazorBoilerplateApiClient.EnsureSuccessAsync(response);
                Toast.Success(L.GetString("domains.toast.created", host));
            }
            else
            {
                response = await Http.PutAsJsonAsync($"api/domains/bindings/{_editingId}", payload);
                await BlazorBoilerplateApiClient.EnsureSuccessAsync(response);
                Toast.Success(L.GetString("domains.toast.saved", host));
            }

            _editOpen = false;
            DomainRouteResolverRegistry.Current?.InvalidateCache();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("domains.toast.saveFailed", ex.GetUserMessage(L)));
        }
        finally
        {
            _busy = false;
        }
    }

    private void OpenDeleteModal(DomainRouteBindingItem item)
    {
        _deleting = item;
        _deleteOpen = true;
    }

    private async Task ConfirmDeleteAsync()
    {
        if (_deleting is null) return;
        _busy = true;
        var host = _deleting.Host;
        try
        {
            var response = await Http.DeleteAsync($"api/domains/bindings/{_deleting.Id}");
            await BlazorBoilerplateApiClient.EnsureSuccessAsync(response);
            _deleteOpen = false;
            _deleting = null;
            DomainRouteResolverRegistry.Current?.InvalidateCache();
            Toast.Success(L.GetString("domains.toast.deleted", host));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("domains.toast.deleteFailed", ex.GetUserMessage(L)));
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task TestResolveAsync()
    {
        var host = _testHost.Trim();
        if (string.IsNullOrWhiteSpace(host)) return;
        try
        {
            var res = await Http.GetFromJsonAsync<DomainResolveItem>($"api/domains/resolve?host={Uri.EscapeDataString(host)}");
            if (res is { Found: true })
                Toast.Success(L.GetString("domains.resolve.hit", res.ModuleId ?? "-", res.SiteKey ?? "-", res.EntryPath ?? "/"));
            else
                Toast.Info(L["domains.resolve.miss"]);
        }
        catch (Exception ex)
        {
            Toast.Error(ex.GetUserMessage(L));
        }
    }
}

