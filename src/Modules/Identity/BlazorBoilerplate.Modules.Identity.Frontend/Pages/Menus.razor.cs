using Bit.BlazorUI;
using System.Net.Http.Json;
using System.Reflection;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Shared.Features.Account;
using BlazorBoilerplate.Shared.Features.Identity;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.Modules.Identity.Frontend.Pages;

public partial class Menus
{
    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private INavRefreshService NavRefresh { get; set; } = default!;

    [Inject]
    private ILogger<Menus> Logger { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private FrontendLocalizationService FrontendLoc { get; set; } = default!;

    private sealed record IconEntry(string Name, string Value);

    private static readonly HashSet<string> PreferredIconNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Home", "ViewDashboard", "People", "SecurityGroup", "EditList", "Puzzle",
        "Shop", "Attach", "Settings", "Ringer", "Org", "BarChartVerticalFill"
    };

    private static readonly List<IconEntry> AllIcons = BuildAllIcons();

    private static List<IconEntry> BuildAllIcons()
    {
        var fields = typeof(BitIconName).GetFields(BindingFlags.Public | BindingFlags.Static);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<IconEntry>(fields.Length);

        foreach (var field in fields)
        {
            if (field.FieldType != typeof(string)) continue;
            var value = field.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(value) || !seen.Add(value)) continue;
            items.Add(new IconEntry(field.Name, value));
        }

        // 常见图标排在最前，其余按字母序排列
        return items
            .OrderByDescending(x => PreferredIconNames.Contains(x.Name) || PreferredIconNames.Contains(x.Value))
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<BitDropdownItem<string>> _kindItems = [];

    private List<IdentityMenuDto> _menus = [];
    private List<BitNavItem> _menuNavItems = [];
    private List<BitDropdownItem<Guid?>> _parentItems = [];
    private List<BitDropdownItem<string>> _permissionItems = [];
    private IdentityMenuDto? _selected;
    private bool _loading = true;
    private bool _busy;
    private bool _showHidden;
    private bool _creating;
    private string _searchText = string.Empty;
    private string? _error;
    private bool _deleteDialogOpen;
    private bool _iconPickerOpen;
    private string _iconSearch = string.Empty;

    private string EditName = string.Empty;
    private string EditPath = string.Empty;
    private string? EditIcon;
    private Guid? EditParentId;
    private int EditSort;
    private bool EditIsVisible = true;
    private string? EditModuleId;
    private string EditPermissionCode = string.Empty;
    private string EditKind = "menu";

