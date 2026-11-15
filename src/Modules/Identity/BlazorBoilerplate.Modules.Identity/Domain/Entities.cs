namespace BlazorBoilerplate.Modules.Identity.Domain;

/// <summary>
/// 平台用户。唯一性（UserName/Email）由 UserService 在代码层校验，
/// 数据库只建非唯一索引 —— 与 Nova 一致：不依赖数据库级约束。
/// </summary>
public sealed class User
{
    private User() { }

    private User(string userName, string email, string displayName, string passwordHash)
    {
        Id = Guid.CreateVersion7();
        UserName = userName;
        Email = email;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        IsActive = true;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string UserName { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    public static User Create(string userName, string email, string displayName, string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return new User(userName.Trim(), email.Trim(), string.IsNullOrWhiteSpace(displayName) ? userName : displayName, passwordHash);
    }

    public void Update(string email, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        Email = email.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? UserName : displayName;
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }

    public void SetPassword(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Activate() { IsActive = true; ModifiedAtUtc = DateTimeOffset.UtcNow; }
    public void Deactivate() { IsActive = false; ModifiedAtUtc = DateTimeOffset.UtcNow; }
}

/// <summary>角色。IsSystem 的内置角色不允许删除/改名，由 RoleService 保护。</summary>
public sealed class Role
{
    private Role() { }

    private Role(string name, string? description, bool isSystem)
    {
        Id = Guid.CreateVersion7();
        Name = name;
        Description = description;
        IsSystem = isSystem;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public bool IsSystem { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    public static Role Create(string name, string? description = null, bool isSystem = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Role(name.Trim(), description?.Trim(), isSystem);
    }

    public void Update(string name, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Description = description?.Trim();
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// 菜单（自引用树：ParentId 为标量列，不配置 EF 关系 → 不生成数据库外键）。
/// 树形结构由 MenuService 在代码层构建与校验（循环引用/父节点存在性）。
/// </summary>
public sealed class Menu
{
    private Menu() { }

    private Menu(string name, string path, string? icon, Guid? parentId, int sort,
        string? moduleId, string? permissionCode, string kind)
    {
        Id = Guid.CreateVersion7();
        Name = name;
        Path = path;
        Icon = icon;
        ParentId = parentId;
        Sort = sort;
        IsVisible = true;
        ModuleId = moduleId;
        PermissionCode = permissionCode;
        Kind = kind;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = default!;
    public string Path { get; private set; } = default!;
    public string? Icon { get; private set; }
    public int Sort { get; private set; }
    public bool IsVisible { get; private set; }
    /// <summary>归属模块 Id（可空表示平台核心菜单）。</summary>
    public string? ModuleId { get; private set; }
    /// <summary>访问该菜单所需的权限码；为空表示不做按钮级校验。</summary>
    public string? PermissionCode { get; private set; }
    /// <summary>菜单类型："menu"（默认）/ "button"。</summary>
    public string Kind { get; private set; } = "menu";
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    // moduleId / permissionCode / kind 均为可选参数：旧调用点（安装向导种子、旧前端）
    // 不传时仍等价于「平台菜单」，保持源码兼容。
    public static Menu Create(string name, string path, string? icon = null, Guid? parentId = null, int sort = 0,
        string? moduleId = null, string? permissionCode = null, string kind = "menu")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new Menu(name.Trim(), path.Trim(), icon?.Trim(), parentId, sort,
            moduleId?.Trim(), permissionCode?.Trim(), NormalizeKind(kind));
    }

    public void Update(string name, string path, string? icon, Guid? parentId, int sort, bool isVisible,
        string? moduleId = null, string? permissionCode = null, string kind = "menu")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Name = name.Trim();
        Path = path.Trim();
        Icon = icon?.Trim();
        ParentId = parentId;
        Sort = sort;
        IsVisible = isVisible;
        ModuleId = moduleId?.Trim();
        PermissionCode = permissionCode?.Trim();
        Kind = NormalizeKind(kind);
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string NormalizeKind(string kind) =>
        kind is "menu" or "button" or "group" ? kind : "menu";
}

/// <summary>用户-角色关联（无外键：UserId/RoleId 为标量列，完整性由服务层保证）。</summary>
public sealed class UserRole
{
    private UserRole() { }

    public UserRole(Guid userId, Guid roleId)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        RoleId = roleId;
        AssignedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public DateTimeOffset AssignedAtUtc { get; private set; }
}

/// <summary>角色-菜单关联（角色可访问的菜单授权）。</summary>
public sealed class RoleMenu
{
    private RoleMenu() { }

    public RoleMenu(Guid roleId, Guid menuId)
    {
        Id = Guid.CreateVersion7();
        RoleId = roleId;
        MenuId = menuId;
    }

    public Guid Id { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid MenuId { get; private set; }
}

// ---- 以下为按钮级权限 / 多租户 / 会话 / 偏好 / 通知所需实体 ----
// 与上面 5 个实体保持同一约定：Guid v7 主键、private setter + 静态 Create / 实例 Update，
// 所有跨实体引用均为标量 Guid 列（无导航、无外键），完整性由 Services 层在代码层校验。

/// <summary>租户。成员关系（TenantUser）与存在性由 TenantService 校验。</summary>
public sealed class Tenant
{
    private Tenant() { }

    private Tenant(string name, string? title, string? domain)
    {
        Id = Guid.CreateVersion7();
        Name = name;
        Title = title;
        Domain = domain;
        IsActive = true;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Title { get; private set; }
    public string? Domain { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Tenant Create(string name, string? title = null, string? domain = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Tenant(name.Trim(), title?.Trim(), domain?.Trim());
    }

    public void Update(string name, string? title, string? domain, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Title = title?.Trim();
        Domain = domain?.Trim();
        IsActive = isActive;
    }
}

/// <summary>租户成员。邀请（Accepted=false）与接受由 TenantService 维护。</summary>
public sealed class TenantUser
{
    private TenantUser() { }

    private TenantUser(Guid tenantId, Guid userId, bool isOwner, bool accepted)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        UserId = userId;
        Accepted = accepted;
        IsOwner = isOwner;
        InvitedAtUtc = DateTimeOffset.UtcNow;
        AcceptedAtUtc = accepted ? InvitedAtUtc : null;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public bool Accepted { get; private set; }
    public bool IsOwner { get; private set; }
    public DateTimeOffset InvitedAtUtc { get; private set; }
    public DateTimeOffset? AcceptedAtUtc { get; private set; }

    public static TenantUser Create(Guid tenantId, Guid userId, bool isOwner = false, bool accepted = false) =>
        new(tenantId, userId, isOwner, accepted);

    public void Accept()
    {
        if (Accepted) return;
        Accepted = true;
        AcceptedAtUtc = DateTimeOffset.UtcNow;
    }
}

/// <summary>用户会话（登录会话 / 刷新令牌载体）。撤销为软删除：只置 RevokedAtUtc。</summary>
public sealed class UserSession
{
    private UserSession() { }

    private UserSession(Guid userId, Guid? tenantId, string? ip, string? deviceInfo,
        string? cultureName, string? timeZone, bool notificationsEnabled, string? refreshToken)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        TenantId = tenantId;
        IP = ip;
        DeviceInfo = deviceInfo;
        CultureName = cultureName;
        TimeZone = timeZone;
        NotificationsEnabled = notificationsEnabled;
        RefreshToken = refreshToken;
        StartedOnUtc = DateTimeOffset.UtcNow;
        RenewedOnUtc = StartedOnUtc;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? TenantId { get; private set; }
    public string? IP { get; private set; }
    public string? DeviceInfo { get; private set; }
    public string? CultureName { get; private set; }
    public string? TimeZone { get; private set; }
    public bool NotificationsEnabled { get; private set; }
    public string? RefreshToken { get; private set; }
    public DateTimeOffset StartedOnUtc { get; private set; }
    public DateTimeOffset RenewedOnUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public static UserSession Create(Guid userId, Guid? tenantId = null, string? ip = null, string? deviceInfo = null,
        string? cultureName = null, string? timeZone = null, bool notificationsEnabled = false, string? refreshToken = null) =>
        new(userId, tenantId, ip, deviceInfo, cultureName, timeZone, notificationsEnabled, refreshToken);

    /// <summary>局部更新：null 表示「不修改」该字段，并刷新 RenewedOnUtc。</summary>
    public void Update(string? cultureName, string? timeZone, bool? notificationsEnabled, string? deviceInfo)
    {
        if (cultureName is not null) CultureName = cultureName;
        if (timeZone is not null) TimeZone = timeZone;
        if (notificationsEnabled is { } enabled) NotificationsEnabled = enabled;
        if (deviceInfo is not null) DeviceInfo = deviceInfo;
        RenewedOnUtc = DateTimeOffset.UtcNow;
    }

    public void SetNotificationsEnabled(bool enabled)
    {
        NotificationsEnabled = enabled;
        RenewedOnUtc = DateTimeOffset.UtcNow;
    }

    public void Renew(string? refreshToken = null)
    {
        if (refreshToken is not null) RefreshToken = refreshToken;
        RenewedOnUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>切换租户：只改会话的租户归属，不动任何数据库结构。</summary>
    public void SwitchTenant(Guid? tenantId)
    {
        TenantId = tenantId;
        RenewedOnUtc = DateTimeOffset.UtcNow;
    }

    public void Revoke()
    {
        RevokedAtUtc = DateTimeOffset.UtcNow;
        RefreshToken = null;
    }
}

/// <summary>用户偏好（跨设备同步）。默认值与共享 UserPreferenceDto 保持一致。</summary>
public sealed class UserPreference
{
    private UserPreference() { }

    private UserPreference(Guid userId, string culture, string timeZone, string theme,
        string? accentColor, bool notificationsEnabled)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        Culture = culture;
        TimeZone = timeZone;
        Theme = theme;
        AccentColor = accentColor;
        NotificationsEnabled = notificationsEnabled;
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Culture { get; private set; } = default!;
    public string TimeZone { get; private set; } = default!;
    public string Theme { get; private set; } = default!;
    public string? AccentColor { get; private set; }
    public bool NotificationsEnabled { get; private set; }
    public DateTimeOffset ModifiedAtUtc { get; private set; }

    public static UserPreference Create(Guid userId, string culture = "zh-CN", string timeZone = "Asia/Shanghai",
        string theme = "light", string? accentColor = null, bool notificationsEnabled = false) =>
        new(userId, culture, timeZone, theme, accentColor, notificationsEnabled);

    public void Update(string culture, string timeZone, string theme, string? accentColor, bool notificationsEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZone);
        ArgumentException.ThrowIfNullOrWhiteSpace(theme);
        Culture = culture;
        TimeZone = timeZone;
        Theme = theme;
        AccentColor = accentColor;
        NotificationsEnabled = notificationsEnabled;
        ModifiedAtUtc = DateTimeOffset.UtcNow;
    }
}

/// <summary>站内通知。已读为软状态，由 NotificationService 保证只能操作自己的通知。</summary>
public sealed class Notification
{
    private Notification() { }

    private Notification(Guid userId, string title, string message, string? pageUrl)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        Title = title;
        Message = message;
        IsRead = false;
        PageUrl = pageUrl;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Title { get; private set; } = default!;
    public string Message { get; private set; } = default!;
    public bool IsRead { get; private set; }
    public string? PageUrl { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ReadAtUtc { get; private set; }

    public static Notification Create(Guid userId, string title, string message, string? pageUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return new Notification(userId, title.Trim(), message, pageUrl?.Trim());
    }

    public void MarkRead()
    {
        if (IsRead) return;
        IsRead = true;
        ReadAtUtc = DateTimeOffset.UtcNow;
    }
}

/// <summary>推送订阅记录。匿名也可订阅，故 UserId 可空；不接外部推送网关。</summary>
public sealed class PushNotificationSubscription
{
    private PushNotificationSubscription() { }

    private PushNotificationSubscription(string deviceId, string platform, string pushChannel,
        string? endpoint, string? p256dh, string? auth, Guid? userId)
    {
        Id = Guid.CreateVersion7();
        DeviceId = deviceId;
        Platform = platform;
        PushChannel = pushChannel;
        Endpoint = endpoint;
        P256dh = p256dh;
        Auth = auth;
        UserId = userId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string DeviceId { get; private set; } = default!;
    public string Platform { get; private set; } = default!;
    public string PushChannel { get; private set; } = default!;
    public string? Endpoint { get; private set; }
    public string? P256dh { get; private set; }
    public string? Auth { get; private set; }
    public Guid? UserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static PushNotificationSubscription Create(string deviceId, string platform, string pushChannel,
        string? endpoint = null, string? p256dh = null, string? auth = null, Guid? userId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(pushChannel);
        return new PushNotificationSubscription(deviceId.Trim(), platform.Trim(), pushChannel.Trim(),
            endpoint, p256dh, auth, userId);
    }

    public void Update(string platform, string pushChannel, string? endpoint, string? p256dh, string? auth, Guid? userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(pushChannel);
        Platform = platform.Trim();
        PushChannel = pushChannel.Trim();
        Endpoint = endpoint;
        P256dh = p256dh;
        Auth = auth;
        UserId = userId;
    }
}

/// <summary>权限定义目录。Kind = "menu"（菜单级）/ "button"（按钮级）；Code 唯一性由服务层保证。</summary>
public sealed class PermissionDefinition
{
    private PermissionDefinition() { }

    private PermissionDefinition(string code, string name, string? moduleId, string? menuPath, string kind)
    {
        Id = Guid.CreateVersion7();
        Code = code;
        Name = name;
        ModuleId = moduleId;
        MenuPath = menuPath;
        Kind = kind;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? ModuleId { get; private set; }
    public string? MenuPath { get; private set; }
    public string Kind { get; private set; } = default!;

    public static PermissionDefinition Create(string code, string name, string? moduleId = null,
        string? menuPath = null, string kind = "button")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new PermissionDefinition(code.Trim(), name.Trim(), moduleId?.Trim(), menuPath?.Trim(), NormalizeKind(kind));
    }

    public void Update(string name, string? moduleId, string? menuPath, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        ModuleId = moduleId?.Trim();
        MenuPath = menuPath?.Trim();
        Kind = NormalizeKind(kind);
    }

    private static string NormalizeKind(string kind) =>
        kind is "menu" or "button" ? kind : "button";
}

/// <summary>角色-权限码关联（无外键）。差量更新由 RoleService.AssignPermissionCodesAsync 保证。</summary>
public sealed class RolePermission
{
    private RolePermission() { }

    public RolePermission(Guid roleId, string code)
    {
        Id = Guid.CreateVersion7();
        RoleId = roleId;
        Code = code;
    }

    public Guid Id { get; private set; }
    public Guid RoleId { get; private set; }
    public string Code { get; private set; } = default!;
}
