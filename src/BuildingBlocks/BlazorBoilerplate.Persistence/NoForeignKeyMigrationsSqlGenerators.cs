using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;

namespace BlazorBoilerplate.Persistence;

/// <summary>
/// 多数据库「移除外键」迁移 SQL 生成器集合：
/// 代码层面可正常配置 EF Core 导航属性用于 LINQ 连表查询（Include / Join），
/// 但在生成建表与迁移 SQL 时统一剥离 FOREIGN KEY 约束，引用完整性由应用服务层维护。
/// 1) CreateTableOperation 前清空 ForeignKeys；
/// 2) AddForeignKeyOperation / DropForeignKeyOperation 均跳过。
/// </summary>
#pragma warning disable EF1001 // EF Core internal API（INpgsqlSingletonOptions）
internal sealed class CustomNpgsqlMigrationsSqlGenerator(
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
        operation.ForeignKeys.Clear();
        base.Generate(operation, model, builder, terminate);
    }

    protected override void Generate(
        AddForeignKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
    }

    protected override void Generate(
        DropForeignKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
    }
}
#pragma warning restore EF1001

/// <summary>
/// Oracle MySql.EntityFrameworkCore 的 MySQLMigrationsSqlGenerator 为 internal 类型，
/// 因此通过包装 IMigrationsSqlGenerator 在转发前统一剥离外键操作。
/// </summary>
internal sealed class CustomMySqlMigrationsSqlGenerator(IServiceProvider serviceProvider)
    : IMigrationsSqlGenerator
{
    private static readonly Type InnerGeneratorType =
        Type.GetType("MySql.EntityFrameworkCore.Migrations.MySQLMigrationsSqlGenerator, MySql.EntityFrameworkCore")
        ?? throw new InvalidOperationException("Cannot locate MySQLMigrationsSqlGenerator.");

    private readonly IMigrationsSqlGenerator _inner =
        (IMigrationsSqlGenerator)ActivatorUtilities.CreateInstance(serviceProvider, InnerGeneratorType);

    public IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
    {
        var filtered = new List<MigrationOperation>(operations.Count);
        foreach (var op in operations)
        {
            if (op is AddForeignKeyOperation or DropForeignKeyOperation)
                continue;

            if (op is CreateTableOperation createTable)
                createTable.ForeignKeys.Clear();

            filtered.Add(op);
        }

        return _inner.Generate(filtered, model, options);
    }
}

internal sealed class CustomSqlServerMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    ICommandBatchPreparer commandBatchPreparer)
    : SqlServerMigrationsSqlGenerator(dependencies, commandBatchPreparer)
{
    protected override void Generate(
        CreateTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        operation.ForeignKeys.Clear();
        base.Generate(operation, model, builder, terminate);
    }

    protected override void Generate(
        AddForeignKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
    }

    protected override void Generate(
        DropForeignKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
    }
}

internal sealed class CustomSqliteMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    IRelationalAnnotationProvider migrationsAnnotations)
    : SqliteMigrationsSqlGenerator(dependencies, migrationsAnnotations)
{
    protected override void Generate(
        CreateTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        operation.ForeignKeys.Clear();
        base.Generate(operation, model, builder, terminate);
    }

    protected override void Generate(
        AddForeignKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
    }

    protected override void Generate(
        DropForeignKeyOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
    }
}
