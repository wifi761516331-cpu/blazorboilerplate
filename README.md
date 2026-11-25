# BlazorBoilerplate Platform

.NET 10 + Bit.BlazorUI 的模块化单体平台骨架：**后端模块 + 独立前端 RCL**、nopCommerce 风格的模块加载顺序、可回收 ALC 的运行时装卸，以及带 EF Core Migrations 的首次运行安装向导。

> 基线仓库为 BitPlatform **官方上游** <https://github.com/bitfoundation/bitplatform>，
> 组件文档 <https://blazorui.bitplatform.dev/overview>。组件 API 以实际引用的
> `Bit.BlazorUI / Extras / Icons 10.7.0` 为准，不凭记忆写参数。

## 快速开始

```bash
# 1) 还原并构建（.NET 10 SDK，中央包版本管理，global.json 允许 rollForward）
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

- **PostgreSQL / MySQL / SQL Server（生产）**：在向导里填写连接串，统一走 EF Core `MigrateAsync` 应用各数据库目录下的迁移。
- **SQLite（开发/演示，无需外部服务）**：连串留空即使用 `src/Hosts/BlazorBoilerplate.ApiHost/data/blazorboilerplate.db`，同样由 `ModuleDbMigrator` 执行迁移（无 `EnsureCreated` 分支）。
- 重置首装：删除 `src/Hosts/BlazorBoilerplate.ApiHost/data/` 下的 `setup.json`、`blazorboilerplate.settings.json`、`blazorboilerplate.db*`。
- `SetupOptions.DevBypass` 默认 **false**；跳过引导只在本地覆盖文件里开启。

### 本地密钥配置（重要）

`appsettings.json` 只保留占位值 `not-configured`，**不存放任何真实密钥**。本机私密配置写在被 gitignore 的 `appsettings.Development.local.json`（或对应环境的 `*.local.json`），ApiHost 启动时由 `Program.cs` 显式加载：

```json
{
  "ConnectionStrings": {
    "smtp": "Endpoint=smtp://<host>:587;UserName=<user>;Password=<secret>;EnableSsl=true"
  },
  "Email": { "DefaultFromEmail": "<user>@example.com" },
  "CmsAi": { "ApiKey": "<llm-api-key>" }
}
```

`*.local.json`、`.env`、`data/` 均已在 `.gitignore` 中，不会被提交。生产环境请用环境变量或密钥管理服务注入同名配置项。

## 首次运行链路（已实测）

```
未安装 → ApiHost Bootstrap（除 /api/setup、/api/localization、/health、/openapi 外全部 423）
       → WebHost / 与 /modules 302 → /setup 配置页
       → POST /api/setup/complete
          ① 持久化 data/blazorboilerplate.settings.json（Provider / 连接串 / Redis）
          ② EF Core 建库建表（四种 Provider 统一 MigrateAsync，无数据库级外键）
          ③ 种子：管理员账户 + 系统角色 + 核心菜单 + 模块 manifest 声明的菜单 + 角色授权
          ④ 写 data/setup.json，进入 Normal
          ⑤ 按「依赖优先 + DisplayOrder」自动加载全部已安装模块
       → 前端恢复正常，侧边栏改由 DB 菜单驱动
