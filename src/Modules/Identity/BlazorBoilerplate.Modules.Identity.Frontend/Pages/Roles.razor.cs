using Bit.BlazorUI;
using BlazorBoilerplate.Application.Localization;
using BlazorBoilerplate.Shared.Features.Identity;
using BlazorBoilerplate.Web.Shared.Localization;
using BlazorBoilerplate.Web.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace BlazorBoilerplate.Modules.Identity.Frontend.Pages;

public partial class Roles
{
    [Inject]
    private BlazorBoilerplateApiClient Api { get; set; } = default!;

    [Inject]
    private ILogger<Roles> Logger { get; set; } = default!;

    [Inject]
    private ToastService Toast { get; set; } = default!;

    [Inject]
    private ILocalizationService L { get; set; } = default!;

    [Inject]
    private FrontendLocalizationService FrontendLoc { get; set; } = default!;

    private List<IdentityRoleDto> _roles = [];
    private List<IdentityMenuDto> _menus = [];
    private List<BitNavItem> _roleNavItems = [];
    private IdentityRoleDto? _selected;
    private HashSet<Guid> _selectedMenuIds = [];
    private Dictionary<Guid, bool> _menuSelection = [];
    private bool _menuDirty;
    private List<PermissionDefinitionDto> _permissionCatalog = [];
    private Dictionary<string, bool> _permissionSelection = [];
    private bool _permissionDirty;
    private bool _loading = true;
    private string? _error;
    private string _search = string.Empty;
    private string NewRoleName = string.Empty;
    private string EditName = string.Empty;
    private string? EditDescription;
    private bool _deleteDialogOpen;

    protected override async Task OnInitializedAsync()
    {
        await FrontendLoc.EnsureLoadedAsync();
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _error = null;
        _loading = true;
        try
        {
            _roles = [.. await Api.GetListAsync<IdentityRoleDto>("api/identity/roles")];
            _menus = [.. await Api.GetListAsync<IdentityMenuDto>("api/identity/menus")];
            if (_selected is not null)
                _selected = _roles.FirstOrDefault(r => r.Id == _selected.Id);
            RebuildNav();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            Logger.LogWarning(ex, "加载角色列表失败");
        }
        finally
        {
            _loading = false;
        }
    }

    private void RebuildNav()
    {
        var query = _search.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _roles
            : [.. _roles.Where(r => $"{r.Name}|{r.Description}".Contains(query, StringComparison.OrdinalIgnoreCase))];
        _roleNavItems = [.. filtered.Select(r => new BitNavItem
        {
            Key = r.Id.ToString(),
            Text = r.Name,
            Description = r.Description,
            Data = r
        })];
    }

