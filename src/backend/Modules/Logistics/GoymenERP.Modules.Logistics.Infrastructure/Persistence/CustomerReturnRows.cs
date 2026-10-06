namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

internal sealed class CustomerReturnRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid CustomerReturnId { get; set; }
    public Guid LoadingInstructionId { get; set; }
    public Guid LoadingInstructionLineId { get; set; }
    public Guid SourceDeliveryId { get; set; }
    public Guid SourceShipmentId { get; set; }
    public Guid SourceOrderId { get; set; }
    public Guid SourceOrderLineId { get; set; }
    public Guid UnitId { get; set; }
    public int QuantityUnit { get; set; }
    public decimal Quantity { get; set; }
    public decimal SourceDeliveredQuantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public int Status { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public string? DecisionNote { get; set; }
    public Guid? InventoryMovementId { get; set; }
    public Guid? OriginalLabelId { get; set; }
    public Guid? OriginalShipmentMovementId { get; set; }
}

internal sealed class CustomerReturnAttemptRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public int Outcome { get; set; }
    public Guid? CustomerReturnId { get; set; }
    public string? ResultJson { get; set; }
    public string? RefusalJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}
