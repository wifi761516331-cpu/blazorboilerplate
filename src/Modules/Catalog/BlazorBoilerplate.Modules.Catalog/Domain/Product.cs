using BlazorBoilerplate.Domain;

namespace BlazorBoilerplate.Modules.Catalog.Domain;

/// <summary>模块私有聚合根，不跨模块暴露。</summary>
public sealed class Product : AuditableEntity<Guid>, IAggregateRoot
{
    private Product() { }

    public Product(string name, decimal price, string category)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Guard.AgainstNullOrWhiteSpace(category, nameof(category));

        Id = Guid.NewGuid();
        Name = name;
        Price = price;
        Category = category;
        MarkCreated(DateTimeOffset.UtcNow, null);
    }

    public string Name { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public string Category { get; private set; } = string.Empty;
}
