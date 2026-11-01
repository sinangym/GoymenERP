namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>
/// SAT-D134: the item type (demir / hurda / hizmet) of a product in one company. Only the item type, the version and the
/// update facts move; the history is the audit trail.
/// </summary>
internal sealed class ProductItemTypeRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid ProductId { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
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
