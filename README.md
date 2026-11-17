# BlazorBoilerplate Platform

.NET 10 + Bit.BlazorUI 的模块化单体平台骨架：**前后端同体的动态模块**（Razor Class Library）、
nopCommerce 风格的模块加载顺序、可回收 ALC 的运行时装卸，以及带 EF Core Migrations 的首次运行安装向导。

> 基线仓库为 BitPlatform **官方上游** <https://github.com/bitfoundation/bitplatform>，
> 组件文档 <https://blazorui.bitplatform.dev/overview>。组件 API 以实际引用的
> `Bit.BlazorUI / Extras / Icons 10.7.0` 为准，不凭记忆写参数。

## 快速开始

```bash
# 1) 还原并构建（中央包版本管理，global.json 允许 rollForward）
dotnet restore BlazorBoilerplate.slnx
dotnet build BlazorBoilerplate.slnx -c Debug

# 2) 打包内置模块到 modules/installed/<id>/<version>/
bash scripts/build-module.sh catalog  1.0.0
bash scripts/build-module.sh identity 1.0.0

# 3) 启动后端（:5100）与前端（:5200），两个终端分别执行
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Hosts/BlazorBoilerplate.ApiHost -c Debug
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Hosts/BlazorBoilerplate.WebHost -c Debug

# 4) 浏览器打开 http://localhost:5200/
#    首次运行自动 302 → /setup，填写管理员邮箱/密码并选择数据库类型
```

- **PostgreSQL（生产）**：填写连接串，`SetupInitializer` 执行 `MigrateAsync` 应用 `Initial_Identity` 迁移。
- **SQLite（开发/演示，无需外部服务）**：连串留空即使用 `src/Hosts/BlazorBoilerplate.ApiHost/data/blazorboilerplate.db`，走 `EnsureCreated`。
- 重置首装：删除 `src/Hosts/BlazorBoilerplate.ApiHost/data/` 下的 `setup.json`、`blazorboilerplate.settings.json`、`blazorboilerplate.db*`。
- `SetupOptions.DevBypass` 默认 **false**（全局配置未开启）；需要跳过引导时只在 `appsettings.Development.json` 里开启。

## 首次运行链路（已实测）

```
未安装 → ApiHost Bootstrap（除 /api/setup、/health、/openapi 外全部 423）
       → WebHost / 与 /modules 302 → /setup 配置页
       → POST /api/setup/complete
          ① 持久化 data/blazorboilerplate.settings.json
          ② EF Core 建库建表（无数据库级外键）
          ③ 种子：管理员账户 + 系统角色 + 核心菜单 + 模块 manifest 声明的菜单 + 角色授权
          ④ 写 data/setup.json，进入 Normal
          ⑤ 按「依赖优先 + DisplayOrder」自动加载全部已安装模块
       → 前端恢复正常，侧边栏改由 DB 菜单驱动
```

重复 `complete` 返回 409。启动后（Normal 模式）自动按序加载已安装模块；Bootstrap 模式下不加载。

## 目录与架构

| 路径 | 职责 |
| --- | --- |
| `src/BuildingBlocks/BlazorBoilerplate.ModuleAbstractions` | Host 与模块**共享同一份**程序集的稳定契约（manifest/DTO/接口/连接状态） |
| `src/BuildingBlocks/BlazorBoilerplate.ModuleRuntime` | 可收回 ALC、动态 `EndpointDataSource`、生命周期状态机、加载顺序拓扑排序、包校验 |
| `src/Hosts/BlazorBoilerplate.ApiHost` | HTTP 管线、Setup 门槛、模块管理 API、EF 迁移与种子 |
| `src/Hosts/BlazorBoilerplate.WebHost` | Blazor Web App（Interactive Server）+ Bit.BlazorUI + 安装页 + 三个管理页 |
| `src/Modules/Catalog/...` | 示例业务模块（前后端同体 RCL，OData 子集查询，可动态装卸） |
| `src/Modules/Identity/...` | 必要模块：用户 / 角色 / 菜单管理，EF Core 持久化，无外键 |
| `tests/BlazorBoilerplate.ModuleRuntime.Tests` | 加载顺序、依赖图、动态重载与必要模块的单元测试 |

