using System.Net.Http.Json;
using System.Text.Json;
using BlazorBoilerplate.Application.OData;
using BlazorBoilerplate.ModuleAbstractions;
using BlazorBoilerplate.Shared.Features.Account;
using BlazorBoilerplate.Shared.Features.Identity;

namespace BlazorBoilerplate.Web.Shared.Services;

/// <summary>
/// 统一调用 ApiHost 的 HttpClient 封装。
/// 注意：ApiHost（ASP.NET Core）默认以 camelCase 输出属性名，客户端必须采用同样的命名策略，
/// 否则反序列化的实体字段会全部为 null。这里统一用 CamelCase + 大小写不敏感。
/// </summary>
public sealed class BlazorBoilerplateApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<SetupStatus> GetSetupStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync("api/setup/status", cancellationToken);
        return await ReadOrThrowAsync<SetupStatus>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ModuleDescriptor>> GetModulesAsync(CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync("api/modules", cancellationToken);
        var list = await ReadOrThrowAsync<List<ModuleDescriptor>>(response, cancellationToken);
        return list ?? [];
    }

    public async Task<ModuleDescriptor?> GetModuleAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync($"api/modules/{Uri.EscapeDataString(id)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        return await ReadOrThrowAsync<ModuleDescriptor>(response, cancellationToken);
    }

    /// <summary>加载模块；version 为空时加载当前激活版本，否则切换到指定版本。</summary>
    public async Task<ModuleDescriptor> LoadModuleAsync(string id, string? version = null, CancellationToken cancellationToken = default)
    {
        var url = $"api/modules/{Uri.EscapeDataString(id)}/load";
        if (!string.IsNullOrWhiteSpace(version))
            url += $"?version={Uri.EscapeDataString(version)}";
        var response = await http.PostAsync(url, null, cancellationToken);
        return await ReadOrThrowAsync<ModuleDescriptor>(response, cancellationToken);
    }

    /// <summary>列出模块全部已安装版本（semver 降序，标记当前激活版本）。</summary>
    public Task<IReadOnlyList<ModuleVersionDto>> GetModuleVersionsAsync(string id, CancellationToken cancellationToken = default) =>
        GetListAsync<ModuleVersionDto>($"api/modules/{Uri.EscapeDataString(id)}/versions", cancellationToken);

    /// <summary>上传模块包（.zip）：归档至 S3/MinIO + 解压安装为新版本。</summary>
    public async Task<ModuleUploadResult> UploadModuleAsync(Stream zipStream, string fileName, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(zipStream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "package", fileName);
        var response = await http.PostAsync("api/modules/upload", content, cancellationToken);
        return await ReadOrThrowAsync<ModuleUploadResult>(response, cancellationToken);
    }

    /// <summary>下载模块包（.zip）：优先 S3 归档，内置模块回退为现场打包；version 为空时取当前激活版本。</summary>
    public async Task<Stream> DownloadModuleAsync(string id, string? version = null, CancellationToken cancellationToken = default)
    {
        var url = $"api/modules/{Uri.EscapeDataString(id)}/download";
        if (!string.IsNullOrWhiteSpace(version))
            url += $"?version={Uri.EscapeDataString(version)}";
        var response = await http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"下载模块包失败 ({response.StatusCode}): {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    /// <summary>开发打包：能力状态 + 模块源码工程清单（仅 ApiHost Development 环境启用）。</summary>
    public async Task<ModulePackInfo> GetPackInfoAsync(CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync("api/modules/pack/info", cancellationToken);
        return await ReadOrThrowAsync<ModulePackInfo>(response, cancellationToken);
    }

    /// <summary>开发打包：按源码工程名打包为 .zip 并返回内容流（dotnet build Release → 组包）。</summary>
    public async Task<Stream> PackModuleAsync(string source, CancellationToken cancellationToken = default)
    {
        var response = await http.PostAsJsonAsync("api/modules/pack", new { Source = source }, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"打包失败 ({response.StatusCode}): {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    public async Task UnloadModuleAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await http.PostAsync($"api/modules/{Uri.EscapeDataString(id)}/unload", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"卸载模块失败 ({response.StatusCode}): {await response.Content.ReadAsStringAsync(cancellationToken)}");
    }

    /// <summary>按 nopCommerce 风格加载顺序（依赖优先 + DisplayOrder）批量加载全部模块。</summary>
    public async Task<IReadOnlyList<ModuleDescriptor>> LoadAllModulesAsync(CancellationToken cancellationToken = default)
    {
        var response = await http.PostAsync("api/modules/load-all", null, cancellationToken);
        var list = await ReadOrThrowAsync<List<ModuleDescriptor>>(response, cancellationToken);
        return list ?? [];
    }

    /// <summary>首次运行安装向导：提交基础设施配置并完成初始化。</summary>
    public async Task<SetupStatus> CompleteSetupAsync(SetupConfiguration config, CancellationToken cancellationToken = default)
    {
        var response = await http.PostAsJsonAsync("api/setup/complete", config, JsonOptions, cancellationToken);
        return await ReadOrThrowAsync<SetupStatus>(response, cancellationToken);
    }

    // ---- Identity 模块：用户 / 角色 / 菜单管理 ----

    public Task<IReadOnlyList<IdentityUserDto>> GetUsersAsync(CancellationToken ct = default) =>
        GetListAsync<IdentityUserDto>("api/identity/users", ct);

    // ---- 认证与账户（Bluebird 风格 api/v1/[controller]/[action]）----

    private const string IdentityBase = "api/v1/Identity/";
    private const string UserBase = "api/v1/User/";
    private const string TenantBase = "api/v1/Tenant/";
    private const string PreferenceBase = "api/v1/UserPreference/";
    private const string NotificationBase = "api/v1/Notification/";
    private const string PushBase = "api/v1/PushNotification/";

    public Task<SignInResponseDto> SignInAsync(SignInRequestDto request, CancellationToken ct = default) =>
        PostSingleAsync<SignInResponseDto>(IdentityBase + "SignIn", request, ct);

    public Task<TokenResponseDto> RefreshTokenAsync(RefreshTokenRequestDto request, CancellationToken ct = default) =>
        PostSingleAsync<TokenResponseDto>(IdentityBase + "Refresh", request, ct);

    public Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken ct = default) =>
        PostVoidAsync(IdentityBase + "RevokeRefreshToken", new RefreshTokenRequestDto(refreshToken), ct);

    public Task<IReadOnlyList<string>> GetSupportedCulturesAsync(CancellationToken ct = default) =>
        GetListAsync<string>(IdentityBase + "GetSupportedCultures", ct);

    public Task<CurrentUserDto> GetCurrentUserAsync(CancellationToken ct = default) =>
        GetSingleAsync<CurrentUserDto>(UserBase + "GetCurrentUser", ct);

    public Task SignOutAsync(CancellationToken ct = default) => PostAsync(UserBase + "SignOut", new { }, ct);

    public Task<CurrentUserDto> UpdateProfileAsync(EditUserRequestDto request, CancellationToken ct = default) =>
        PutSingleAsync<CurrentUserDto>(UserBase + "Update", request, ct);

    public Task ChangePasswordAsync(ChangePasswordRequestDto request, CancellationToken ct = default) =>
        PostVoidAsync(UserBase + "ChangePassword", request, ct);

    public Task<IReadOnlyList<UserSessionDto>> GetUserSessionsAsync(CancellationToken ct = default) =>
        GetListAsync<UserSessionDto>(UserBase + "GetUserSessions", ct);

    public Task RevokeSessionAsync(Guid id, CancellationToken ct = default) =>
        PostVoidAsync(UserBase + $"RevokeSession/{id}", new { }, ct);

    public Task UpdateSessionAsync(UpdateUserSessionRequestDto request, CancellationToken ct = default) =>
        PostVoidAsync(UserBase + "UpdateSession", request, ct);

    public Task SetNotificationEnabledAsync(bool enabled, CancellationToken ct = default) =>
        PostVoidAsync(UserBase + $"SetNotificationEnabled/{enabled.ToString().ToLowerInvariant()}", new { }, ct);

    public Task<IReadOnlyList<TenantDto>> GetTenantsAsync(CancellationToken ct = default) =>
        GetListAsync<TenantDto>(UserBase + "GetTenants", ct);

    public Task LeaveTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        PostVoidAsync(UserBase + $"LeaveTenant/{tenantId}", new { }, ct);

    public Task<IReadOnlyList<PermissionDefinitionDto>> GetPermissionCatalogAsync(CancellationToken ct = default) =>
        GetListAsync<PermissionDefinitionDto>(UserBase + "GetPermissionCatalog", ct);

    public Task<IReadOnlyList<string>> GetRolePermissionsAsync(Guid roleId, CancellationToken ct = default) =>
        GetListAsync<string>(UserBase + $"GetRolePermissions/{roleId}", ct);

    public Task SetRolePermissionsAsync(Guid roleId, string[] codes, CancellationToken ct = default) =>
        PostVoidAsync(UserBase + $"SetRolePermissions/{roleId}", codes, ct);

    public Task<TenantDto?> GetCurrentTenantAsync(CancellationToken ct = default) =>
        GetSingleAsync<TenantDto?>(TenantBase + "GetCurrentTenant", ct);

    public Task<IReadOnlyList<TenantDto>> GetAllTenantsAsync(CancellationToken ct = default) =>
        GetListAsync<TenantDto>(TenantBase + "GetTenants", ct);

    public Task<TenantDto> CreateTenantAsync(TenantDto tenant, CancellationToken ct = default) =>
        PostSingleAsync<TenantDto>(TenantBase + "Create", tenant, ct);

    public Task<TenantDto> UpdateTenantAsync(TenantDto tenant, CancellationToken ct = default) =>
        PutSingleAsync<TenantDto>(TenantBase + "Update", tenant, ct);

    public Task InviteTenantUserAsync(InviteUserToTenantRequestDto request, CancellationToken ct = default) =>
        PostVoidAsync(TenantBase + "InviteUser", request, ct);

    public Task<TokenResponseDto> SwitchTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        PostSingleAsync<TokenResponseDto>(TenantBase + $"Switch/{tenantId}", new { }, ct);

    public Task<UserPreferenceDto> GetPreferencesAsync(CancellationToken ct = default) =>
        GetSingleAsync<UserPreferenceDto>(PreferenceBase + "Get", ct);

    public Task<UserPreferenceDto> SavePreferencesAsync(UserPreferenceDto request, CancellationToken ct = default) =>
        PostSingleAsync<UserPreferenceDto>(PreferenceBase + "Save", request, ct);

    public Task<IReadOnlyList<NotificationDto>> GetNotificationsAsync(CancellationToken ct = default) =>
        GetListAsync<NotificationDto>(NotificationBase + "GetNotifications", ct);

    public Task<int> GetUnreadCountAsync(CancellationToken ct = default) =>
        GetSingleAsync<int>(NotificationBase + "GetUnreadCount", ct);

    public Task MarkNotificationReadAsync(Guid id, CancellationToken ct = default) =>
        PostVoidAsync(NotificationBase + $"MarkRead/{id}", new { }, ct);

    public Task MarkAllNotificationsReadAsync(CancellationToken ct = default) =>
        PostVoidAsync(NotificationBase + "MarkAllRead", new { }, ct);

    public Task<NotificationDto> SendTestNotificationAsync(CancellationToken ct = default) =>
        PostSingleAsync<NotificationDto>(NotificationBase + "SendTestNotification", new { }, ct);

    public Task<SendEmailNotificationResultDto> SendTestEmailAsync(
        SendEmailNotificationRequestDto? request = null, CancellationToken ct = default) =>
        PostSingleAsync<SendEmailNotificationResultDto>(
            NotificationBase + "SendTestEmail",
            request ?? new SendEmailNotificationRequestDto(),
            ct);

    public Task SubscribePushAsync(PushNotificationSubscriptionDto subscription, CancellationToken ct = default) =>
        PostVoidAsync(PushBase + "Subscribe", subscription, ct);

    public Task UnsubscribePushAsync(PushNotificationSubscriptionDto subscription, CancellationToken ct = default) =>
        PostVoidAsync(PushBase + "Unsubscribe", subscription, ct);

    private async Task PostVoidAsync(string url, object body, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync(url, body, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"ApiHost 返回 {(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
    }

    private async Task<T> PostSingleAsync<T>(string url, object body, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync(url, body, JsonOptions, ct);
        return await ReadOrThrowAsync<T>(response, ct);
    }

    private async Task<T> PutSingleAsync<T>(string url, object body, CancellationToken ct)
    {
        var response = await http.PutAsJsonAsync(url, body, JsonOptions, ct);
        return await ReadOrThrowAsync<T>(response, ct);
    }

    public Task<HttpResponseMessage> PostAsync<TBody>(string url, TBody body, CancellationToken ct = default) =>
        http.PostAsJsonAsync(url, body, JsonOptions, ct);

    public Task<HttpResponseMessage> PutAsync<TBody>(string url, TBody body, CancellationToken ct = default) =>
        http.PutAsJsonAsync(url, body, JsonOptions, ct);

    public Task<HttpResponseMessage> DeleteAsync(string url, CancellationToken ct = default) =>
        http.DeleteAsync(url, ct);

    public async Task<T> GetSingleAsync<T>(string url, CancellationToken ct = default)
    {
        var response = await http.GetAsync(url, ct);
        return await ReadOrThrowAsync<T>(response, ct);
    }

    public async Task<IReadOnlyList<T>> GetListAsync<T>(string url, CancellationToken ct = default)
    {
        var response = await http.GetAsync(url, ct);
        var list = await ReadOrThrowAsync<List<T>>(response, ct);
        return list ?? [];
    }

    public async Task<ODataPage<T>> GetODataAsync<T>(string relativeUrl, ODataQuery query, CancellationToken cancellationToken = default)
    {
        var qs = BuildQueryString(query);
        var response = await http.GetAsync($"{relativeUrl}{qs}", cancellationToken);
        return await ReadOrThrowAsync<ODataPage<T>>(response, cancellationToken);
    }

    private static string BuildQueryString(ODataQuery query)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.Filter)) parts.Add("$filter=" + Uri.EscapeDataString(query.Filter));
        if (!string.IsNullOrWhiteSpace(query.OrderBy)) parts.Add("$orderby=" + Uri.EscapeDataString(query.OrderBy));
        if (query.Top is { } top) parts.Add("$top=" + top);
        if (query.Skip is { } skip) parts.Add("$skip=" + skip);
        if (query.Count) parts.Add("$count=true");
        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private static async Task<T> ReadOrThrowAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"ApiHost 返回 {(int)response.StatusCode} {response.StatusCode}: {body}");
        }
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException("ApiHost 返回了空响应体。");
    }
}

