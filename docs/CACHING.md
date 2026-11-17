# 缓存设计（生产级底座）

## 设计原则

- 统一 `ICacheService` API，调用方不感知 L1 / L2；
- L1 = 进程内 `IMemoryCache`，L2 = Redis（可选，默认启用）；
- per-key 锁防进程内缓存击穿，锁对象有界回收；
- tag 失效在 Redis 侧用 Lua 原子执行，并低频清理过期残留索引；
- 绝对过期 + 滑动过期；
- 租户/用户 key 必须经 `ICacheKeyBuilder` 构造；
- L2 value 以 JSON envelope 保存，天然限定为 DTO / 共享契约 / 基础类型。

## 当前实现边界

| 能力 | 状态 | 说明 |
|---|---|---|
| 内存缓存 L1 | ✅ 已实现 | `MemoryCacheService`，宿主单例 |
| Redis 缓存 L2 | ✅ 已实现（默认启用） | `RedisCacheStore`，`StackExchange.Redis`，JSON envelope |
| 读—未命中—生成—回填 | ✅ 已实现 | `HybridCacheService.GetOrCreateAsync`，per-key 锁 + 二次检查 |
| L2 命中回填 L1 | ✅ 已实现 | 返回时按剩余 TTL / tag 写入 L1 |
| tag 批量失效 | ✅ 已实现 | L1 精确索引；L2 Lua 原子删除 |
| 单 key 删除 | ✅ 已实现 | `RemoveAsync` 同时清 L1 和 L2，Lua 同步摘 tag 索引 |
| 绝对 / 滑动过期 | ✅ 已实现 | `CacheEntryOptions`，Redis TTL + L1 eviction 回调 |
| 跨节点 L1 失效 | ✅ 已实现 | Redis pub/sub `bb:cache:invalidate`，连接串变化时自动重订阅 |
| Redis 索引残留清理 | ✅ 已实现 | `RedisCacheTagIndexCleaner` 每 10 分钟扫描并清理 |
| 有界 key 锁池 | ✅ 已实现 | `KeyedLockPool` 引用计数归零即释放，消除动态 key 泄漏 |
| 租户 / 用户 key 隔离 | ✅ 已实现 | `ICacheKeyBuilder.Global/Tenant/User`，显式传 scope |
| null 结果缓存 | ✅ 已实现 | `CacheEntryOptions.CacheNullValues`，默认 false |
| Redis 健康探针 | ✅ 已实现 | `/health` 中的 `redis` 检查，PING 延迟 > 1s 为 Degraded |
| Redis 故障降级 | ✅ 已实现 | Redis 读写异常时 fail-open，L1 继续工作 |
| 跨进程 single-flight | ❌ 未实现 | 多节点同时冷 miss 仍可能各自回源一次 |
| L2 EF 查询缓存 | ❌ 未实现 | 依赖变更钩子与表达式分析，成本高于收益 |
| 请求级缓存 | ❌ 未实现 | 当前无单次请求内重复读的热点 |
| 命中率 / 耗时指标 | ❌ 未实现 | 暂无 OpenTelemetry meter/counter |

## 配置

Redis 默认连接串是 `localhost:6379`，已写在 `appsettings.json`，安装向导也会预填该值：

```json
{
  "Infrastructure": {
    "Redis": "localhost:6379"
  }
}
```

格式遵循 `StackExchange.Redis` 的 `ConfigurationOptions.Parse`。配置优先级：

1. `data/blazorboilerplate.settings.json` 中安装向导写出的值（显式留空 = 禁用 L2）；
2. `Infrastructure:Redis` 配置节 / 环境变量；
3. 默认值 `localhost:6379`。

空白、`not-configured` 或向导里留空都表示禁用 L2，此时行为等价于纯内存缓存。
安装向导完成后 `RedisCacheState.Configure` 会让运行中的宿主立即感知：连接对象释放、订阅重建，无需重启。

Redis key 约定：

| 用途 | 形式 |
|---|---|
| 缓存值 | `bb:cache:v:{logicalKey}` |
| tag 索引 | `bb:cache:t:{tag}` |
| 失效广播 | channel `bb:cache:invalidate` |

逻辑 key 由 `ICacheKeyBuilder` 生成，例如：

```text
g:settings
t:{tenantId}:menus:visible
t:{tenantId}:u:{userId}:profile
```

广播消息只带 `{ nodeId, type, value }`，并跳过发送方自身，避免刚写入的 L1 被本机订阅立刻清掉。

## 契约层（`BlazorBoilerplate.Application`）

