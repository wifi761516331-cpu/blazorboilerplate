# Demo 模块开发模板（Developer Quick-Start）

本模块演示了平台全部核心扩展点，开发者可直接复制本目录作为新模块脚手架。

## 目录结构

- **后端 (`BlazorBoilerplate.Modules.Demo`)**
  - `manifest.json`：模块元数据、`displayOrder`、依赖、权限与前端菜单声明
  - `localization/*.json`：多语言词条（加载模块时自动同步）
  - `DemoModule.cs`：`IModule` 主入口（核心服务、私有 DbContext 与迁移、主端点与运行期生命周期）
  - `DemoDbContext.cs`：继承 `ModuleDbContextBase` 的模块私有 `DbContext`（映射 `DemoNotes` 表，自动软删除过滤 + 审计/领域事件拦截）
  - `DemoDatabaseMigrations.cs`：`IModuleDatabaseMigrations` 增量迁移与幂等种子（模块加载时自动建表并写入初始记录）
  - `DemoStartup.cs`：`IModuleStartup` 分片启动器（按 `Order` 升序自动执行）
  - `DemoLifecycleHooks.cs`：`IModuleLifecycleHooks`（首次安装、跨版本升级、彻底卸载清理）
  - `DemoSettings.cs`：`ISettings` 强类型配置（支持运维中心编辑）
  - `DemoScheduleTask.cs`：`IScheduleTask` 后台定时任务（自动注册到运维中心）
  - `DemoProviders.cs`：`IProvider` 可插拔策略提供者
  - `DemoEntityConsumer.cs`：`IConsumer<EntityInsertedEvent<T>>` 领域事件自动订阅
  - `DemoEndpoints.cs`：`IDistributedLockService`、`ICacheService`、`IActivityLogService`、`IMessageTemplateService`、`IQueuedEmailService`、`IRuleEngine`、`ISearchService`、`IDataExchangeService`、`IProviderManager`、`IEventPublisher` 综合示范

- **前端 (`BlazorBoilerplate.Modules.Demo.Frontend`)**
  - `DemoClientStartup.cs`：`IModuleClientStartup` + `IClientService` + `WidgetZoneRegistry.Register(...)`
  - `Widgets/DemoHomeWidget.razor`：挂载在插槽中的微件组件
  - `Pages/Demo.razor(.cs)`：模块工作台页面（路由 `/demo`）

## 复制为新模块的最小步骤

1. 复制 `src/Modules/Demo` 为 `src/Modules/<YourModule>`，重命名 `.csproj`、命名空间与类名。
2. 修改 `manifest.json` 中的 `id`、`name`、`displayName`、`entryAssembly`、`entryType`、`permissions`、`frontend.assembly`、`frontend.routes`、`frontend.menuItems`。
3. 将新模块前后端 `.csproj` 加入根目录 `BlazorBoilerplate.slnx`。
4. 通过「模块管理 → 开发打包」生成 `.zip`，上传安装并启用即可热加载生效。

## 可用微件插槽（WidgetZone）

- 全局插槽：`header_actions`、`account_menu_items`、`layout_body_top`、`layout_body_bottom`、`home_widgets_top`、`home_widgets_bottom`
- 路由自动插槽（无需改目标模块）：`page_<路由>_top` / `page_<路由>_bottom`（如 `page_catalog_top`、`page_identity_users_top`）
- 模块内精细插槽：`<模块>_<页面>_top` / `<模块>_<页面>_bottom`（如 `catalog_products_top`）

