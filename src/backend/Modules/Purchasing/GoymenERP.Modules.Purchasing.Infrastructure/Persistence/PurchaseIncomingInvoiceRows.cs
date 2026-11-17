namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>Identity claimed at import, before matching. Official source identity never changes.</summary>
internal sealed class PurchaseIncomingInvoiceRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid SupplierPartyId { get; set; }
    public Guid Ettn { get; set; }
    public string OfficialNumber { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public long DocumentVersion { get; set; }
    public string DocumentSha256 { get; set; } = string.Empty;
}

/// <summary>Every transition is appended. A unique version is the optimistic concurrency fence.</summary>
internal sealed class PurchaseIncomingInvoiceRevisionRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InvoiceId { get; set; }
    public long Version { get; set; }
    public int Status { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

internal sealed class PurchaseIncomingInvoiceAttemptRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? SnapshotJson { get; set; }
    public string? RefusalJson { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>The immutable match ledger prevents a receipt share from being silently billed twice.</summary>
internal sealed class PurchaseInvoiceReceiptBindingRow : IPurchasingTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid InvoiceLineId { get; set; }
    public Guid AcceptanceId { get; set; }
    public Guid OrderId { get; set; }
    public Guid OrderLineId { get; set; }
    public decimal Quantity { get; set; }
}
