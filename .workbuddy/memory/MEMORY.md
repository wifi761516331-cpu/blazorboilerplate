# 项目长期记忆（E:/ELing/Blazor）

## 前端基准仓库（用户明确约定）
- 前端基于 BitPlatform **官方上游仓库**：https://github.com/bitfoundation/bitplatform（Bit.BlazorUI + Blazor Web App + MAUI Blazor Hybrid）。
- **不是** fork `https://github.com/l18211338136-hub/bitplatform`。任何 fork 的改动、分支或行为都不得作为依据。
- 引用组件 API 前，先确认实际采用的官方仓库分支/标签/commit、目标框架与 Bit.BlazorUI 版本，以官方源码、模板/示例和官方文档为准，不凭记忆编造参数。
- **官方说明文档入口**：https://blazorui.bitplatform.dev/overview（bit BlazorUI 文档站）；组件 API 目录 /components、安装 /getting-started、主题 /theming、图标 /iconography、裁剪 /trimming、排障 wiki https://wiki.bitplatform.dev。文档页标注版本（查证时为 v11.0.0-pre-19、MIT、多目标 .NET 10/11），须与实际 Bit.BlazorUI 包版本对齐。
- **DataGrid 不在核心包**：它属于可选的 `Bit.BlazorUI.Extras`（图表/地图/PDF viewer/编辑器与其他设计系统预设同包）。核心 `Bit.BlazorUI` 只含基础组件 + 主题运行时 + Fluent。做 OData Grid 适配前须确认 Extras 已引入。

## 开发规范来源
- 用户级技能 `dotnet10-bitplatform-fullstack-zh`（~/.workbuddy/skills/）：.NET 10 + DDD/Clean Architecture + Modular Monolith + Dynamic Module Runtime。关键红线：禁 RabbitMQ；动态 Endpoint / ALC 卸载 / OData 动态 EDM / DI 卸载须先做可运行 POC 验证；未经用户批准不得默认"重启主 Host"。

## 模块加载顺序（nopCommerce 风格，2026-11-13 实现）
- 各模块统一为「前端+后端同体 RCL」，manifest 声明 `displayOrder`（int，升序先加载）+ `dependencies`（依赖模块 Id 数组）。
- 加载顺序 = 拓扑排序（依赖优先）+ DisplayOrder 升序 + Id 兜底，确定性；缺失依赖/循环依赖由 `ModuleCatalog.ValidateLoadGraph()` 拦截。
- 运行时 API：`GET /api/modules/load-order`、`GET /api/modules/load-graph`、`POST /api/modules/load-all`（`LoadAllAsync` 按序批量加载，依赖失败方标记 Failed 跳过）。
- 启动自动加载：`ModuleRuntimeOptions.AutoLoadOnStartup`（默认 true），仅在 `SetupMode.Normal`（已安装）时执行，与 nopCommerce 一致（已安装模块每次启动自动激活）。
- 单测：`tests/Lumen.ModuleRuntime.Tests/LoadOrderTests.cs`（7 例全过）。

## 首次运行安装引导（2026-11-14 完成，端到端已验证）
- 流程：未安装（无 `data/setup.json`）→ ApiHost **Bootstrap 模式**（除 /api/setup、/health、/openapi 外全部 423）→ 前端 `/` `/modules` 302 → `/setup` 配置页 → `POST /api/setup/complete`（持久化配置+写标记+按序自动加载模块）→ Normal。重复 complete 返回 409。
- **`SetupOptions.DevBypass` 默认必须为 false**（appsettings.json 不得全局写 true，否则生产也绕过引导）。
- 两个根因级 Blazor gotcha：① `NavigateTo()` 抛出的 `NavigationException` 是预渲染重定向信号，绝不能被 try/catch 吞掉（守卫/跳转逻辑导航须在 catch 之外）；② RCL 页面路由需 `MapRazorComponents().AddAdditionalAssemblies(...)` + `Router.AdditionalAssemblies` 双处声明（清单在 `WebHost/Components/ComponentAssemblies.cs`），否则 404。
- 必要模块：manifest `isEssential` → descriptor 透出 → `UnloadAsync` 服务端拒绝 → UI 徽标不渲染卸载按钮；catalog 未标 essential（可卸载演示）。
- 运维：重置首装 = 删 `src/Hosts/Lumen.ApiHost/data/` 下 setup.json/lumen.settings.json/lumen.db*；Git Bash `taskkill` 用 `//PID`；构建被占用时先杀遗留 Lumen.ApiHost/WebHost 进程。

