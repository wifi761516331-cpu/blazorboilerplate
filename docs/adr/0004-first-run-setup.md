# ADR 0004：首次运行必须经过配置页（Bootstrap 门槛）

- 状态：已采纳（2026-11-14，用户明确要求「首次运行项目的时候一定要启动一个配置页面初始化数据库等配置」）

## 决策
- `SetupStateProvider` 以 `data/setup.json` 是否存在判定安装；不存在即 `SetupMode.Bootstrap`。
- ApiHost 中间件：Bootstrap 下除 `/api/setup`、`/health`、`/openapi` 外一律 **423 Locked**，模块不加载、端点不上线。
- WebHost `MainLayout` 守卫：未安装时 `/` 与 `/modules` 一律重定向到 `/setup`。
- `Setup.razor` 表单收集管理员邮箱/密码、系统名称、数据库类型（PostgreSQL / SQLite）与 PG/SQLite/Redis/MinIO/SMTP 连接串。
- `POST /api/setup/complete` → `ISetupInitializer.InitializeAsync`：持久化 `data/blazorboilerplate.settings.json` → EF Core 建库建表 → 种子数据 → 写标记 → 按序自动加载模块。
- `SetupOptions.DevBypass` 默认 **false**，且不得写在全局 `appsettings.json`（否则生产也绕过引导）；只允许在 `appsettings.Development.json` 按需开启。
- 重复 `complete` 返回 409；重置首装 = 删除 `data/` 下的 `setup.json`、`blazorboilerplate.settings.json`、`blazorboilerplate.db*`。

## 关键 gotcha（已修复并写入项目记忆）
1. Blazor 静态预渲染阶段 `NavigateTo()` 抛出的 `NavigationException` **是重定向信号**，绝不能被自身 `try/catch` 吞掉；导航必须在 catch 之外。
2. `MapRazorComponents<App>()` 只扫描入口程序集，RCL 页面必须在 `Program.cs` 与 `Routes.razor` 两处 `AddAdditionalAssemblies`。
3. `ILogger<StaticClass>` 触发 CS0718，需引入非静态的日志分类标记类。
4. `BitCallout` 在 10.7.0 没有 `Color` 参数；未知参数会被当成 HTML 属性静默吞掉，应用 `Class` 表达状态。

## 未决
- 向导目前只持久化 Redis/MinIO/SMTP 配置，未做连通性测试；Secret 加密存储、恢复模式、登录鉴权属批次 4。