    protected override async Task OnInitializedAsync()
    {
        await FrontendLoc.EnsureLoadedAsync();
        _kindItems =
        [
            new() { Text = L["identity.menus.kind.menu"], Value = "menu" },
            new() { Text = L["identity.menus.kind.group"], Value = "group" }
        ];
        _permissionItems = [new() { Text = L["identity.menus.noPermission"], Value = string.Empty }];
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _error = null;
        _loading = true;
        try
        {
            var menusTask = Api.GetListAsync<IdentityMenuDto>("api/identity/menus");
            var permsTask = LoadPermissionsSafeAsync();
            await Task.WhenAll(menusTask, permsTask);

            _menus = [.. await menusTask];
            if (_selected is not null)
                _selected = _menus.FirstOrDefault(m => m.Id == _selected.Id);

            RebuildNavAndParents();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            Logger.LogWarning(ex, "加载菜单失败");
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task LoadPermissionsSafeAsync()
    {
        try
        {
            var perms = await Api.GetPermissionCatalogAsync();
            _permissionItems =
            [
                new() { Text = L["identity.menus.noPermission"], Value = string.Empty },
                .. perms.OrderBy(p => p.Code).Select(p => new BitDropdownItem<string>
                {
                    Text = $"{p.Name} ({p.Code})",
                    Value = p.Code
                })
            ];
        }
        catch
        {
            // 权限目录读取失败时保留默认项，不阻断菜单管理
        }
    }

    private void RebuildNavAndParents()
    {
        var query = _searchText.Trim();
        var hasQuery = !string.IsNullOrWhiteSpace(query);

        // 先按可见性过滤；若开启搜索，匹配节点及其祖先一并保留
        var baseList = _menus.Where(m => _showHidden || m.IsVisible).ToList();
        HashSet<Guid> includedIds;
        if (!hasQuery)
        {
            includedIds = [.. baseList.Select(m => m.Id)];
        }
        else
        {
            includedIds = [];
            var byId = baseList.ToDictionary(m => m.Id);
            foreach (var m in baseList)
            {
                if (m.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    m.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    var cur = m;
                    while (cur is not null && includedIds.Add(cur.Id))
                    {
                        cur = cur.ParentId is { } pid && byId.TryGetValue(pid, out var p) ? p : null;
                    }
                }
            }
        }

        // 找出当前集合中所有顶层或父级不在当前集合中的“孤儿可见子节点”，避免父级隐藏时子节点彻底消失
        var rootNodes = baseList
            .Where(m => includedIds.Contains(m.Id) && (m.ParentId is null || !includedIds.Contains(m.ParentId.Value)))
            .OrderBy(m => m.Sort).ThenBy(m => m.CreatedAtUtc)
            .ToList();

        _menuNavItems = [.. rootNodes.Select(m => BuildNavItem(m, baseList, includedIds))];

        // 父菜单下拉：基于全量菜单，编辑时排除自己及其所有子孙节点，防止成环
        var excluded = (!_creating && _selected is not null) ? GetSubtreeIds(_selected.Id) : [];
        _parentItems =
        [
            new() { Text = L["identity.menus.topLevel"], Value = null },
            .. _menus
                .Where(m => !excluded.Contains(m.Id))
                .OrderBy(m => m.Sort).ThenBy(m => m.Name)
                .Select(m => new BitDropdownItem<Guid?>
                {
                    Text = m.IsVisible ? m.Name : $"{m.Name} ({L["identity.menus.status.hidden"]})",
                    Value = m.Id
                })
        ];
    }

    private BitNavItem BuildNavItem(IdentityMenuDto m, List<IdentityMenuDto> pool, HashSet<Guid> includedIds)
    {
        var children = pool
            .Where(c => c.ParentId == m.Id && includedIds.Contains(c.Id))
            .OrderBy(c => c.Sort).ThenBy(c => c.CreatedAtUtc)
            .Select(c => BuildNavItem(c, pool, includedIds))
            .ToList();

        return new BitNavItem
        {
            Key = m.Id.ToString(),
            Text = m.Name,
            Data = m,
            ChildItems = children
        };
    }

    private HashSet<Guid> GetSubtreeIds(Guid rootId)
    {
        var result = new HashSet<Guid> { rootId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var child in _menus.Where(m => m.ParentId == cur))
            {
                if (result.Add(child.Id))
                    queue.Enqueue(child.Id);
            }
        }
        return result;
    }

    private Task SelectMenuAsync(BitNavItem item)
    {
        if (item?.Data is not IdentityMenuDto menu)
            return Task.CompletedTask;

        _creating = false;
        _selected = menu;
        EditName = menu.Name;
        EditPath = menu.Path;
        EditIcon = menu.Icon;
        EditParentId = menu.ParentId;
        EditSort = menu.Sort;
        EditIsVisible = menu.IsVisible;
        EditModuleId = menu.ModuleId;
        EditPermissionCode = menu.PermissionCode ?? string.Empty;
        EditKind = string.IsNullOrWhiteSpace(menu.Kind) ? "menu" : menu.Kind;
        EnsurePermissionOptionExists(EditPermissionCode);
        RebuildNavAndParents();
        _error = null;
        return Task.CompletedTask;
    }

    private void EnsurePermissionOptionExists(string code)
    {
        if (!string.IsNullOrWhiteSpace(code) && _permissionItems.All(x => x.Value != code))
            _permissionItems.Add(new BitDropdownItem<string> { Text = code, Value = code });
    }

    private void OpenCreate(Guid? parentId)
    {
        _creating = true;
        _selected = null;
        EditName = string.Empty;
        EditPath = string.Empty;
        EditIcon = null;
        EditParentId = parentId;
        var siblingMax = _menus.Where(m => m.ParentId == parentId).Select(m => m.Sort).DefaultIfEmpty(0).Max();
        EditSort = siblingMax + 10;
        EditIsVisible = true;
        EditModuleId = null;
        EditPermissionCode = string.Empty;
        EditKind = "menu";
        RebuildNavAndParents();
        _error = null;
    }

    private void CancelCreate()
    {
        _creating = false;
        _error = null;
    }

    private void OpenIconPicker()
    {
        _iconSearch = string.Empty;
        _iconPickerOpen = true;
    }

    private void OnIconSearchChanged(string? value)
    {
        _iconSearch = value ?? string.Empty;
    }

    private List<IconEntry> GetFilteredIcons()
    {
        if (string.IsNullOrWhiteSpace(_iconSearch))
            return AllIcons;

        var keyword = _iconSearch.Trim();
        return AllIcons
            .Where(x => x.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                     || x.Value.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void SelectIcon(string value)
    {
        EditIcon = value;
        _iconPickerOpen = false;
    }

    private List<IdentityMenuDto> GetSiblings(Guid? parentId) =>
        _menus.Where(m => m.ParentId == parentId)
              .OrderBy(m => m.Sort).ThenBy(m => m.CreatedAtUtc)
              .ToList();

    private bool CanMoveSibling(int delta)
    {
        if (_selected is null) return false;
        var siblings = GetSiblings(_selected.ParentId);
        var idx = siblings.FindIndex(m => m.Id == _selected.Id);
        return idx >= 0 && idx + delta >= 0 && idx + delta < siblings.Count;
    }

    private async Task MoveSiblingAsync(int delta)
    {
        if (_selected is null) return;
        var siblings = GetSiblings(_selected.ParentId);
        var idx = siblings.FindIndex(m => m.Id == _selected.Id);
        var targetIdx = idx + delta;
        if (idx < 0 || targetIdx < 0 || targetIdx >= siblings.Count) return;

        var current = siblings[idx];
        var neighbor = siblings[targetIdx];
        var newCurrentSort = neighbor.Sort;
        var newNeighborSort = current.Sort;
        if (newCurrentSort == newNeighborSort)
        {
            newCurrentSort = delta < 0 ? neighbor.Sort - 1 : neighbor.Sort + 1;
        }

        _busy = true;
        try
        {
            await UpdateMenuRawAsync(current with { Sort = newCurrentSort });
            await UpdateMenuRawAsync(neighbor with { Sort = newNeighborSort });
            EditSort = newCurrentSort;
            await RefreshAsync();
            NavRefresh.Notify();
            Toast.Success(L["identity.menus.toast.sortUpdated"]);
        }
        catch (Exception ex)
        {
            Toast.Error(L.GetString("identity.menus.toast.sortFailed", ex.Message));
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task UpdateMenuRawAsync(IdentityMenuDto m)
    {
        var response = await Api.PutAsync($"api/identity/menus/{m.Id}", new
        {
            m.Name,
            m.Path,
            m.Icon,
            m.ParentId,
            m.Sort,
            m.IsVisible,
            m.ModuleId,
            m.PermissionCode,
            m.Kind
        });
        await EnsureOkAsync(response);
    }

    private async Task SaveAsync()
    {
        var name = EditName.Trim();
        var path = EditPath.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Toast.Warning(L["identity.menus.toast.nameRequired"]);
            return;
        }
        if (string.IsNullOrWhiteSpace(path))
        {
            Toast.Warning(L["identity.menus.toast.pathRequired"]);
            return;
        }
        if (!path.StartsWith('/') && !path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            path = "/" + path;
        EditPath = path;

        var payload = new
        {
            Name = name,
            Path = path,
            Icon = string.IsNullOrWhiteSpace(EditIcon) ? null : EditIcon.Trim(),
            ParentId = EditParentId,
            Sort = EditSort,
            IsVisible = EditIsVisible,
            ModuleId = string.IsNullOrWhiteSpace(EditModuleId) ? null : EditModuleId,
            PermissionCode = string.IsNullOrWhiteSpace(EditPermissionCode) ? null : EditPermissionCode,
            Kind = string.IsNullOrWhiteSpace(EditKind) ? "menu" : EditKind
        };

        _busy = true;
        _error = null;
        try
        {
            if (_creating)
            {
                var response = await Api.PostAsync("api/identity/menus", payload);
                await EnsureOkAsync(response);
                var created = await response.Content.ReadFromJsonAsync<IdentityMenuDto>();
                _creating = false;
                _selected = created;
                Toast.Success(L.GetString("identity.menus.toast.created", name));
            }
            else
            {
                if (_selected is null) return;
                var response = await Api.PutAsync($"api/identity/menus/{_selected.Id}", payload);
                await EnsureOkAsync(response);
                Toast.Success(L.GetString("identity.menus.toast.saved", name));
            }

            await RefreshAsync();
            NavRefresh.Notify();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            Toast.Error(L.GetString("identity.menus.toast.saveFailed", ex.Message));
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (_selected is null) return;
        _error = null;
        var name = _selected.Name;
        try
        {
            var response = await Api.DeleteAsync($"api/identity/menus/{_selected.Id}");
            await EnsureOkAsync(response);
            _deleteDialogOpen = false;
            Toast.Success(L.GetString("identity.menus.toast.deleted", name));
            _selected = null;
            await RefreshAsync();
            NavRefresh.Notify();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            Toast.Error(L.GetString("identity.menus.toast.deleteFailed", ex.Message));
        }
    }

    private static async Task EnsureOkAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}
