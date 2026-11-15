using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace BlazorBoilerplate.ApiHost.Services;

/// <summary>
/// 邮箱验证码内存存储（用于注册与找回密码）：
/// 仅保存验证码 SHA-256 摘要（不存明文），支持 60 秒发送冷却、10 分钟过期、最多 5 次错误尝试后作废。
/// </summary>
public sealed class EmailVerificationCodeStore
{
    public const string PurposeRegister = "register";
    public const string PurposeResetPassword = "reset-password";

    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SendCooldown = TimeSpan.FromSeconds(60);
    private const int MaxVerifyAttempts = 5;

    private sealed class Entry
    {
        public required byte[] CodeHash { get; init; }
        public required DateTimeOffset CreatedAtUtc { get; init; }
        public required DateTimeOffset ExpiresAtUtc { get; init; }
        public int FailedAttempts { get; set; }
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>生成 6 位数字验证码（返回明文用于发信，内存仅存哈希）；若在 60 秒冷却期内则抛出提示异常。</summary>
    public string Issue(string purpose, string email)
    {
        var key = BuildKey(purpose, email);
        var now = DateTimeOffset.UtcNow;

        if (_entries.TryGetValue(key, out var existing))
        {
            var elapsed = now - existing.CreatedAtUtc;
            if (elapsed < SendCooldown)
            {
                var remaining = (int)Math.Ceiling((SendCooldown - elapsed).TotalSeconds);
                throw new InvalidOperationException($"发送过于频繁，请在 {remaining} 秒后再试。");
            }
        }

        var code = RandomNumberGenerator.GetInt32(100000, 1_000_000).ToString();
        _entries[key] = new Entry
        {
            CodeHash = HashCode(key, code),
            CreatedAtUtc = now,
            ExpiresAtUtc = now + CodeLifetime
        };
        return code;
    }

    /// <summary>校验并一次性消费验证码（常量时间比对哈希）；失败时抛出明确中文错误。</summary>
    public void VerifyAndConsume(string purpose, string email, string? code)
    {
        var trimmed = (code ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("请输入邮箱验证码。");

        var key = BuildKey(purpose, email);
        if (!_entries.TryGetValue(key, out var entry))
            throw new InvalidOperationException("验证码不存在或已过期，请重新发送。");

        if (DateTimeOffset.UtcNow > entry.ExpiresAtUtc)
        {
            _entries.TryRemove(key, out _);
            throw new InvalidOperationException("验证码已过期，请重新发送。");
        }

        var candidateHash = HashCode(key, trimmed);
        if (!CryptographicOperations.FixedTimeEquals(entry.CodeHash, candidateHash))
        {
            entry.FailedAttempts++;
            if (entry.FailedAttempts >= MaxVerifyAttempts)
            {
                _entries.TryRemove(key, out _);
                throw new InvalidOperationException("验证码错误次数过多，请重新发送。");
            }
            throw new InvalidOperationException("邮箱验证码不正确。");
        }

        _entries.TryRemove(key, out _);
    }

    private static byte[] HashCode(string key, string code) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{key}:{code}"));

    private static string BuildKey(string purpose, string email) =>
        $"{purpose.Trim().ToLowerInvariant()}:{email.Trim().ToLowerInvariant()}";
}
