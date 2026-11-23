namespace GoymenERP.Modules.Product.Infrastructure.PriceLists;

/// <summary>Append-only explicit membership table. An import only adds its named products; absent products are carried forward.</summary>
internal sealed class ProductPaintMembershipVersionRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? FileSha256 { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
internal sealed class ProductPaintMembershipLineRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid ProductId { get; set; }
    public long MembershipVersion { get; set; }
    public long RecordedProductVersion { get; set; }
}
internal sealed class ProductPaintMembershipAttemptRow : IProductPriceListTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
