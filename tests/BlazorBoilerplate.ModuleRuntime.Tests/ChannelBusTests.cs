using System.Text.Json;
using BlazorBoilerplate.Infrastructure.Caching;
using BlazorBoilerplate.Infrastructure.Events;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace BlazorBoilerplate.ModuleRuntime.Tests;

/// <summary>
/// 字符串频道总线测试。核心约定：不依赖 Redis 也能进程内收发（默认行为），
/// 配置 Redis 时叠加跨节点投递，并按 nodeId 过滤自身回投。
/// </summary>
public class ChannelBusTests
{
    private static ChannelBus NewBus() =>
        new(new RedisConnectionProvider(NullLogger<RedisConnectionProvider>.Instance),
            NullLogger<ChannelBus>.Instance);

    [Fact]
    public async Task Publish_WithoutRedis_ReachesLocalSubscribers()
    {
        var previous = RedisCacheState.ConnectionString;
        RedisCacheState.Configure(null);
        try
        {
            var bus = NewBus();
            string? gotChannel = null, gotMessage = null;
            await bus.SubscribeAsync("orders", (c, m) =>
            {
                gotChannel = c;
                gotMessage = m;
                return Task.CompletedTask;
            }, default);

            await bus.PublishAsync("orders", "{\"id\":1}", default);

            Assert.Equal("orders", gotChannel);
            Assert.Equal("{\"id\":1}", gotMessage);
        }
        finally
        {
            RedisCacheState.Configure(previous);
        }
    }

    [Fact]
    public async Task Publish_OtherChannel_IsNotDelivered()
    {
        var previous = RedisCacheState.ConnectionString;
        RedisCacheState.Configure(null);
        try
        {
            var bus = NewBus();
            var received = false;
            await bus.SubscribeAsync("a", (_, _) => { received = true; return Task.CompletedTask; }, default);

            await bus.PublishAsync("b", "x", default);

            Assert.False(received);
        }
        finally
        {
            RedisCacheState.Configure(previous);
        }
    }

    [Fact]
    public async Task DisposeSubscription_StopsDelivery()
    {
        var previous = RedisCacheState.ConnectionString;
        RedisCacheState.Configure(null);
        try
        {
            var bus = NewBus();
            var count = 0;
            var sub = await bus.SubscribeAsync("c", (_, _) => { count++; return Task.CompletedTask; }, default);

            await bus.PublishAsync("c", "1", default);
            await sub.DisposeAsync();
            await bus.PublishAsync("c", "2", default);

            Assert.Equal(1, count);
        }
        finally
        {
            RedisCacheState.Configure(previous);
        }
    }

    [Fact]
    public async Task Publish_OneHandlerThrows_OthersStillReceive()
    {
        var previous = RedisCacheState.ConnectionString;
        RedisCacheState.Configure(null);
        try
        {
            var bus = NewBus();
            var ok = false;
            await bus.SubscribeAsync("d", (_, _) => throw new InvalidOperationException("boom"), default);
            await bus.SubscribeAsync("d", (_, _) => { ok = true; return Task.CompletedTask; }, default);

            await bus.PublishAsync("d", "x", default);

            Assert.True(ok);
        }
        finally
        {
            RedisCacheState.Configure(previous);
        }
    }

    /// <summary>
    /// 模拟在线动态加载模块的场景：模块 A、B 共用同一个 ChannelBus 单例（宿主 DI 注入），
    /// B 在 A 发布第一条消息之后才加载并订阅 "a" 主题。
    /// 验证：(1) B 收不到订阅前的历史消息；(2) B 能收到订阅后 A 发布的消息；
    /// (3) B 卸载退订后不再收到 —— 对应模块必须在 StopAsync/卸载时 Dispose subscription 的约定。
    /// </summary>
    [Fact]
    public async Task LateModule_SubscribesSharedBus_OnlyGetsMessagesAfterSubscription()
    {
        var previous = RedisCacheState.ConnectionString;
        RedisCacheState.Configure(null);
        try
        {
            // 宿主单例：两个模块拿到的是同一个实例，共享 _handlers 订阅表。
            var bus = NewBus();

            // 模块 A 在 B 加载前先发布一条 —— pub/sub 不回放，B 不应收到。
            await bus.PublishAsync("a", "before-b-existed", default);

            // 模块 B 动态加载，手动订阅 "a" 主题，持有句柄供卸载时退订。
            var bMessages = new List<string>();
            var bSubscription = await bus.SubscribeAsync("a", (_, msg) =>
            {
                bMessages.Add(msg);
                return Task.CompletedTask;
            }, default);

            // B 订阅后 A 再发布 —— B 应收到。
            await bus.PublishAsync("a", "after-b-subscribed", default);
            Assert.Equal(["after-b-subscribed"], bMessages);

            // B 卸载，退订句柄 —— 之后 A 发布的消息 B 不再收到，也不再持有 handler 闭包。
            await bSubscription.DisposeAsync();
            await bus.PublishAsync("a", "after-b-unloaded", default);
            Assert.Equal(["after-b-subscribed"], bMessages);
        }
        finally
        {
            RedisCacheState.Configure(previous);
        }
    }

    /// <summary>
    /// 真 Redis 跨节点测试：设置 BB_TEST_REDIS=localhost:6379 后才会实际执行。
    /// 用一个不同 nodeId 的 envelope 模拟"其他节点发布"，验证本节点订阅者能收到；
    /// 同时验证 ignoreLoopback —— 自身 nodeId 发布的消息被忽略（本地已分发，避免重复）。
    /// </summary>
    [Fact]
    public async Task LiveRedis_OtherNodeMessage_Delivered_SelfNodeIgnored()
    {
        var connectionString = Environment.GetEnvironmentVariable("BB_TEST_REDIS");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var previous = RedisCacheState.ConnectionString;
        RedisCacheState.Configure(connectionString);
        try
        {
            var connection = new RedisConnectionProvider(NullLogger<RedisConnectionProvider>.Instance);
            await using var bus = new ChannelBus(connection, NullLogger<ChannelBus>.Instance);
            var channel = "it:" + Guid.NewGuid().ToString("N");

            string? received = null;
            // SubscribeAsync 内部惰性建立 Redis 订阅并 await 完成，返回后即可收发。
            await bus.SubscribeAsync(channel, (_, msg) => { received = msg; return Task.CompletedTask; }, default);

            var subscriber = await connection.GetSubscriberAsync(default);
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var otherNodeEnvelope = JsonSerializer.Serialize(
                new { nodeId = Guid.NewGuid(), message = "from-other-node" }, options);
            await subscriber.PublishAsync(RedisChannel.Literal("bb:evt:" + channel), otherNodeEnvelope);

            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (received is null && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            Assert.Equal("from-other-node", received);

            // ignoreLoopback：自身 nodeId 发布的消息应被忽略。
            received = null;
            var selfEnvelope = JsonSerializer.Serialize(
                new { nodeId = RedisCacheState.NodeId, message = "from-self" }, options);
            await subscriber.PublishAsync(RedisChannel.Literal("bb:evt:" + channel), selfEnvelope);
            await Task.Delay(300);
            Assert.Null(received);
        }
        finally
        {
            RedisCacheState.Configure(previous);
        }
    }
}
