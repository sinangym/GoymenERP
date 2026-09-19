namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

// EF row values are kept separate from the aggregate's mutation API. Materializing
// a historical row must never reset its version or make it a newly created Draft.
public sealed class ProductRow
{
    private ProductRow() { }
    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public Guid GroupId { get; private set; }
    public Guid? ClassId { get; private set; }
    public Guid? FamilyId { get; private set; }
    public bool? PurchasableValue { get; private set; }
    public int PurchasableProvenance { get; private set; }
    public bool? SellableValue { get; private set; }
    public int SellableProvenance { get; private set; }
    public bool? ProductionInputValue { get; private set; }
    public int ProductionInputProvenance { get; private set; }
    public bool? ProductionOutputValue { get; private set; }
    public int ProductionOutputProvenance { get; private set; }
    public int Lifecycle { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public string ReferencesJson { get; private set; } = string.Empty;

    public static ProductRow FromRecord(ProductStoredRecord record)
    {
        _ = record.RestoreProduct();
        _ = record.RestoreReferences();
        var row = new ProductRow
        {
            Id = record.Id, Code = record.Code, GroupId = record.GroupId, CreatedAtUtc = record.CreatedAtUtc,
        };
        row.CopyState(record);
        return row;
    }

    public void ApplyEdit(ProductStoredRecord record)
    {
        _ = record.RestoreProduct();
        _ = record.RestoreReferences();
        if (record.Id != Id || record.Code != Code || record.GroupId != GroupId || record.CreatedAtUtc != CreatedAtUtc
            || Lifecycle != (int)Domain.Products.ProductLifecycle.Draft
            || record.Lifecycle != Lifecycle || record.Version != checked(Version + 1))
            throw new InvalidOperationException("A basic Product edit must preserve identity and advance one version.");
        CopyState(record);
    }

    // DEC-PRD-010 approval: the one lifecycle move this slice writes. Identity and every card value
    // stay; only Draft -> Active, one version and the update time change.
    public void ApplyActivation(ProductStoredRecord record)
    {
        _ = record.RestoreProduct();
        _ = record.RestoreReferences();
        if (record.Id != Id || record.Code != Code || record.GroupId != GroupId || record.CreatedAtUtc != CreatedAtUtc
            || record.Name != Name || record.ClassId != ClassId || record.FamilyId != FamilyId
            || record.ReferencesJson != ReferencesJson
            || record.Purchasable.Value != PurchasableValue || record.Purchasable.Provenance != PurchasableProvenance
            || record.Sellable.Value != SellableValue || record.Sellable.Provenance != SellableProvenance
            || record.ProductionInput.Value != ProductionInputValue || record.ProductionInput.Provenance != ProductionInputProvenance
            || record.ProductionOutput.Value != ProductionOutputValue || record.ProductionOutput.Provenance != ProductionOutputProvenance
            || Lifecycle != (int)Domain.Products.ProductLifecycle.Draft
            || record.Lifecycle != (int)Domain.Products.ProductLifecycle.Active
            || record.Version != checked(Version + 1))
            throw new InvalidOperationException("A Product activation must preserve the card and move Draft to Active by one version.");
        CopyState(record);
    }

    public ProductStoredRecord ToRecord() => new(Id, Code, Name, GroupId, ClassId, FamilyId,
        new(PurchasableValue, PurchasableProvenance), new(SellableValue, SellableProvenance),
        new(ProductionInputValue, ProductionInputProvenance), new(ProductionOutputValue, ProductionOutputProvenance),
        Lifecycle, Version, CreatedAtUtc, UpdatedAtUtc, ReferencesJson);

    private void CopyState(ProductStoredRecord record)
    {
        Name = record.Name;
        ClassId = record.ClassId;
        FamilyId = record.FamilyId;
        PurchasableValue = record.Purchasable.Value;
        PurchasableProvenance = record.Purchasable.Provenance;
        SellableValue = record.Sellable.Value;
        SellableProvenance = record.Sellable.Provenance;
        ProductionInputValue = record.ProductionInput.Value;
        ProductionInputProvenance = record.ProductionInput.Provenance;
        ProductionOutputValue = record.ProductionOutput.Value;
        ProductionOutputProvenance = record.ProductionOutput.Provenance;
        Lifecycle = record.Lifecycle;
        Version = record.Version;
        UpdatedAtUtc = record.UpdatedAtUtc;
        ReferencesJson = record.ReferencesJson;
    }
}

// All current and retired codes share one namespace. No rename/retire/delete
// command exists in this slice; a retired code remains reserved for its Product.
public sealed class ProductCodeRegistration
{
    private ProductCodeRegistration() { }
    public string Code { get; private set; } = string.Empty;
    public Guid ProductId { get; private set; }
    public bool IsCurrent { get; private set; }

    public static ProductCodeRegistration Current(ProductRow product) => new()
    {
        Code = product.Code, ProductId = product.Id, IsCurrent = true,
    };
}
