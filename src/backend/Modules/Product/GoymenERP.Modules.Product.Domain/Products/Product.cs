namespace GoymenERP.Modules.Product.Domain.Products;

/// <summary>
/// Group-shared master identity (PRD-SCOPE-001). Physical lot/quality and company
/// ownership belong to downstream records. Creation does not activate the card.
/// </summary>
public sealed class Product
{
    private Product()
    {
    }

    public Product(Guid id, ProductCode code, string name, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Ürün kimliği boş olamaz.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Code = code;
        Name = name.Trim();
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public ProductCode Code { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public ProductLifecycle Lifecycle { get; private set; } = ProductLifecycle.Draft;
    public long Version { get; private set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