契约放在 `Application` 程序集是刻意的：`ModuleLoadContext.SharedAssemblies`
包含 `BlazorBoilerplate.Application`，宿主与所有模块 ALC 共用同一份类型身份。
若把 `ICacheService` / `CacheEntryOptions` / `ICacheKeyBuilder` 放进模块或 Infrastructure 私有程序集，
模块 ALC 会加载出独立的类型副本，导致接口无法赋值、策略无法传递。

```csharp
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct);
    Task SetAsync<T>(string key, T value, TimeSpan? ttl, string? tag, CancellationToken ct);
    Task RemoveByTagAsync(string tag, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
    Task<T?> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory,
        CacheEntryOptions? options, CancellationToken ct);
}

public interface ICacheKeyBuilder
{
    string Global(string key);
    string Tenant(Guid? tenantId, string key);
    string User(Guid? tenantId, Guid? userId, string key);
}
```

## 使用示例

```csharp
public sealed class MenuService(ICacheService cache, ICacheKeyBuilder keys, ICurrentUserContext user)
{
    public Task<List<MenuDto>> GetVisibleAsync(CancellationToken ct) =>
        cache.GetOrCreateAsync(keys.Tenant(user.TenantId, "menus:visible"), async token =>
        {
            var menus = await LoadFromDbAsync(token);
            return menus.Select(ToDto).ToList();   // 返回 DTO，不是 EF 实体
        }, new CacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
            Tag = "menus"
        }, ct);

    public Task InvalidateAsync(CancellationToken ct) =>
        cache.RemoveByTagAsync("menus", ct);
}
```

`ICacheKeyBuilder` 不隐式读取当前用户：宿主单例不能注入 scoped `ICurrentUserContext`。
调用方从自己的请求上下文拿到 `tenantId` / `userId` 后显式传入。

## 必守约定（ALC 安全）

缓存由宿主单例持有，生命周期跨越所有模块。这是最容易被忽视的一条红线：

> **缓存 value 只能是共享契约类型（Application / ModuleAbstractions / Domain）或基础类型（string / DTO / 集合）。
> 绝不缓存模块 ALC 内部的类实例。**

原因与 topic bus 相同：宿主单例持有模块 ALC 对象 → 该对象成为 GC root →
`alc.Unload()` 后 ALC 无法回收 → 模块热插拔失效。L1 靠"只存 DTO"约定约束，
L2 则通过 JSON 序列化天然拒绝模块私有类型跨进程传递。

## 组合根接入

`HostPlatformExtensions.cs` 中在占位注册之后调用：

```csharp
services.AddBlazorBoilerplateInfrastructurePlaceholders();
services.AddBlazorBoilerplateCaching(configuration, contentRootPath);
```

注册的宿主单例包括：

- `ICacheKeyBuilder` / `KeyedLockPool` / `MemoryCacheService`
- `RedisConnectionProvider` / `IRedisCacheStore` / `HybridCacheService`
- `RedisCacheInvalidationSubscriber` / `RedisCacheTagIndexCleaner`
- `ICacheService` → `HybridCacheService`

旧的 `AddBlazorBoilerplateMemoryCache()` 保留为纯内存路径（测试/兼容用途）。
L1 的 `IMemoryCache` 未设 `SizeLimit`：启用后每个条目都必须配 `Size`，单实例有限 key 空间不需要。

## 测试

`tests/BlazorBoilerplate.ModuleRuntime.Tests/CacheServiceTests.cs`：

- L1 9 例：读写往返、缺失 key、单 key 删除、tag 失效、换 tag、并发单飞、TTL 过期等；
- Hybrid 5 例：L2 命中回填 L1、`GetOrCreate` 复用 L2、双层写入、双层删除、tag 只失效匹配 key；
- KeyBuilder 3 例：全局/租户/用户作用域；
- null 缓存 2 例：默认不缓存、开启后缓存；
- 锁池 2 例：引用计数回收、同 key 互斥；
- 真 Redis 集成 1 例：设置 `BB_TEST_REDIS=localhost:6379` 后执行，未设置时直接返回。

`dotnet test` 全量 37/37 通过（无真实 Redis 时集成例走空分支）。

## 后续演进（未实现）

1. **跨进程 single-flight**：用 Redis lock 或 `SET NX` 协调多节点冷 miss 回源。
2. **L2 查询缓存**：只有当读 QPS 成为瓶颈再评估，优先物化视图 / 只读副本。
3. **命中率 / 耗时指标**：接 OpenTelemetry counter/histogram，区分 L1/L2 命中与回源次数。
