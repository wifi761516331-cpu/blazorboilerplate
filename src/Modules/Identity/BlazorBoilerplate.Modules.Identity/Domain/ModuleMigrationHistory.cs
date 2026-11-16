using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Modules.Identity.Domain;

/// <summary>模块增量迁移执行历史（替代原生 SQL 建表与查询，统一由 EF Core 读写）。</summary>
public sealed class ModuleMigrationHistory : AuditableEntity<Guid>
{
    private ModuleMigrationHistory() { }

    private ModuleMigrationHistory(string moduleId, int sequence, string name)
    {
        Id = Guid.CreateVersion7();
        ModuleId = moduleId;
        Sequence = sequence;
        Name = name;
        AppliedAtUtc = DateTimeOffset.UtcNow;
    }

    public string ModuleId { get; private set; } = default!;
    public int Sequence { get; private set; }
    public string Name { get; private set; } = default!;
    public DateTimeOffset AppliedAtUtc { get; private set; }

    public static ModuleMigrationHistory Create(string moduleId, int sequence, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ModuleMigrationHistory(moduleId.Trim(), sequence, name.Trim());
    }
}