/// <summary>与 ApiHost /api/setup/status 对应的稳定 DTO（camelCase：mode / installed / devBypass）。</summary>
public sealed record SetupStatus(
    string Mode,
    bool Installed,
    bool DevBypass);

/// <summary>与 ApiHost POST /api/setup/complete 请求体对应的 DTO（camelCase）。</summary>
public sealed record SetupConfiguration(
    string AdminEmail,
    string AdminPassword,
    string? SystemName,
    string DatabaseProvider,
    string? PostgreSQL,
    string? SQLite,
    string? Redis);

/// <summary>POST /api/modules/upload 响应体（camelCase：descriptor / s3Key）。</summary>
public sealed record ModuleUploadResult(
    ModuleDescriptor Descriptor,
    string S3Key);

/// <summary>GET /api/modules/pack/info 响应体（camelCase）。</summary>
public sealed record ModulePackInfo(bool Enabled, IReadOnlyList<ModuleSourceDto> Sources);

/// <summary>开发打包的模块源码工程（camelCase）。</summary>
public sealed record ModuleSourceDto(string Name, string Directory, string Id, string Version);

/// <summary>Identity 模块用户 DTO（/api/identity/users）。</summary>
public sealed record IdentityUserDto(
    Guid Id,
    string UserName,
    string Email,
    string DisplayName,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ModifiedAtUtc);

/// <summary>Identity 模块角色 DTO（/api/identity/roles）。</summary>
public sealed record IdentityRoleDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ModifiedAtUtc);

/// <summary>Identity 模块菜单 DTO（/api/identity/menus）。</summary>
public sealed record IdentityMenuDto(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Path,
    string? Icon,
    int Sort,
    bool IsVisible,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ModifiedAtUtc);

/// <summary>Identity 模块菜单树节点 DTO（/api/identity/menus/tree）。</summary>
public sealed record IdentityMenuNodeDto(
    Guid Id,
    string Name,
    string Path,
    string? Icon,
    int Sort,
    bool IsVisible,
    IReadOnlyList<IdentityMenuNodeDto> Children);
