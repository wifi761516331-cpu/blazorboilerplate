# 技术验证记录：EF Core Migrations + Identity 端到端（2026-11-14）

## 环境
- SDK：本机无 .NET 10 SDK，通过 `global.json` `rollForward: latestMajor` 使用 11.0.100-rc.3 构建 net10.0。
- 包：EF Core 10.0.11 全家桶 / Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 / Microsoft.EntityFrameworkCore.Sqlite 10.0.11（均已在 NuGet 缓存）。
- 工具：`dotnet-ef` 10.0.7（比运行时旧，仅提示，不影响生成）。
- 本机无 PostgreSQL 监听；Docker 不可用 → 主路径用离线生成的 PG 迁移脚本核验，运行期用 SQLite 降级路径端到端验证。

## 验证项与结果

| # | 验证项 | 命令 / 请求 | 结果 |
| --- | --- | --- | --- |
| 1 | 生成初始迁移 | `dotnet ef migrations add Initial_Identity` | 生成 `20261113180827_Initial_Identity.cs`，5 张表 |
| 2 | PG 迁移无外键 | `dotnet ef migrations script --no-build` | SQL 2455 字节，`FOREIGN KEY` = 0，`ADD CONSTRAINT` = 0 |
| 3 | SQLite 库无外键 | `PRAGMA foreign_key_list` | Menus/RoleMenus/Roles/UserRoles/Users 全部 0 |
| 4 | 全新首装 | 删 `data/*` → 启动双端 | `mode=Bootstrap`，`/api/modules` 423，`/` 302 → `/setup` |
| 5 | 完成配置 | `POST /api/setup/complete`（SQLite） | 200，`mode=Normal`，identity(order=10, essential) 与 catalog(order=100) 均 Running |
| 6 | 种子数据 | `GET /api/identity/{users,roles,menus/visible}` | 管理员 admin / 2 系统角色 / 6 菜单（含 manifest 声明的「商品目录」sort=110） |
| 7 | 用户 CRUD | POST/GET/PUT/DELETE | 创建、重复用户名 409、分配角色、重置密码、启停、改资料、删除全通 |
| 8 | 角色 CRUD | POST/PUT/DELETE | 创建、分配菜单、系统角色改名/删除 409、删除全通 |
| 9 | 菜单 CRUD | POST/PUT/DELETE + tree | 建父子、循环引用 409、有子菜单不可删 409、按序删除成功、树正常 |
| 10 | 动态装卸 | `POST /api/modules/{id}/unload|load` | identity 卸载 409；catalog 卸载后端点 404，重载后恢复 200 |
| 11 | 前端页面 | `/ /modules /catalog /identity/users|roles|menus` | 全部 200；侧边栏出现 3 个 DB 菜单 + 商品目录 |
| 12 | 回归 | `dotnet build BlazorBoilerplate.slnx` / `dotnet test` | 0 警告 0 错误 / 13 个测试全过 |

## 过程中发现并修复的缺陷（根因）
1. **SQLite 不支持 `DateTimeOffset` 出现在 ORDER BY** → 三个 `ListAsync` 报 `NotSupportedException` 500。改用时间有序的 Guid v7 主键排序（PG/SQLite 均可翻译）。
2. **菜单循环引用守卫方向写反**：原实现向下遍历 `targetParent` 的子树找 `nodeId`，实际应沿 `ParentId` **向上**追溯 `targetParent` 的祖先链。错误实现导致「把父菜单挂到自己子菜单下」返回 204 并造成父子互指、两者都无法删除。
3. **卸载后无法重载**：`UnloadAsync` 把状态置为 `Uninstalled`，而状态机只允许 `Uninstalled → Discovered`。动态卸载语义应为「停用但保留包」，改为回到 `Installed`，并在 `Unloading` 迁移表加入 `Installed`。
4. **`BitCallout.Color` 不是 10.7.0 的参数**：未知参数被 `CaptureUnmatchedValues` 静默变成 HTML 属性；改用 `Class` 表达状态。
5. **`BitNavItem.IsEnabled` 不存在**（应为 `IsDisabled`）；`BitNav.OnSelectItem` 为 `EventCallback<TItem>`，方法组直接赋值在 Razor 泛型推断下报 CS1503，改为显式 lambda `@((BitNavItem item) => ...)`。
6. **模块 ALC 依赖重复加载风险**：Npgsql / Bit.* 不在共享清单，会在模块 ALC 里加载第二份导致类型身份冲突；已把这些程序集加入 `ModuleLoadContext.SharedAssemblies`。
7. **模块 manifest 菜单未进 DB**：ApiHost 不静态引用 Catalog，`ModuleFrontendRegistry` 在 Host 侧为空。新增 `ModuleFrontendManifest.MenuItems` 结构化声明 + `ModuleCatalog.Manifests()`，安装时把模块菜单写入「菜单管理」表。

## 结论
EF Core Migrations 已接入首装向导；用户/角色/菜单功能端到端可用；无外键约定在 PG 迁移产物与 SQLite 实际库两侧均成立。
真实 PostgreSQL 实例的 `MigrateAsync`、登录鉴权、动态模块前端路由仍待验证（见 `docs/ROADMAP.md` 批次 4）。
