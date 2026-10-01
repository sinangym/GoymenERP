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

    // Deactivation (Active -> Inactive) and reactivation (Inactive -> Active): as for the approval, only
    // the lifecycle, one version and the update time change; identity and every card value stay.
    public void ApplyLifecycleChange(ProductStoredRecord record, Domain.Products.ProductLifecycle from,
        Domain.Products.ProductLifecycle to)
    {
        _ = record.RestoreProduct();
        _ = record.RestoreReferences();
        if (from == to || from == Domain.Products.ProductLifecycle.Draft || to == Domain.Products.ProductLifecycle.Draft
            || record.Id != Id || record.Code != Code || record.GroupId != GroupId || record.CreatedAtUtc != CreatedAtUtc
            || record.Name != Name || record.ClassId != ClassId || record.FamilyId != FamilyId
            || record.ReferencesJson != ReferencesJson
            || record.Purchasable.Value != PurchasableValue || record.Purchasable.Provenance != PurchasableProvenance
            || record.Sellable.Value != SellableValue || record.Sellable.Provenance != SellableProvenance
            || record.ProductionInput.Value != ProductionInputValue || record.ProductionInput.Provenance != ProductionInputProvenance
            || record.ProductionOutput.Value != ProductionOutputValue || record.ProductionOutput.Provenance != ProductionOutputProvenance
            || Lifecycle != (int)from || record.Lifecycle != (int)to
            || record.Version != checked(Version + 1))
            throw new InvalidOperationException("A Product lifecycle change must preserve the card and move by one version.");
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
    private const int MaximumCodeLength = Domain.Products.ProductCode.MaximumPrefixLength + 1 + 19;

    private ProductCodeRegistration() { }
    public string Code { get; private set; } = string.Empty;
    public Guid ProductId { get; private set; }
    public bool IsCurrent { get; private set; }

    public static ProductCodeRegistration Current(ProductRow product) => new()
    {
        Code = product.Code, ProductId = product.Id, IsCurrent = true,
    };

    public static ProductCodeRegistration Legacy(ProductRow product, string code)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var normalized = code.Trim();
        if (!IsStorageCompatibleLegacyCode(normalized) || code.Any(char.IsControl))
            throw new ArgumentException("Eski ürün kodu geçersiz.", nameof(code));
        return new() { Code = normalized, ProductId = product.Id, IsCurrent = false };
    }

    // CodeRegistrations.Code is varchar under Latin1_General_100_BIN2 (Windows-1252).
    // Refuse aliases SQL Server cannot preserve byte-for-byte instead of allowing
    // replacement-character collisions at the participant boundary.
    internal static bool IsStorageCompatibleLegacyCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code.Length is > 0 and <= MaximumCodeLength && code.All(IsWindows1252IdentityCharacter);
    }

    private static bool IsWindows1252IdentityCharacter(char character)
    {
        if (char.IsControl(character)) return false;
        return character <= '\u007f'
               || character is >= '\u00a0' and <= '\u00ff'
               || character is '\u20ac' or '\u201a' or '\u0192' or '\u201e' or '\u2026' or '\u2020'
                   or '\u2021' or '\u02c6' or '\u2030' or '\u0160' or '\u2039' or '\u0152' or '\u017d'
                   or '\u2018' or '\u2019' or '\u201c' or '\u201d' or '\u2022' or '\u2013' or '\u2014'
                   or '\u02dc' or '\u2122' or '\u0161' or '\u203a' or '\u0153' or '\u017e' or '\u0178';
    }
}
