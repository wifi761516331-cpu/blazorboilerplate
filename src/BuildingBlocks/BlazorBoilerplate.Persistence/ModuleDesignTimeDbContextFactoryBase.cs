using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BlazorBoilerplate.Persistence;

/// <summary>
/// 多数据库设计时 DbContext 工厂基类（供 dotnet-ef 迁移命令使用）。
/// 各模块仅需声明空壳子类即可，无需重复编写环境变量与 Provider 配置样板代码。
/// </summary>
public abstract class ModuleDesignTimeDbContextFactoryBase<TContext>(DbSystemType system)
    : IDesignTimeDbContextFactory<TContext>
    where TContext : DbContext
{
    public TContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("BLAZORBOILERPLATE_DESIGN_CONNECTION")
                   ?? GetDefaultDesignConnection(system);

        var builder = new DbContextOptionsBuilder<TContext>();
        DbFactory.Create(system).ConfigureDbContext(builder, conn, enableRetryOnFailure: true);

        return (TContext)(Activator.CreateInstance(typeof(TContext), builder.Options)
            ?? throw new InvalidOperationException($"Failed to instantiate {typeof(TContext).Name}."));
    }

    private static string GetDefaultDesignConnection(DbSystemType system) => system switch
    {
        DbSystemType.MySql => "Server=localhost;Port=3306;Database=blazorboilerplate_design;User=design;Password=design;",
        DbSystemType.SqlServer => "Server=localhost;Database=blazorboilerplate_design;User Id=sa;Password=DesignPassword123!;TrustServerCertificate=True;",
        DbSystemType.Sqlite => "Data Source=blazorboilerplate_design.db",
        _ => "Host=localhost;Port=5432;Database=blazorboilerplate_design;Username=design;Password=design"
    };
}

