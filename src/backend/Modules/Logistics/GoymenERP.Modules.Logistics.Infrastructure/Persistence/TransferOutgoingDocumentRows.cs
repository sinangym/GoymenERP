namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

internal sealed class TransferOutgoingDocumentRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid TransferId { get; set; }
    public int Status { get; set; }
    public int Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public string? EDispatchNumber { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? EffectiveBy { get; set; }
    public DateTimeOffset? EffectiveAtUtc { get; set; }
    public string? CancelledBy { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
}

internal sealed class TransferOutgoingDocumentLineRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public int Ordinal { get; set; }
    public Guid TransferLineId { get; set; }
}

internal sealed class TransferOutgoingDocumentAttemptRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}
