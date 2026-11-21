using System.Text.Json;
using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.EntityFrameworkCore;

namespace BlazorBoilerplate.Persistence.Platform;

/// <summary>基于 EF Core 的强类型模块配置服务（未配置数据库前自动回退到 fallback）。</summary>
public sealed class EfSettingService(ISettingService fallback) : ISettingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T> LoadSettingAsync<T>(Guid? tenantId = null, CancellationToken cancellationToken = default)
        where T : class, ISettings, new()
    {
        await using var db = await PlatformDbAccessor.CreateReadyDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (db is null)
            return await fallback.LoadSettingAsync<T>(tenantId, cancellationToken).ConfigureAwait(false);

        var prefix = typeof(T).Name + ".";
        var scopeTenant = tenantId ?? Guid.Empty;
        var rows = await db.Settings.AsNoTracking()
            .Where(s => s.Key.StartsWith(prefix) && (s.TenantId == Guid.Empty || s.TenantId == scopeTenant))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var globalMap = rows.Where(r => r.TenantId == Guid.Empty).ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);
        var tenantMap = scopeTenant != Guid.Empty
            ? rows.Where(r => r.TenantId == scopeTenant).ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase)
            : null;

        var instance = new T();
        foreach (var prop in typeof(T).GetProperties())
        {
            if (!prop.CanWrite) continue;
            var key = prefix + prop.Name;
            string? raw = null;
            if (tenantMap is not null && tenantMap.TryGetValue(key, out var tv))
                raw = tv;
            else if (globalMap.TryGetValue(key, out var gv))
                raw = gv;

            if (raw is null) continue;
            try
            {
                var parsed = JsonSerializer.Deserialize(raw, prop.PropertyType, JsonOptions);
                prop.SetValue(instance, parsed);
            }
            catch
            {
                // 忽略类型变更导致的反序列化异常，保留默认值
            }
        }

        return instance;
    }

    public async Task SaveSettingAsync<T>(T settings, Guid? tenantId = null, CancellationToken cancellationToken = default)
        where T : class, ISettings, new()
    {
        await using var db = await PlatformDbAccessor.CreateReadyDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (db is null)
        {
            await fallback.SaveSettingAsync(settings, tenantId, cancellationToken).ConfigureAwait(false);
            return;
        }

        var prefix = typeof(T).Name + ".";
        var scopeTenant = tenantId ?? Guid.Empty;
        var existing = await db.Settings
            .Where(s => s.TenantId == scopeTenant && s.Key.StartsWith(prefix))
            .ToDictionaryAsync(s => s.Key, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        foreach (var prop in typeof(T).GetProperties())
        {
            if (!prop.CanRead) continue;
            var key = prefix + prop.Name;
            var raw = JsonSerializer.Serialize(prop.GetValue(settings), prop.PropertyType, JsonOptions);
            if (existing.TryGetValue(key, out var rec))
                rec.Value = raw;
            else
                db.Settings.Add(new PlatformSetting { TenantId = scopeTenant, Key = key, Value = raw });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteSettingAsync<T>(Guid? tenantId = null, CancellationToken cancellationToken = default)
        where T : class, ISettings, new()
    {
        await using var db = await PlatformDbAccessor.CreateReadyDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (db is null)
        {
            await fallback.DeleteSettingAsync<T>(tenantId, cancellationToken).ConfigureAwait(false);
            return;
        }

        var prefix = typeof(T).Name + ".";
        var scopeTenant = tenantId ?? Guid.Empty;
        var rows = await db.Settings
            .Where(s => s.TenantId == scopeTenant && s.Key.StartsWith(prefix))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count > 0)
        {
            db.Settings.RemoveRange(rows);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllSettingsAsync(
        string? prefix = null,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await PlatformDbAccessor.CreateReadyDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (db is null)
            return await fallback.GetAllSettingsAsync(prefix, tenantId, cancellationToken).ConfigureAwait(false);

        var scopeTenant = tenantId ?? Guid.Empty;
        var query = db.Settings.AsNoTracking().Where(s => s.TenantId == Guid.Empty || s.TenantId == scopeTenant);
        if (!string.IsNullOrWhiteSpace(prefix))
            query = query.Where(s => s.Key.StartsWith(prefix));

        var rows = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows.Where(r => r.TenantId == Guid.Empty))
            result[r.Key] = r.Value;
        if (scopeTenant != Guid.Empty)
        {
            foreach (var r in rows.Where(r => r.TenantId == scopeTenant))
                result[r.Key] = r.Value;
        }
        return result;
    }
}

