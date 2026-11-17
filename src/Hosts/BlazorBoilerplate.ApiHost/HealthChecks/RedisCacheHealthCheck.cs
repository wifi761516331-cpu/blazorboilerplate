using BlazorBoilerplate.Infrastructure.Caching;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace BlazorBoilerplate.ApiHost.HealthChecks;

/// <summary>
/// Redis L2 健康检查。未启用时视为正常降级；连接或 PING 失败时按注册失败状态处理。
/// </summary>
public sealed class RedisCacheHealthCheck(RedisConnectionProvider connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!RedisCacheState.IsEnabled)
            return HealthCheckResult.Healthy("Redis L2 未启用，当前使用内存缓存。");

        try
        {
            var database = await connection.GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
            var latency = await database.PingAsync(CommandFlags.None).ConfigureAwait(false);

            return latency > TimeSpan.FromSeconds(1)
                ? HealthCheckResult.Degraded($"Redis 响应较慢：{latency.TotalMilliseconds:0} ms")
                : HealthCheckResult.Healthy($"Redis 正常：{latency.TotalMilliseconds:0} ms");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Redis 健康检查失败", ex);
        }
    }
}