### 模块约定
- 每个模块是**一个 RCL**，Domain / Application / Infrastructure / Endpoints / Razor 页面 / `manifest.json` 全在同一项目里。
- `manifest.json` 声明 `displayOrder`（升序先加载）、`dependencies`（依赖优先）、`isEssential`（不可卸载）、`permissions`、`frontend.menuItems`（安装时写入 DB 菜单表）。
- 新增模块只需打包到 `modules/installed/<id>/<version>/`，无需改动其它模块，也无需按顺序开发。
- 运行时 API：`GET /api/modules/load-order`、`GET /api/modules/load-graph`、`POST /api/modules/load-all`、`POST /api/modules/{id}/load|unload`。
- **动态卸载 = 停用并回收 ALC**，状态回到 `Installed`，可再次 `load`；`Uninstalled` 仅表示包已从目录移除。

### 无外键约定（参考 Nova）
模型层不配置导航关系，只有标量列 + 索引；PostgreSQL 路径再用
`CustomNpgsqlMigrationsSqlGenerator` 清空 `CreateTableOperation.ForeignKeys`、跳过 `AddForeignKeyOperation`。
引用完整性（父节点存在、循环引用、唯一性、级联清理）全部由服务层在**代码层**保证。
已核验：生成的 PG 迁移脚本 `FOREIGN KEY` / `ADD CONSTRAINT` 均为 0；SQLite 库 5 张表 `PRAGMA foreign_key_list` 均为空。

## 功能清单

| 功能 | 入口 |
| --- | --- |
| 首次运行配置向导 | `/setup` |
| 用户管理（增删改、启停、重置密码、分配角色） | `/identity/users` |
| 角色管理（增删改、系统角色保护、分配菜单） | `/identity/roles` |
| 菜单管理（树、父子、隐藏、循环引用守卫） | `/identity/menus` |
| 模块管理（加载/卸载/批量按序加载） | `/modules` |
| 商品目录（OData 子集示例） | `/catalog` |

Identity API 前缀 `/api/identity/`，业务规则冲突返回 409、找不到返回 404。

## 已验证 / 未验证

**已验证（2026-11-14，真实启动 + 真实请求）**
- 全量 `dotnet build BlazorBoilerplate.slnx` 0 警告 0 错误；`dotnet test` 13/13 通过。
- Bootstrap 423 门槛、前端 302 守卫、完成配置后自动加载、重复 complete 409。
- EF 建表 + 种子（管理员 / 2 系统角色 / 6 个菜单，含模块 manifest 声明的「商品目录」）。
- 用户 / 角色 / 菜单完整 CRUD、唯一性校验、系统角色保护、菜单循环引用与「有子菜单不可删」守卫。
- 必要模块卸载返回 409；普通模块卸载后端点 404，再加载后端点恢复 200。
- 6 个前端页面全部 200；侧边栏由 DB 菜单驱动。

**未验证 / 待办**
- 真实 PostgreSQL 实例上的 `MigrateAsync`（本机无 PG 监听；迁移 SQL 已离线生成并核验无外键）。
- Redis / MinIO / SMTP 的实际连通（安装向导目前只持久化配置，未做连通性测试）。
- 登录鉴权与基于角色菜单的访问控制（当前 API 只做 permission 元数据标注，未做认证授权）。
- **动态加载模块的前端路由**：RCL 页面需在 `ComponentAssemblies` 静态声明；运行时新增模块的页面自动发现是独立 POC。
- `deploy/docker-compose.yml` 与 Dockerfile 未在本机运行验证（Docker 不可用）。
- 模块包签名 / 仓库、ALC 卸载 WeakReference 自动化断言、OData 完整语法、管理端审计日志。

详见 `docs/ROADMAP.md`、`docs/adr/` 与 `docs/CACHING.md`（内存 L1 + Redis L2 缓存设计：per-key 防击穿、tag 失效、跨节点失效、ALC 安全约定）。