```

重复 `complete` 返回 409。启动后（Normal 模式）自动按序加载已安装模块；Bootstrap 模式下不加载。

## 目录与架构

解决方案共 28 个项目，依赖单向：`Domain → Application → Infrastructure / Persistence / ModuleAbstractions → ModuleRuntime / 业务模块 → Hosts`，BuildingBlocks 从不反向引用 Hosts 或模块。

| 路径 | 职责 |
| --- | --- |
| `src/BuildingBlocks/BlazorBoilerplate.Domain` | 领域原语（`Entity<T>`、`IDomainEvent`、审计/软删/多租户接口），零依赖 |
| `src/BuildingBlocks/BlazorBoilerplate.Application` | 跨 ALC 共享的稳定契约（`ICacheService`、`IUnitOfWork`、模块状态/DTO） |
| `src/BuildingBlocks/BlazorBoilerplate.Infrastructure` | 横切实现：缓存、事件总线、S3、邮件、本地化、规则/搜索等 |
| `src/BuildingBlocks/BlazorBoilerplate.Persistence` | EF Core 持久化、四 Provider 工厂、无外键迁移生成器、写后领域事件发布 |
| `src/BuildingBlocks/BlazorBoilerplate.ModuleAbstractions` | 模块 SDK 契约：`IModule` / `IModuleStartup` / `IModuleContext` / manifest / 状态机 |
| `src/BuildingBlocks/BlazorBoilerplate.ModuleRuntime` | 可回收 ALC、动态 `EndpointDataSource`、生命周期状态机、加载顺序拓扑排序、包校验 |
| `src/BuildingBlocks/BlazorBoilerplate.Web.Shared` | 前端共享 RCL：API 客户端、权限门、布局/图标、模块前端契约（禁止反向引用 WebHost） |
| `src/Hosts/BlazorBoilerplate.ApiHost` | HTTP 管线、Setup 门槛、认证授权、模块管理 API、OpenAPI、SignalR |
| `src/Hosts/BlazorBoilerplate.WebHost` | Blazor Web App（Interactive Server + WebAssembly 混合）、BFF 代理、模块前端 ALC |
| `src/Hosts/BlazorBoilerplate.WebHost.Client` | Blazor WebAssembly 端，WASM 模块目录与路由裁决 |
| `src/Modules/Attachments / Catalog / Demo / Domains / Identity / Localization` | 业务模块：后端项目 + 独立 `*.Frontend` RCL 项目 |
| `src/Modules/Cms` | 官网内容管理：Domain / Application / Infrastructure / Frontend / Entry 五个项目 |
| `tests/BlazorBoilerplate.ModuleRuntime.Tests` | 加载顺序、依赖图、动态端点、热重载、缓存、事件总线、本地化等单元测试 |

### 模块约定

- 每个模块由**后端项目 + 独立 `*.Frontend` RCL** 组成（Cms 再拆 Domain / Application / Infrastructure）；`manifest.json` 的 `frontend.assembly` 指向前端程序集，为空时兼容旧的单 DLL 模块。
- `manifest.json` 声明 `displayOrder`（就绪集内升序）、`dependencies`（依赖优先，Kahn 拓扑排序）、`isEssential`（不可卸载）、`permissions`、`frontend.menuItems`（安装时写入 DB 菜单表）。
- 加载顺序 = 拓扑排序 + `DisplayOrder` 升序 + Id 兜底；缺依赖/成环在加载前由 `ValidateLoadGraph` 拦截。
- 新增模块只需打包到 `modules/installed/<id>/<version>/`，无需改动其它模块。
- 运行时 API：`GET /api/modules/load-order`、`GET /api/modules/load-graph`、`POST /api/modules/load-all`、`POST /api/modules/{id}/load|unload`。
- **动态卸载 = 停用并回收 ALC**，状态回到 `Installed`，可再次 `load`；`Uninstalled` 仅表示包已从目录移除。
- 前端有两套独立 ALC：WebHost 侧按 `/api/modules` 的 Running 状态加载前端程序集（SSR 用 catch-all 页面动态解析组件，interactive 由 Router 绑定动态程序集集合）；WASM 侧按版本化 URL 下载程序集并在导航时裁决渲染模式。
- 模块私有 DI 以 `ValidateOnBuild=true / ValidateScopes=true` 构建，注册缺依赖会在加载期直接失败并标记 `Failed`，不影响其它模块。

### 无外键约定（参考 Nova）

模型层不配置导航关系，只有标量列 + 索引；四种 Provider 的迁移生成器都在建表时清空 `ForeignKeys` 并跳过 `AddForeignKeyOperation` / `DropForeignKeyOperation`。
引用完整性（父节点存在、循环引用、唯一性、级联清理）由服务层在**代码层**保证。迁移 SQL 中无 `FOREIGN KEY` / `ADD CONSTRAINT FOREIGN KEY`。

## 功能清单

| 功能 | 入口 |
| --- | --- |
| 首次运行配置向导 | `/setup` |
| 用户管理（增删改、启停、重置密码、分配角色） | `/identity/users` |
| 角色管理（增删改、系统角色保护、分配菜单与权限码） | `/identity/roles` |
| 菜单管理（树、父子、隐藏、循环引用守卫） | `/identity/menus` |
| 模块管理（加载/卸载/批量按序加载/上传包） | `/modules` |
| 商品目录（OData 子集示例，域名路由） | `/catalog` |
| 附件管理、多语言管理、域名路由示例 | `/attachments`、`/localization`、`/domains` |
| 官网内容与布局管理、可视化设计器、站点门户 | `/cms`、`/cms/layouts`、`/site` |

Identity API 前缀 `/api/identity/`，业务规则冲突返回 409、找不到返回 404。认证为自签 HS256 JWT（access 2 分钟 / refresh 7 天），WebHost 侧用 HttpOnly Cookie + BFF 同源代理，前端不接触明文令牌；权限码统一由 `UseEndpointPermissionGuard` 校验（401/403）。

## 已验证 / 未验证

**已验证（最近一次：2026-11-25）**
- 全量 `dotnet build BlazorBoilerplate.slnx` 0 警告 0 错误；`dotnet test` **74/74 通过**。
- Bootstrap 423 门槛、前端 302 守卫、完成配置后自动加载、重复 complete 409。
- EF 建表 + 种子（管理员 / 系统角色 / 核心菜单 + 模块 manifest 菜单 + 角色授权）。
- 用户 / 角色 / 菜单完整 CRUD、唯一性校验、系统角色保护、菜单循环引用守卫。
- 必要模块卸载被拒；普通模块卸载后端点 404，再加载后恢复 200。
- 登录鉴权、JWT 刷新、基于角色/权限码的菜单与按钮级访问控制已实现。

**未验证 / 待办**
- 真实 PostgreSQL / MySQL / SQL Server 实例上的 `MigrateAsync`（迁移 SQL 已离线生成并核验无外键）。
- Redis / MinIO / SMTP 的实际连通性（安装向导目前只持久化配置，未做连通性测试）。
- `SetupMode.Recovery`、Secret 加密存储尚未实现；短信发送为仅记日志的实现。
- 模块包签名 / 远程仓库、ALC 卸载的自动化断言、OData 完整语法、管理端审计日志持久化。
- `deploy/docker-compose.yml` 与 Dockerfile 未在本机运行验证（Docker 不可用）。

详见 `docs/ROADMAP.md`、`docs/adr/` 与 `docs/CACHING.md`（内存 L1 + Redis L2 缓存设计：per-key 防击穿、tag 失效、跨节点失效、ALC 安全约定）。