## 批次 3：EF Core Migrations + 用户/角色/菜单（2026-11-14 完成）
- Identity 必要模块（RCL，displayOrder=10，不可卸载）：5 表 Users/Roles/Menus/UserRoles/RoleMenus，Guid v7 主键；端点 `/api/identity/users|roles|menus`（含 /visible、/tree、分配与密码/启停）；页面 `/identity/users|roles|menus`（照 bitplatform Boilerplate Management/UsersPage 风格）。
- **无外键约定（参考 E:/ELing/Nova）**：模型层不配置导航关系 + `CustomNpgsqlMigrationsSqlGenerator`（清 ForeignKeys、跳 AddForeignKeyOperation），运行时与设计时两处 `ReplaceService`；完整性全在服务层代码保证。PG 迁移脚本 0 FOREIGN KEY，SQLite 5 表 FK 全空。
- `SetupInitializer`：PG→`MigrateAsync` / SQLite→`EnsureCreated`；种子管理员+2 系统角色+核心菜单+模块 manifest 菜单+角色授权；幂等。`SetupConfiguration` 含 databaseProvider/adminPassword/sqlite；`PlatformConnectionState` 静态状态由安装向导填充、ApiHost 启动时从 `data/lumen.settings.json` 恢复（契约程序集在模块 ALC 中共享）。
- 模块菜单声明用 `manifest.frontend.menuItems`（结构化 text/route/icon/sort），安装时写入 DB 菜单表；`MainLayout` 侧边栏优先取 `/api/identity/menus/visible`，失败回退 `ModuleFrontendRegistry`。
- **动态卸载 = 停用并回收 ALC，状态回到 `Installed` 可再次 load**；`Uninstalled` 只表示包从目录移除。状态机 `Unloading → Installed` 已放行。
- EF/SQLite gotcha：SQLite 不支持 `DateTimeOffset` 出现在 ORDER BY → 用时间有序 Guid v7 主键排序。菜单循环引用守卫必须沿 `ParentId` **向上**追溯祖先链（向下遍历子树是错的方向）。
- Bit.BlazorUI 10.7.0：`BitNav`/`BitIconName` 分别在 Extras/Icons 包（核心包没有）；`BitNavItem.IsDisabled`（非 IsEnabled）；`BitNav.OnSelectItem` 是 `EventCallback<TItem>`，Razor 里要用显式 lambda；`BitCallout` 无 `Color` 参数（未知参数会被静默当 HTML 属性）。
- `ModuleLoadContext.SharedAssemblies` 只放平台契约与 Host 已静态引用且必须类型同一的程序集（Npgsql / Npgsql.EF / Bit.* 等），避免模块 ALC 加载第二份造成类型身份冲突。
- **模块私有依赖绝不进宿主 csproj（用户明确红线）**：每加一个模块就要改宿主 = 架构失败。ALC 解析顺序固定为 ① 显式共享白名单→默认 ALC；② 模块包自带的任意 DLL（含 `Microsoft.Extensions.AI`、`OpenAI`、`System.ClientModel` 等 `Microsoft.*`/`System.*`）→模块 ALC；③ 包里没有的框架程序集才回落默认 ALC。模块项目设 `CopyLocalLockFileAssemblies=true`，`scripts/build-module.sh` 复制整个 bin 的 DLL 到包 `backend/`。实测 74/74、ALC 内完整加载 MEAI 链成功。
- **用户提供的免宿主引用参考实现**（2026-11-26）：`E:/文档/软著/cogo-platform/src/backend/api/Boilerplate`。关键文件：`framework/Infrastructure/Extensions/ApplicationPartManagerExtensions.cs:146-172` 扫描 `Plugins/*` 并 `Assembly.LoadFrom` 注册 ApplicationPart；`framework/Core/Infrastructure/WebAppTypeFinder.cs:187-214` 预扫描插件目录 DLL；`framework/Core/Infrastructure/BoilerplateEngine.cs:54-66,103` 挂 `AppDomain.CurrentDomain.AssemblyResolve` 按程序集全名从扫描结果返回依赖。本质是「默认 ALC + 全局 AssemblyResolve 兜底」，能免宿主引用但不可卸载；本项目保留 collectible ALC + `AssemblyDependencyResolver` + 包内同目录探测，隔离更强，借鉴点只到「按名字兜底解析」，不照搬默认 ALC。
- 文档/部署：`README.md`、`docs/ROADMAP.md`、`docs/adr/0001~0005`、`docs/spikes/2026-11-14-ef-migrations-e2e.md`、`deploy/docker-compose.yml`+Dockerfile（Docker 本机不可用，未实测）。测试 13/13。
- 遗留：真实 PG 实例 MigrateAsync、基础设施连通性测试、动态模块**前端**路由自动发现、模块包签名/远程仓库、docker-compose 实机验证。
- **登录鉴权与菜单权限过滤已实现（2026-11-17 核实，勿再说"没做"）**：自签 JWT HS256（`ApiHost/Auth/JwtTokenService.cs`，密钥持久化 `data/auth.key`，access 2 分钟 / refresh 7 天）+ 自研 `AppBearerHandler`；claims 内嵌 roles/perms/tid/sid；`UseEndpointPermissionGuard` 统一校验动态模块端点与 Minimal API 权限码，MVC 用 `[Authorize]` + `[RequirePermission]`；WebHost 侧 Cookie `bb_auth`（HttpOnly/Lax）+ `CookieAuthSession` 无感刷新 + `PermissionGate.razor` 按钮级权限；`CurrentUserFactory` 实时聚合角色/权限码/菜单路径，首个用户隐式全权。提交见 `2a85aef`、`e77f580`。剩余真缺口：第三方登录按钮是占位、`SignIn` 未挂 `auth-sensitive` 限流（注册/重置有）、refresh_token 故意不轮换。

