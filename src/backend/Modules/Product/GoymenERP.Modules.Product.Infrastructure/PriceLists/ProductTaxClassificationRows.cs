namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// MOD-03 (SAT-D73/D74/D82): a product's VAT class (required) and withholding class (null = no withholding) in one company,
/// referring to that company's Settings tax categories by id (checked through the Settings port; no foreign key across
/// modules). Only the classes, the version and the update facts move; the history is the audit trail.
/// </summary>
internal sealed class ProductTaxClassificationRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid ProductId { get; set; }
    public Guid VatClassId { get; set; }
    public Guid? WithholdingClassId { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// The terminal answer of one classification command under one key (ADR-011): company, command and the SHA-256 of the key,
/// under a binary collation; the raw key is never stored. Insert-only.
/// </summary>
internal sealed class ProductTaxClassificationAttemptRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public int StatusCode { get; set; }
    public string AnswerJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
