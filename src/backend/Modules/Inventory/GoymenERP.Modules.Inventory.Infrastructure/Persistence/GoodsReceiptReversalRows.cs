namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>Append-only physical undo of one original acceptance. Original receipt/conversion rows remain unchanged.</summary>
internal sealed class GoodsReceiptReversalRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public Guid AcceptanceId { get; set; }
    public Guid LabelId { get; set; }
    public Guid OriginalMovementId { get; set; }
    public Guid MovementId { get; set; }
    public int Kind { get; set; }
    public decimal QuantityKg { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RecordedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>
/// One exact undo share of one immutable original conversion. Reopened + overflow released + unheld = undone.
/// Unheld means the original physical hold or open demand no longer exists; it is never revived.
/// </summary>
internal sealed class FutureSupplyConversionReversalRow : IInventoryTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid ReversalId { get; set; }
    public Guid ConversionId { get; set; }
    public decimal UndoneQuantity { get; set; }
    public decimal UndoneKg { get; set; }
    public decimal ReopenedQuantity { get; set; }
    public decimal OverflowReleasedQuantity { get; set; }
    public decimal UnheldQuantity { get; set; }
}
