namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

internal interface IProductPriceListTenantRow
{
    Guid CompanyId { get; }
}

/// <summary>Organization's company table, referenced for the foreign key only (never migrated here).</summary>
internal sealed class PriceListCompanyReference
{
    public Guid Id { get; set; }
}

/// <summary>
/// The Product card, read-only from this context (never migrated or written here): the lines' foreign key and the
/// facts the price-list checks need (lifecycle, the sellable role, the unit bindings in the references snapshot).
/// </summary>
internal sealed class PriceListProductReference
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Lifecycle { get; set; }
    public bool? SellableValue { get; set; }
    public string ReferencesJson { get; set; } = string.Empty;
}

/// <summary>The list header; only Name, Version and UpdatedAtUtc move after the first commit.</summary>
internal sealed class PriceListRow : IProductPriceListTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public int Channel { get; set; }
    public int VatBasis { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>A version; a Draft's validity may change, and the Draft → Published move sets State and the publication facts once.</summary>
internal sealed class PriceListVersionRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid PriceListId { get; set; }
    public int VersionNo { get; set; }
    public Guid VersionId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidThrough { get; set; }
    public int State { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public string? PublishedBy { get; set; }
}

/// <summary>A price line; lines of a Draft are replaced as a whole, lines of a published version never change.</summary>
internal sealed class PriceListLineRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid PriceListId { get; set; }
    public int VersionNo { get; set; }
    public int LineNo { get; set; }
    public Guid LineId { get; set; }
    public Guid ProductId { get; set; }
    public Guid PriceUnitId { get; set; }
    public decimal LowerQuantity { get; set; }
    public decimal? UpperQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? FloorUnitPrice { get; set; }
}

/// <summary>ADR-011: one attempt per (company, command, key hash); a stored result or a recorded terminal refusal.</summary>
internal sealed class PriceListAttemptRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public int Outcome { get; set; }
    public Guid? PriceListId { get; set; }
    public string? RefusalJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}
