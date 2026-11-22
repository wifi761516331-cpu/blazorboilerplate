using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Modules.Cms.Frontend.Models;
using BlazorBoilerplate.Modules.Cms.Frontend.Services;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlazorBoilerplate.Modules.Cms.Frontend.Pages;

public partial class CmsSites
{
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

    private List<CmsSiteDto> _sites = [];
    private bool _loading = true;
    private bool _busy;

    private bool _createOpen;
    private Guid? _editingSiteId;
    private string _newName = string.Empty;
    private string _newKey = string.Empty;
    private string _newTitle = string.Empty;
    private string _newDesc = string.Empty;
    private string _newLogoUrl = string.Empty;
    private string _newFaviconUrl = string.Empty;
    private string _newDefaultCulture = "zh-CN";
    private string _newSupportedCultures = "zh-CN,en-US";
    private bool _logoMediaOpen;
    private bool _pickingFavicon;

    private bool _leadsOpen;
    private CmsSiteDto? _leadsSite;
    private List<CmsLeadDto> _leads = [];

    protected override async Task OnInitializedAsync()
    {
        await FrontendLoc.EnsureLoadedAsync();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            _sites = await CmsApi.ListSitesAsync() ?? [];
        }
        catch (Exception ex)
        {
            Toast.Error($"加载站点失败：{ex.Message}");
        }
        finally
        {
            _loading = false;
        }
    }

    private void OpenCreateModal()
    {
        _editingSiteId = null;
        _newName = string.Empty;
        _newKey = string.Empty;
        _newTitle = string.Empty;
        _newDesc = string.Empty;
        _newLogoUrl = string.Empty;
        _newFaviconUrl = string.Empty;
        _newDefaultCulture = "zh-CN";
        _newSupportedCultures = "zh-CN,en-US";
        _createOpen = true;
    }

    private void OpenEditModal(CmsSiteDto site)
    {
        _editingSiteId = site.Id;
        _newName = site.Name;
        _newKey = site.SiteKey;
        _newTitle = site.Title;
        _newDesc = site.Description ?? string.Empty;
        _newLogoUrl = site.LogoUrl ?? string.Empty;
        _newFaviconUrl = site.FaviconUrl ?? string.Empty;
        _newDefaultCulture = site.DefaultCulture;
        _newSupportedCultures = string.Join(",", site.SupportedCultures);
        _createOpen = true;
    }

    private async Task SaveSiteAsync()
    {
        if (string.IsNullOrWhiteSpace(_newName) || string.IsNullOrWhiteSpace(_newKey) || string.IsNullOrWhiteSpace(_newTitle))
        {
            Toast.Warning("请填写站点名称、SiteKey 与浏览器标题。");
            return;
        }
        _busy = true;
        try
        {
            var cultures = _newSupportedCultures
                .Split([',', '，', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var req = new UpsertCmsSiteRequest(_newName, _newKey, _newTitle, _newDesc, _newLogoUrl, _newFaviconUrl, DefaultCulture: _newDefaultCulture, SupportedCultures: cultures);
            if (_editingSiteId.HasValue)
            {
                await CmsApi.UpdateSiteAsync(_editingSiteId.Value, req);
                await LoadAsync();
                Toast.Success($"已更新站点 {_newName}");
            }
            else
            {
                var created = await CmsApi.CreateSiteAsync(req);
                _sites.Add(created);
                Toast.Success($"已创建站点 {created.Name}");
            }
            _createOpen = false;
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

    private void OpenSiteMediaPicker(bool isFavicon)
    {
        _pickingFavicon = isFavicon;
        _logoMediaOpen = true;
    }

    private void HandleSiteMediaSelected(string url)
    {
        if (_pickingFavicon) _newFaviconUrl = url;
        else _newLogoUrl = url;
    }

    private async Task PublishAsync(CmsSiteDto site)
    {
        await CmsApi.PublishSiteAsync(site.Id);
        Toast.Success($"站点 {site.Name} 已发布");
        await LoadAsync();
    }

    private async Task UnpublishAsync(CmsSiteDto site)
    {
        await CmsApi.UnpublishSiteAsync(site.Id);
        Toast.Info($"站点 {site.Name} 已下线");
        await LoadAsync();
    }

    private async Task DeleteSiteAsync(CmsSiteDto site)
    {
        try
        {
            await CmsApi.DeleteSiteAsync(site.Id);
            _sites.RemoveAll(s => s.Id == site.Id);
            Toast.Success($"已删除站点 {site.Name}");
        }
        catch (Exception ex)
        {
            Toast.Error($"删除失败：{ex.Message}");
        }
    }

    private async Task CloneSiteAsync(CmsSiteDto site)
    {
        _busy = true;
        try
        {
            var cloned = await CmsApi.CloneSiteAsync(site.Id);
            _sites.Add(cloned);
            Toast.Success($"已复制为新站点「{cloned.Name}」，可进入可视化设计器自由编辑");
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

    private Task OpenPreview(CmsSiteDto site) =>
        JS.InvokeVoidAsync("open", $"/site?siteKey={Uri.EscapeDataString(site.SiteKey)}", "_blank", "noopener").AsTask();

    private async Task OpenLeadsAsync(CmsSiteDto site)
    {
        _leadsSite = site;
        _leadsOpen = true;
        _leads = await CmsApi.ListLeadsAsync(site.Id) ?? [];
    }

    private async Task MarkLeadAsync(CmsLeadDto lead, string status)
    {
        await CmsApi.UpdateLeadStatusAsync(lead.Id, status);
        if (_leadsSite is not null)
            _leads = await CmsApi.ListLeadsAsync(_leadsSite.Id) ?? [];
    }

    private async Task DeleteLeadAsync(CmsLeadDto lead)
    {
        await CmsApi.DeleteLeadAsync(lead.Id);
        _leads.RemoveAll(l => l.Id == lead.Id);
    }
}