## 事件总线（2026-11-17 实现，ADR 0006）
- 三层分层，与 Smartstore 一致：**进程内强类型管本地、字符串只管跨节点、EF 写后发布**。契约在 `Domain/Events/EventBusContracts.cs`（宿主与模块 ALC 共用类型身份）。
- 第一层 `EventBus`（Infrastructure/Events）：`IEventPublisher`/`IEventHandler<T>`/`[Subscribe]`；进程内事件**一律走它**。ALC 安全核心：registry 只存 handler 的 `WeakReference<object>` + 静态桥接委托，卸载时 `UnsubscribeModule(id)`，弱引用兜底不钉住 ALC。串行分发，单 handler 异常记日志不重抛。跨模块强类型事件的事件类必须放 Domain。
- 第二层 `ChannelBus`（唯一实现，类名中性不绑定技术，单例 + IAsyncDisposable，同时作 IChannelBus）：**默认纯进程内收发，不依赖 Redis**；配置 Redis 时首次使用惰性建立一次跨节点订阅（复用 `RedisConnectionProvider`，pattern 订阅 `bb:evt:*`，envelope `{nodeId,message}` + nodeId 过滤自身回投避免重复）。Publish 先本地分发再 PUBLISH，Redis 失败只记日志。**不继承 BackgroundService、无后台轮询**（用户明确要求去掉），Redis 配置实例生命周期内固定，运行期变更需重启；`Lazy<Task<ISubscriber?>>` + ExecutionAndPublication 保证并发首次只订阅一次，释放时退订。**无 InProcessChannelBus / RedisChannelBus**。模块私有事件序列化成 JSON 字符串走此层。
- 第三层 EF 写后发布：`Persistence/DomainEventPublishingInterceptor` 在 `SavedChangesAsync` 提交后收集 `IDomainEventProducer.DomainEvents`，反射调泛型 Publish，先 Clear 再发，异常记日志不重抛；在 `UsePlatformDbProvider` 注册。Entity<T> 已实现 IDomainEventProducer。
- 生命周期：`ModuleLifecycleManager` 加载时把宿主 `IEventPublisher`/`IChannelBus` 注入模块 DI，扫描 `[Subscribe]` handler（`ActivatorUtilities.CreateInstance`）注册；卸载时摘订阅+清强引用。已删无用的 `IntegrationEvent`/`PlaceholderIntegrationEventBus`。
- 测试 `EventBusTests` + `ChannelBusTests`，全量 45/45。真 Redis 跨节点测试用 `BB_TEST_REDIS` 开关（本机无 redis-server 时跳过）。可靠性 at-most-once，无 Outbox/Stream/重试/顺序保证（ADR 0006 已写明依据），需可靠投递另开 ADR。未提交。
- 契约在 `Application/Abstractions.cs`：`ICacheService` + `CacheEntryOptions`（绝对/滑动过期 + Tag + `CacheNullValues`）+ `ICacheKeyBuilder`（`Global/Tenant/User` 隔离租户/用户 key），宿主与模块 ALC 共用类型身份。
- 生产注册入口是 `AddBlazorBoilerplateCaching(configuration, contentRootPath)`；`HybridCacheService` = L1 `IMemoryCache` + L2 Redis。**默认 Redis = `localhost:6379`**（`RedisCacheState.DefaultConnectionString` + appsettings + Setup 页预填）。Redis 未配置（显式留空或 `not-configured`）时 L2 no-op，等价于纯内存缓存；旧 `AddBlazorBoilerplateMemoryCache()` 仅兼容/测试用。
- 连接串优先级：安装向导写出的 `data/blazorboilerplate.settings.json` > 配置节/环境变量 > 默认值。向导里显式留空会禁用 L2，不会被 appsettings 默认值覆盖。`RedisCacheState.ConnectionChanged` 通知宿主：`RedisConnectionProvider` 立即释放旧 multiplexer、`RedisCacheInvalidationSubscriber` 退订重订阅，无需重启。
- Redis key：`bb:cache:v:{logicalKey}`（JSON envelope）、`bb:cache:t:{tag}`（SET 索引）；set/remove/tag 失效全部 Lua 原子化；`RedisCacheTagIndexCleaner` 每 10 分钟清理 TTL 过期残留索引；pub/sub `bb:cache:invalidate` 只传 key/tag 字符串并跳过自身节点，用于清各节点 L1。Redis 异常 fail-open，不拖垮 API。
- `KeyedLockPool`：全局短锁 + 引用计数，归零即释放 SemaphoreSlim，替换永久锁字典，支持动态 key 不泄漏。仅限进程内 single-flight；跨进程 single-flight 未实现。
- `/health` 接入 `redis` 检查：未启用 Healthy、PING >1s Degraded、异常按失败状态。真 Redis 集成测试用环境变量 `BB_TEST_REDIS` 开启。
- ALC 红线：L1 宿主单例不能缓存模块 ALC 内部对象；L2 JSON 天然只承载 DTO/共享契约/基础类型。
- 测试位置 `tests/BlazorBoilerplate.ModuleRuntime.Tests/CacheServiceTests.cs`，全量 37/37。设计文档 `docs/CACHING.md`。未做：跨进程 single-flight、L2 EF 查询缓存、命中率/耗时指标。
