using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BlazorBoilerplate.ApiHost.Auth;

/// <summary>
/// 自签 JWT（HS256，零外部包）。Bluebird 的 access_token / refresh_token 语义：
/// access_token 有效期 2 分钟，refresh_token 7 天；两类 token 的 typ 不同，不可混用。
/// 签名密钥持久化在 data/auth.key（首次启动随机生成），重启后已签发 token 仍可校验。
/// </summary>
public sealed class JwtTokenService
{
    public const string AccessType = "access";
    public const string RefreshType = "refresh";

    private readonly byte[] _key;
    private readonly int _accessSeconds;
    private readonly int _refreshDays;

    public JwtTokenService(IHostEnvironment environment, IConfiguration configuration)
    {
        _accessSeconds = configuration.GetValue("Auth:AccessTokenLifetimeSeconds", 120);
        _refreshDays = configuration.GetValue("Auth:RefreshTokenLifetimeDays", 7);

        var configured = configuration["Auth:SigningKey"];
        if (!string.IsNullOrWhiteSpace(configured) && configured.Length >= 32)
        {
            _key = Encoding.UTF8.GetBytes(configured);
            return;
        }

        var dir = Path.Combine(environment.ContentRootPath, "data");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "auth.key");
        if (File.Exists(path))
        {
            _key = Convert.FromBase64String(File.ReadAllText(path).Trim());
        }
        else
        {
            _key = RandomNumberGenerator.GetBytes(32);
            File.WriteAllText(path, Convert.ToBase64String(_key));
        }
    }

    public int AccessTokenLifetimeSeconds => _accessSeconds;
    public int RefreshTokenLifetimeDays => _refreshDays;

    /// <summary>签发 access token（claims 携带 roles/perms，供 ApiHost 授权与 WebHost UI 直接使用）。</summary>
    public string IssueAccessToken(
        Guid userId, string userName, string email, Guid sessionId, Guid? tenantId,
        IReadOnlyList<string> roles, IReadOnlyList<string> permissions)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object>
        {
            ["sub"] = userId.ToString(),
            ["name"] = userName,
            ["email"] = email,
            ["sid"] = sessionId.ToString(),
            ["tid"] = tenantId?.ToString() ?? string.Empty,
            ["roles"] = roles,
            ["perms"] = permissions,
            ["typ"] = AccessType,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddSeconds(_accessSeconds).ToUnixTimeSeconds()
        };
        return Encode(payload);
    }

    public string IssueRefreshToken(Guid userId, Guid sessionId, Guid? tenantId)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object>
        {
            ["sub"] = userId.ToString(),
            ["sid"] = sessionId.ToString(),
            ["tid"] = tenantId?.ToString() ?? string.Empty,
            ["typ"] = RefreshType,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddDays(_refreshDays).ToUnixTimeSeconds()
        };
        return Encode(payload);
    }

    /// <summary>校验签名/有效期/类型；失败返回 null。返回解析后的 payload 供调用方读取 claims。</summary>
    public JsonDocument? Validate(string token, string expectedType)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return null;
            var expected = Sign($"{parts[0]}.{parts[1]}");
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[2])))
                return null;

            var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var root = payload.RootElement;
            if (!root.TryGetProperty("typ", out var typ) || typ.GetString() != expectedType) return null;
            if (!root.TryGetProperty("exp", out var exp) || exp.GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
            return payload;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>不校验签名读取 exp（用于客户端侧预刷新判断）。</summary>
    public static DateTimeOffset? ReadExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return null;
            using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            return payload.RootElement.TryGetProperty("exp", out var exp)
                ? DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64())
                : null;
        }
        catch { return null; }
    }

    public static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static string[] ReadArray(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray()
            : [];

    private string Encode(Dictionary<string, object> payload)
    {
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var body = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        return $"{header}.{body}.{Sign($"{header}.{body}")}";
    }

    private string Sign(string data)
    {
        using var hmac = new HMACSHA256(_key);
        return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
