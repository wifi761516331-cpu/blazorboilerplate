# ADR 0003：不使用数据库级外键（参考 Nova）

- 状态：已采纳（2026-11-14，用户要求参考 `E:\ELing\Nova` 的做法）

## 背景
Nova 项目通过自定义 `NpgsqlMigrationsSqlGenerator` 剥离外键：
生成 `CreateTableOperation` 时 `operation.ForeignKeys.Clear()`，并直接跳过 `AddForeignKeyOperation`；
在运行时 `DbContextOptions` 与设计时工厂两处 `ReplaceService<IMigrationsSqlGenerator, ...>` 注册。
其 Identity 实体采用 Guid v7 主键、私有 setter、领域工厂方法、自引用 `ParentId`。

## 决策
BlazorBoilerplate 的 Identity 模块照此实现，并额外在**模型层**就不配置任何导航关系（只有标量列 + 索引），
因此 PostgreSQL 与 SQLite 两条路径天然都不会产生外键：
- PostgreSQL：`CustomNpgsqlMigrationsSqlGenerator`（Nova 方案）+ `MigrateAsync`。
- SQLite：开发/演示降级路径，`EnsureCreated`（EF 迁移仅针对 PG 生成）。

引用完整性全部由 `UserService` / `RoleService` / `MenuService` 在代码层保证：
用户名/邮箱/角色名唯一性、父菜单存在性、循环引用守卫、删除时的 UserRoles / RoleMenus 级联清理、存在子菜单时拒绝删除。

## 后果
- 跨模块、跨库、分库分表与模块单独卸载时不会因 FK 阻塞。
- 应用层必须承担全部一致性责任；并发写入下唯一性需额外加锁/约束（当前为单实例演示）。
- 已核验：PG 迁移脚本 `FOREIGN KEY` / `ADD CONSTRAINT` 计数均为 0；SQLite 5 张表 `PRAGMA foreign_key_list` 全为空。
- SQLite 不支持 `DateTimeOffset` 出现在 `ORDER BY`，服务层改用时间有序的 Guid v7 主键排序（PG/SQLite 均可翻译）。