    private async Task SelectRoleAsync(BitNavItem item)
    {
        if (item?.Data is not IdentityRoleDto role)
            return;
        _selected = role;
        _error = null;
        EditName = role.Name;
        EditDescription = role.Description;
        try
        {
            _selectedMenuIds = [.. await Api.GetListAsync<Guid>($"api/identity/roles/{role.Id}/menus")];
            _menuSelection = _menus.ToDictionary(m => m.Id, m => _selectedMenuIds.Contains(m.Id));
            _menuDirty = false;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }

        // 选中角色时加载权限目录 + 该角色已授予的权限码（差量勾选）。
        try
        {
            await LoadPermissionsAsync(role.Id);
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private static string GetPermissionLabel(PermissionDefinitionDto permission) =>
        string.IsNullOrWhiteSpace(permission.Name) ||
        string.Equals(permission.Name, permission.Code, StringComparison.OrdinalIgnoreCase)
            ? permission.Code
            : $"{permission.Name}（{permission.Code}）";

    private async Task LoadPermissionsAsync(Guid roleId)
    {
        _permissionCatalog = [.. await Api.GetPermissionCatalogAsync()];
        var granted = new HashSet<string>(await Api.GetRolePermissionsAsync(roleId), StringComparer.Ordinal);
        _permissionSelection = _permissionCatalog.ToDictionary(p => p.Code, p => granted.Contains(p.Code));
        _permissionDirty = false;
    }

    private void OnPermissionSelectionChanged(string code, bool selected)
    {
        _permissionSelection[code] = selected;
        _permissionDirty = true;
    }

    private async Task SavePermissionsAsync()
    {
        if (_selected is null) return;
        _error = null;
        try
        {
            var codes = _permissionSelection.Where(kv => kv.Value).Select(kv => kv.Key).ToArray();
            await Api.SetRolePermissionsAsync(_selected.Id, codes);
            _permissionDirty = false;
            Toast.Success(L.GetString("identity.roles.toast.permissionsSaved", _selected.Name));
        }
        catch (Exception ex) { _error = ex.Message; Toast.Error(L.GetString("identity.roles.toast.savePermissionsFailed", ex.Message)); }
    }

    private int _selectedMenuCount => _menuSelection.Count(kv => kv.Value);
    private int _selectedPermissionCount => _permissionSelection.Count(kv => kv.Value);

    private sealed record PermissionGroupRow(string Key, string Title, List<PermissionDefinitionDto> Items);

    private List<PermissionGroupRow> GetPermissionGroups() =>
        _permissionCatalog
            .GroupBy(ResolvePermissionGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PermissionGroupRow(g.Key, ResolveGroupTitle(g.Key), g.OrderBy(p => p.Code).ToList()))
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string ResolvePermissionGroupKey(PermissionDefinitionDto permission)
    {
        var lastDot = permission.Code.LastIndexOf('.');
        return lastDot > 0 ? permission.Code[..lastDot] : (permission.ModuleId ?? "other");
    }

    private string ResolveGroupTitle(string groupKey)
    {
        var localizationKey = "identity.roles.permissionGroup." + groupKey.Replace('.', '_');
        return L[localizationKey];
    }

    private void TogglePermissionGroup(List<PermissionDefinitionDto> items, bool selected)
    {
        foreach (var item in items)
            _permissionSelection[item.Code] = selected;
        _permissionDirty = true;
    }

    private sealed record MenuTreeRow(IdentityMenuDto Menu, int Depth);

    private List<MenuTreeRow> GetMenuRows()
    {
        var byId = _menus.ToDictionary(m => m.Id);
        var result = new List<MenuTreeRow>(_menus.Count);
        var visited = new HashSet<Guid>();

        void Append(IdentityMenuDto node, int depth)
        {
            if (!visited.Add(node.Id)) return;
            result.Add(new MenuTreeRow(node, depth));
            foreach (var child in _menus.Where(c => c.ParentId == node.Id).OrderBy(c => c.Sort).ThenBy(c => c.CreatedAtUtc))
                Append(child, depth + 1);
        }

        foreach (var root in _menus.Where(m => m.ParentId is null || !byId.ContainsKey(m.ParentId.Value)).OrderBy(m => m.Sort).ThenBy(m => m.CreatedAtUtc))
            Append(root, 0);

        return result;
    }

    private void SetAllPermissions(bool selected)
    {
        foreach (var p in _permissionCatalog)
            _permissionSelection[p.Code] = selected;
        _permissionDirty = true;
    }

    private void SetAllMenus(bool selected)
    {
        foreach (var m in _menus)
            _menuSelection[m.Id] = selected;
        _menuDirty = true;
    }

    private string GetMenuLabel(IdentityMenuDto menu)
    {
        var suffix = string.Equals(menu.Kind, "group", StringComparison.OrdinalIgnoreCase)
            ? L["identity.menus.kind.groupShort"]
            : (string.IsNullOrEmpty(menu.Path) ? null : menu.Path);
        return string.IsNullOrEmpty(suffix) ? menu.Name : $"{menu.Name} ({suffix})";
    }

    private void OnMenuSelectionChanged(Guid menuId, bool selected)
    {
        _menuSelection[menuId] = selected;
        var byId = _menus.ToDictionary(m => m.Id);

        if (selected)
        {
            // 勾选子菜单时自动勾选父级，避免父级未授权
            var cur = byId.GetValueOrDefault(menuId);
            var guard = new HashSet<Guid>();
            while (cur?.ParentId is { } pid && guard.Add(pid) && byId.TryGetValue(pid, out var parent))
            {
                _menuSelection[pid] = true;
                cur = parent;
            }
        }
        else
        {
            // 取消勾选父级时联动取消子孙节点
            var queue = new Queue<Guid>();
            queue.Enqueue(menuId);
            var guard = new HashSet<Guid> { menuId };
            while (queue.Count > 0)
            {
                var curId = queue.Dequeue();
                foreach (var child in _menus.Where(m => m.ParentId == curId))
                {
                    _menuSelection[child.Id] = false;
                    if (guard.Add(child.Id))
                        queue.Enqueue(child.Id);
                }
            }
        }
        _menuDirty = true;
    }

    private async Task CreateRoleAsync()
    {
        _error = null;
        try
        {
            var response = await Api.PostAsync("api/identity/roles", new { Name = NewRoleName, Description = (string?)null });
            EnsureOk(response);
            Toast.Success(L.GetString("identity.roles.toast.created", NewRoleName));
            NewRoleName = string.Empty;
            await RefreshAsync();
        }
        catch (Exception ex) { _error = ex.Message; Toast.Error(L.GetString("identity.roles.toast.createFailed", ex.Message)); }
    }

    private async Task SaveRoleAsync()
    {
        if (_selected is null) return;
        _error = null;
        try
        {
            var response = await Api.PutAsync($"api/identity/roles/{_selected.Id}", new { Name = EditName, Description = EditDescription });
            EnsureOk(response);
            Toast.Success(L.GetString("identity.roles.toast.saved", EditName));
            await RefreshAsync();
        }
        catch (Exception ex) { _error = ex.Message; Toast.Error(L.GetString("identity.roles.toast.saveFailed", ex.Message)); }
    }

    private async Task DeleteRoleAsync()
    {
        if (_selected is null) return;
        _error = null;
        var name = _selected.Name;
        try
        {
            var response = await Api.DeleteAsync($"api/identity/roles/{_selected.Id}");
            EnsureOk(response);
            _deleteDialogOpen = false;
            Toast.Success(L.GetString("identity.roles.toast.deleted", name));
            _selected = null;
            await RefreshAsync();
        }
        catch (Exception ex) { _error = ex.Message; Toast.Error(L.GetString("identity.roles.toast.deleteFailed", ex.Message)); }
    }

    private async Task SaveMenusAsync()
    {
        if (_selected is null) return;
        _error = null;
        try
        {
            var ids = _menuSelection.Where(kv => kv.Value).Select(kv => kv.Key).ToArray();
            var response = await Api.PutAsync($"api/identity/roles/{_selected.Id}/menus", new { Ids = ids });
            EnsureOk(response);
            _menuDirty = false;
            Toast.Success(L.GetString("identity.roles.toast.menusSaved", _selected.Name));
        }
        catch (Exception ex) { _error = ex.Message; Toast.Error(L.GetString("identity.roles.toast.saveMenusFailed", ex.Message)); }
    }

    private static void EnsureOk(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode}: {response.Content.ReadAsStringAsync().GetAwaiter().GetResult()}");
    }
}
