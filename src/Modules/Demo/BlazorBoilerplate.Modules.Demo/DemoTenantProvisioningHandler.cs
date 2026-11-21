using BlazorBoilerplate.ModuleAbstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorBoilerplate.Modules.Demo;

/// <summary>新租户创建时自动为其写入一条欢迎业务数据（演示跨模块租户初始化契约）。</summary>
public sealed class DemoTenantProvisioningHandler(IServiceScopeFactory scopeFactory) : ITenantProvisioningHandler
{
    public async Task OnTenantCreatedAsync(Guid tenantId, string tenantName, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DemoDbContext>();
        var exists = await db.Notes.IgnoreQueryFilters()
            .AnyAsync(n => !n.IsDeleted && n.TenantId == tenantId, cancellationToken);
        if (exists) return;

        db.Notes.Add(new DemoNote
        {
            TenantId = tenantId,
            Title = $"欢迎使用租户「{tenantName}」（新租户默认业务种子数据）"
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}

