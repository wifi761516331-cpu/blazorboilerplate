using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BlazorBoilerplate.Infrastructure.S3;

/// <summary>
/// 极简 AWS Signature Version 4 签名器，仅依赖 BCL（HMACSHA256 / SHA256），无 AWS SDK 依赖。
/// 足以覆盖 S3 对象操作（PutObject / GetObject / DeleteObject / HeadBucket / PutBucket）。
/// MinIO 与 AWS S3 共用同一套 SigV4 协议。
/// </summary>
public static class AwsSigV4Signer
{
    private const string Algorithm = "AWS4-HMAC-SHA256";
    private const string Service = "s3";

    /// <summary>
    /// 为已有的 HttpRequestMessage 追加 Authorization 等 SigV4 头。
    /// <paramref name="payload"/> 即请求体的原始字节（无 body 时传空数组），必须与其 Content 一致。
    /// </summary>
    public static void Sign(
        HttpRequestMessage request,
        byte[] payload,
        string region,
        string accessKey,
        string secretKey)
    {
        var now = DateTime.UtcNow;
        var amzDate = now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var payloadHash = Sha256Hex(payload);

        // 必须签名的头：host 取自最终发送的 Authority（HttpClient 会按 URI 自动带 host）。
        var headerValues = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(request.Headers.Host) && request.RequestUri is not null)
            headerValues["host"] = request.RequestUri.Host +
                (request.RequestUri.Port is 80 or 443 ? "" : ":" + request.RequestUri.Port);
        else
            headerValues["host"] = request.Headers.Host!;

        headerValues["x-amz-content-sha256"] = payloadHash;
        headerValues["x-amz-date"] = amzDate;

        if (request.Content is not null)
        {
            foreach (var h in request.Content.Headers)
                headerValues[h.Key.ToLowerInvariant()] = string.Join(",", h.Value).Trim();
        }

        var canonicalHeadersBuilder = new StringBuilder(headerValues.Sum(kv => kv.Key.Length + kv.Value.Length + 2) + headerValues.Count);
        foreach (var kv in headerValues)
            canonicalHeadersBuilder.Append(kv.Key).Append(':').Append(kv.Value).Append('\n');
        var canonicalHeaders = canonicalHeadersBuilder.ToString();

        var signedHeaders = string.Join(";", headerValues.Keys);

        var canonicalUri = EncodePath(request.RequestUri!.AbsolutePath);
        var canonicalQuery = EncodeQuery(request.RequestUri.Query);
        var canonicalRequest = string.Concat(
            request.Method.Method.ToUpperInvariant(), "\n",
            canonicalUri, "\n",
            canonicalQuery, "\n",
            canonicalHeaders, "\n",
            signedHeaders, "\n",
            payloadHash);

        var scope = $"{dateStamp}/{region}/{Service}/aws4_request";
        var stringToSign = string.Concat(
            Algorithm, "\n",
            amzDate, "\n",
            scope, "\n",
            Sha256Hex(Encoding.UTF8.GetBytes(canonicalRequest)));

        var signingKey = GetSigningKey(secretKey, dateStamp, region, Service);
        var signature = HmacHex(signingKey, stringToSign);

        var authorization =
            $"{Algorithm} Credential={accessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}";

        request.Headers.TryAddWithoutValidation("X-Amz-Date", amzDate);
        request.Headers.TryAddWithoutValidation("X-Amz-Content-Sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
    }

    private static byte[] GetSigningKey(string secret, string dateStamp, string region, string service)
    {
        var kDate = Hmac(Encoding.UTF8.GetBytes("AWS4" + secret), dateStamp);
        var kRegion = Hmac(kDate, region);
        var kService = Hmac(kRegion, service);
        return Hmac(kService, "aws4_request");
    }

    private static byte[] Hmac(byte[] key, string data) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));

    private static byte[] Hmac(byte[] key, byte[] data) =>
        HMACSHA256.HashData(key, data);

    private static string HmacHex(byte[] key, string data) =>
        ToHex(Hmac(key, data));

    private static string Sha256Hex(byte[] data) =>
        ToHex(SHA256.HashData(data));

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>逐段编码路径，保留 '/' 分隔符；空格编码为 %20。</summary>
    private static string EncodePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "/";
        var segments = path.Split('/');
        var sb = new StringBuilder(path.Length + 8);
        for (var i = 0; i < segments.Length; i++)
        {
            if (i > 0) sb.Append('/');
            sb.Append(Uri.EscapeDataString(segments[i]));
        }
        return sb.ToString();
    }

    /// <summary>编码查询串（key=value，按 key 排序，'=` 与 '&' 原样保留）。</summary>
    private static string EncodeQuery(string query)
    {
        if (string.IsNullOrEmpty(query))
            return string.Empty;
        var raw = query.TrimStart('?');
        if (raw.Length == 0)
            return string.Empty;

        var pairs = raw.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p =>
            {
                var idx = p.IndexOf('=');
                return idx < 0
                    ? (Uri.EscapeDataString(p), string.Empty)
                    : (Uri.EscapeDataString(p[..idx]), Uri.EscapeDataString(p[(idx + 1)..]));
            })
            .OrderBy(p => p.Item1, StringComparer.Ordinal)
            .ThenBy(p => p.Item2, StringComparer.Ordinal);

        return string.Join("&", pairs.Select(p => p.Item2.Length == 0 ? p.Item1 : $"{p.Item1}={p.Item2}"));
    }
}
