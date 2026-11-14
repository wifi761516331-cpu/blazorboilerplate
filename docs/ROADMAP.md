# BlazorBoilerplate 实施路线图与状态

状态标记：✅ 完成并实测 / 🧪 离线核验 / ⏳ 待办

## 批次 1：可运行骨架 + 动态模块运行时 POC
- ✅ .NET 10 解决方案、中央包版本管理、`global.json` rollForward。
- ✅ 可回收 `AssemblyLoadContext` + 动态 `ModuleEndpointDataSource`：加载后端点上线，卸载后 404，**不重启主 Host**。
- ✅ 模块包校验（路径穿越防护、契约版本、入口程序集、SHA-256 可选校验）。
- ✅ Catalog 示例模块：前后端同体 RCL + OData 子集（`$filter`/`$orderby`/`$top`/`$skip`/`$count`）。
- ✅ Bit.BlazorUI 10.7.0（核心 + Extras + Icons）资源与组件接入，API 全部按反射/XML 文档核对。

## 批次 2：nopCommerce 风格加载顺序 + 首装引导
- ✅ `displayOrder` + `dependencies` → 确定性拓扑排序（依赖优先 → DisplayOrder → Id）。
- ✅ 缺失依赖 / 循环依赖校验 API `GET /api/modules/load-graph`。
- ✅ `POST /api/modules/load-all` 按序批量加载；依赖失败方标记 Failed 但不阻断其余。
- ✅ 启动自动加载：`AutoLoadOnStartup`（默认 true）且仅在 `SetupMode.Normal` 下执行。
- ✅ `isEssential` 必要模块：`UnloadAsync` 服务端拒绝 + UI 隐藏卸载按钮。
- ✅ 首次运行：Bootstrap 423 门槛 → `/setup` 配置页 → `complete` 持久化 + 自动加载。
- ✅ 修复 4 个根因级 gotcha：全局 `DevBypass`、静态类泛型日志、`NavigationException` 被 catch 吞掉、RCL 页面需 `AddAdditionalAssemblies` 双处声明。

## 批次 3：EF Core Migrations + 用户/角色/菜单（当前批次）
- ✅ `Identity` 必要模块（displayOrder=10），5 张表：Users / Roles / Menus / UserRoles / RoleMenus。
- ✅ 参考 Nova 的**无外键**方案：模型不配置关系 + `CustomNpgsqlMigrationsSqlGenerator`；完整性由服务层保证。
- 🧪 `Initial_Identity` 迁移：PG 脚本 `FOREIGN KEY` = 0、`ADD CONSTRAINT` = 0；SQLite 5 表 FK 列表全空。
- ✅ `SetupInitializer`：PG → `MigrateAsync`，SQLite → `EnsureCreated`；种子管理员、系统角色、核心菜单、模块 manifest 菜单、角色授权；种子幂等。
- ✅ 用户 / 角色 / 菜单服务与 `/api/identity/*` 端点（代码级唯一性、系统角色保护、菜单树与循环守卫、级联清理）。
- ✅ Boilerplate 风格管理页（左侧可搜索 `BitNav` + 右侧 `BitPivot` 详情 + `BitDialog`），`/identity/users|roles|menus`。
- ✅ 侧边栏优先使用 DB 菜单（`/api/identity/menus/visible`），失败回退 `ModuleFrontendRegistry`。
- ✅ 修复：SQLite 不支持 `DateTimeOffset` 排序（改用时间有序 Guid v7 主键）、菜单循环守卫方向写反、卸载后状态为 `Uninstalled` 导致无法重载。
- ✅ 单元测试 13/13；全量构建 0 警告 0 错误；端到端 CRUD + 动态装卸实测。

## 批次 4：待办
- ⏳ 认证与授权：登录页、密码校验接入、JWT/Cookie、按角色菜单过滤、`permission` 元数据真正生效。
- ⏳ 基础设施连通性测试：PG / Redis / MinIO / SMTP 在向导内实连校验；Secret 加密存储。
- ⏳ 恢复模式（`SetupMode.Recovery`）：已安装后修改配置。
- ⏳ 动态模块**前端**路由自动发现（当前 `ComponentAssemblies` 为静态清单，属独立 POC）。
- ⏳ 模块包签名与远程仓库、上传/升级/回滚、审计日志持久化。
- ⏳ ALC 卸载 `WeakReference` 自动断言、`WebApplicationFactory` 动态端点增删测试、架构依赖测试。
- ⏳ OData 完整语法（当前为受控子集）、Bit.BlazorUI.Extras `BitDataGrid` 接入管理页。
- ⏳ `deploy/docker-compose.yml` 在真实 Docker 环境验证（本机 Docker 不可用）。
- ⏳ 生产化：健康检查探针、限流分级、OpenTelemetry、迁移备份策略。

## 明确不做
- ❌ RabbitMQ / MassTransit v9+（商业授权；事件总线若需要优先 MIT 方案，见 `docs/adr/0005-messaging-license.md`）。
- ❌ 数据库级外键（遵循 Nova 约定，跨模块表不建立物理约束）。
