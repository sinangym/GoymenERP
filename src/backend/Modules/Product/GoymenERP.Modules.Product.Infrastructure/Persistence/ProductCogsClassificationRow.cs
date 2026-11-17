namespace GoymenERP.Modules.Product.Infrastructure.Persistence;

/// <summary>SAT-D422: append-only global card extension, with the actual origin company and global authority evidence.</summary>
public sealed class ProductCogsClassificationRow
{
    public Guid ProductId { get; set; }
    public long Version { get; set; }
    public long RecordedProductVersion { get; set; }
    public int Kind { get; set; }
    public Guid OriginCompanyId { get; set; }
    public Guid ActorId { get; set; }
    public string AuthorityRevision { get; set; } = null!;
    public string Reason { get; set; } = null!;
    public string CorrelationId { get; set; } = null!;
    public DateTimeOffset RecordedAtUtc { get; set; }
}
