# ADR 0002：nopCommerce 风格的模块加载顺序与启动自动加载

- 状态：已采纳（2026-11-13 ~ 11-14）

## 背景
用户希望像 nopCommerce 插件那样：每个模块自带加载顺序，可以在中间任意插入、无需按顺序开发。

参考 nopCommerce：`plugin.json` 的 `SystemName / Version / DisplayOrder / FileName / DependsOnSystemNames`，
`IPlugin.InstallAsync/UninstallAsync`，`IRouteProvider.RegisterRoutes` 的 Priority，
`PluginManager` 从 `~/Plugins/` 加载，依赖缺失阻止安装、存在依赖方阻止卸载。

## 决策
- `ModuleManifest` 增加 `DisplayOrder`（数值小者先）、复用 `Dependencies`（依赖优先）、`IsEssential`（平台增强：不可卸载）。
- `ModuleCatalog.ResolveLoadOrder()` 做确定性排序：**依赖优先 → DisplayOrder 升序 → Id 兜底**；`ValidateLoadGraph()` 结构化报告缺失依赖与环。
- `ModuleLifecycleManager.LoadAllAsync()` 按序加载；依赖失败方标记 `Failed` 并跳过，不阻断其余。
- 启动时 `AutoLoadOnStartup`（默认 true）且 `SetupMode.Normal` 时自动激活全部已安装模块 —— 与 nopCommerce「已安装插件每次启动自动激活」一致。
- `POST /api/setup/complete` 完成后同样按序自动加载，首次运行无需重启。
- 动态卸载只停用并回收 ALC，状态回到 `Installed`（可再次加载）；`Uninstalled` 保留给「包从目录移除」。

## 后果
- 新增模块只改自己的 `manifest.json`，其它模块零改动。
- 必要模块（Identity）无法卸载，其余模块可任意装卸。
- Blazor Server 的页面路由由 RCL `@page` 静态发现，不引入 nopCommerce 的 `IRouteProvider.Priority`（避免与 `EndpointDataSource` 语义重复）；HTTP 端点排序由加载顺序间接决定。
