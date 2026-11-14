using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;

namespace BlazorBoilerplate.Modules.Identity.Persistence;

/// <summary>
/// 参考 Nova 项目的「移除外键」方案（Nova.Framework.MultiTenancy/
/// CustomNpgsqlMigrationsSqlGenerator）：
/// 1) 生成建表 SQL 前清空 CreateTableOperation.ForeignKeys；
/// 2) 跳过 AddForeignKeyOperation。
/// 结果：迁移产物（以及 EnsureCreated 建表）中永远不出现 FOREIGN KEY 约束，
/// 引用完整性完全由应用服务层维护。
/// 需要在运行时 UseNpgsql 之后 ReplaceService 注册（见 IdentityDbContextOptions），
/// 设计时工厂同样注册，保证 dotnet ef 迁移与运行时行为一致。
/// </summary>
#pragma warning disable EF1001 // EF Core internal API（INpgsqlSingletonOptions）
public sealed class CustomNpgsqlMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    INpgsqlSingletonOptions npgsqlSingletonOptions)
    : NpgsqlMigrationsSqlGenerator(dependencies, npgsqlSingletonOptions)
{
    protected override void Generate(
        CreateTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        // 移除外键约束
        operation.ForeignKeys.Clear();
        base.Generate(operation, model, builder, terminate);
    }

    protected override void Generate(
        AddForeignKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        // 跳过外键添加
    }
}
#pragma warning restore EF1001
