namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// SAT-D149 (EK20): the item type (demir / hurda / hizmet) of a product on the SHARED product card — one value for every
/// company, keyed by the product alone (no company scope, like the global card it extends). Only the item type, the version
/// and the update facts move; <c>UpdatedInCompanyId</c> is the company in which the last change was made (the audit event of
/// each change is written in that company). The history is the audit trail.
/// </summary>
internal sealed class ProductCardItemTypeRow
{
    public Guid ProductId { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public Guid UpdatedInCompanyId { get; set; }
}

/// <summary>
/// The terminal answer of one item type command under one key (ADR-011): company, command and the SHA-256 of the key, under a
/// binary collation; the raw key is never stored. Insert-only.
/// </summary>
internal sealed class ProductItemTypeAttemptRow : IProductPriceListTenantRow
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
