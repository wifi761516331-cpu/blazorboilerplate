# ADR 0006：事件总线（进程内强类型 + 跨节点字符串频道 + 写后发布）

- 状态：已采纳（2026-11-17）
- 相关：ADR 0005（消息总线授权红线）、ADR 0001（前端+后端同体 RCL）

## 背景

项目已有两处"事件"雏形但未接通：

1. `DomainPrimitives.cs` 的 `Entity<T>.DomainEvents` / `Raise()` / `ClearDomainEvents()`：领域事件**收集器**已存在，但没有任何组件在持久化后把它们发出去。
2. `Application/Abstractions.cs` 的 `IntegrationEvent` / `IIntegrationEventBus` 与 `PlaceholderIntegrationEventBus`：只是内存队列占位，无人调用，且类型设计不满足跨 ALC 安全。

模块 A 希望开放主题、模块 B 订阅；同时多实例部署需要跨节点通知。需要一套自研、零外部依赖（遵守 ADR 0005）、且不破坏 collectible ALC 热卸载的事件基础设施。

## 决策

采用三层结构，不引入任何外部消息框架：

### 第一层：进程内强类型事件（主力）

- **契约放 Domain**：事件类、处理器接口放 `BlazorBoilerplate.Domain`，随宿主与模块 ALC 共用同一类型身份（该程序集已在 `ModuleLoadContext.SharedAssemblies`）。
- `IEventPublisher.PublishAsync<T>(T evt, ct) where T : IDomainEvent`。
- `IEventHandler<in T>` + `[Subscribe]` Attribute 标记订阅者；模块加载时扫描注册。
- 分发器**串行 await**所有订阅者；单个处理器异常**记日志、不重抛**，避免影响写入方与其余订阅者。
- **ALC 安全**：registry 只持有处理器的 `WeakReference` + 静态桥接委托（不捕获模块实例）；模块卸载时显式摘除该模块全部订阅。即使漏摘，弱引用也不会钉住 ALC。

### 第二层：字符串频道（默认进程内，Redis 可选增强）

- `IChannelBus.PublishAsync(channel, message)` / `SubscribeAsync(channel, handler)`，**面向字符串消息**（模块私有事件序列化成 JSON 后走这里）。
- **默认纯进程内**：`ChannelBus` 本地分发即可完成收发，**不依赖 Redis 或任何外部组件**，无 Redis 时功能完整可用。
- **Redis 是可选增强**：配置 Redis 时 `ChannelBus` 在**首次使用时惰性建立一次**跨节点 PUBLISH/SUBSCRIBE（复用 `RedisConnectionProvider`，频道前缀 `bb:evt:`）；未配置时该层静默跳过，不影响进程内行为。
- 消息带节点 Id，接收端按 nodeId 过滤自身回投（ignoreLoopback，避免本地已分发 + Redis 回投造成重复）。
- **仅传字符串**：不传递任何 CLR 对象，避免宿主持有模块 ALC 类型。
- 类名不绑定具体传输技术（不叫 RedisChannelBus）；只有一个实现 `ChannelBus`（单例，实现 `IAsyncDisposable` 在释放时退订）。**不继承 BackgroundService、无后台轮询**：Redis 配置在实例生命周期内固定，运行期变更需重启宿主。

### 第三层：EF Core 写后发布

- `ModuleDbContextBase.SaveChangesAsync` 覆写：先 `base` 提交，再扫描 `ChangeTracker` 中 `IDomainEventProducer.DomainEvents`，逐个 `IEventPublisher.PublishAsync`，最后 `ClearDomainEvents()`。
- **发布在提交之后**；发布失败记日志、不重抛 —— 数据已落库，不能让订阅侧故障回滚写侧或误报失败。
- 这是 CQRS 写侧的一半（写模型产生事件 → 他模块订阅），不引入独立读库、投影或 Outbox。

### 删除项

删除无人调用的 `IntegrationEvent` / `IIntegrationEventBus` / `PlaceholderIntegrationEventBus` 及其注册，避免两套并行 API 造成混淆。

## 备选方案（否决）

- **引入 MassTransit / RabbitMQ**：违反 ADR 0005（v9+ 商业授权），且增加部署组件。
- **照搬强类型总线到跨节点**：强类型依赖共享程序集类型身份，无法承载模块私有事件；字符串频道才能解耦。
- **SaveChanges 前发布 / 发布失败重抛**：前者事件可能在事务回滚后仍被发出，后者让写侧被读侧故障污染。

### V1 不引入 Stream + Outbox 的依据

对同类成熟进程内事件机制的调研表明，其可靠性上限与本方案 V1 一致，即 at-most-once：

- **无 Outbox**：不存在"业务数据与消息同事务持久化"的实体或处理器；典型做法是事务提交后才发布事件 —— 进程在两者之间崩溃即丢事件。
- **无 Stream / consumer group / ack**：未使用任何持久化流或外部消息中间件；事件分发是内存串行调用，fire-and-forget 任务进程退出即丢失。
- **跨节点频道是可选增强**：默认实现为空操作，需外部传输插件才生效；未安装时跨节点通道不承载任何发布。
- **重试/死信仅领域专用**：有限重试 + 重入队只存在于邮件发件队列等特定领域，不是通用事件总线的能力。

因此，在本层叠加 Stream + Outbox 会超出同类实现的能力边界，也违背 YAGNI。若业务提出"业务提交 → 事件必达"的硬要求，应另开 ADR 引入 Outbox 表 + Redis Stream consumer group，而不是在本总线上修补 —— 那是 Reliability 层的一次独立升级。

## 后果

- 获得：模块间解耦、跨节点通知、写后事件自动化；零外部依赖；热卸载不被钉住。
- 代价：
  - **跨节点是 at-most-once**，Redis pub/sub 离线丢消息；无重试、无 Outbox、无顺序保证。未配置 Redis 时仅有进程内投递，无跨节点能力。
  - 强类型事件的事件类必须放共享程序集（Domain）；模块私有事件序列化成字符串走频道。
  - 进程内**强类型**通知用 `IEventPublisher`（有编译期检查与类型安全）；`IChannelBus` 面向字符串/动态消息，两者按需选用。
  - 处理器必须可跨线程调用（分发器不保证同步上下文）。
- 后续若需要可靠投递，另开 ADR 讨论 Redis Stream + Outbox，不在此层叠加。
