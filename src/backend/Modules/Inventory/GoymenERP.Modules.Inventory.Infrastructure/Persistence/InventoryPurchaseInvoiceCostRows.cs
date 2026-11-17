namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>Append-only inbox receipt and complete immutable source/result; success is committed with every owner effect.</summary>
internal sealed class PurchaseInvoiceCostApplicationRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid SourceEventId { get; set; }
    public Guid InvoiceId { get; set; }
    public long InvoiceVersion { get; set; }
    public Guid AdmissionId { get; set; }
    public string SourceSha256 { get; set; } = string.Empty;
    public string SourceJson { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>One invoiced share of an actual receipt. The acceptance index also fences concurrent partial invoices.</summary>
internal sealed class PurchaseInvoiceReceiptCostEffectRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid SourceEventId { get; set; }
    public Guid AcceptanceId { get; set; }
    public Guid InvoiceLineId { get; set; }
    public Guid MovementId { get; set; }
    public int MovementLineNumber { get; set; }
    public decimal MatchedQuantity { get; set; }
    public decimal AcceptedQuantity { get; set; }
    public string ResultJson { get; set; } = string.Empty;
}
