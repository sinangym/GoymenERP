namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

internal interface ILogisticsTenantRow
{
    Guid CompanyId { get; }
}

internal sealed class OrganizationCompanyReference
{
    public Guid Id { get; set; }
}

/// <summary>The instruction header; only Status, Version and UpdatedAtUtc move after the first commit.</summary>
internal sealed class LoadingInstructionRow : ILogisticsTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CustomerPartyId { get; set; }
    public Guid CompanyAccountId { get; set; }
    public int Status { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public DateOnly EffectiveOn { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>One line with its four shares as stored totals (the shares table is the event list they sum from) and the Sales binding (D4).</summary>
internal sealed class LoadingInstructionLineRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InstructionId { get; set; }
    public int LineNo { get; set; }
    public Guid LineId { get; set; }
    public Guid SourceOrderId { get; set; }
    public Guid SourceOrderLineId { get; set; }
    public int SourceOrderRevision { get; set; }
    public Guid ProductId { get; set; }
    public Guid UnitId { get; set; }
    public bool UnitIsKilogram { get; set; }
    public Guid? AllocationId { get; set; }
    public decimal Instructed { get; set; }
    public decimal Loaded { get; set; }
    public decimal Shipped { get; set; }
    public decimal Delivered { get; set; }
    public bool IsBlocked { get; set; }
}

/// <summary>One recorded share (load / shipment / delivery) in recording order; append-only.</summary>
internal sealed class LoadingShareRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InstructionId { get; set; }
    public int ShareNo { get; set; }
    public Guid ShareId { get; set; }
    public Guid LineId { get; set; }
    public int Kind { get; set; }
    public decimal Quantity { get; set; }
    public int? Measurement { get; set; }
    public Guid? SourceShipmentId { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>ADR-011 attempt record (the Sales shape): the key hash, the content hash, the actor, the outcome and the result.</summary>
internal sealed class LoadingAttemptRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public int Outcome { get; set; }
    public Guid? InstructionId { get; set; }
    public string? RefusalJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>
/// AT-053: the instruction's manual weighbridge record, one per instruction (the key), append-only. The two weighings as
/// entered, the direction and the backend's calculation (net, the loaded kilograms when weighed, the signed difference),
/// the share count it was recorded after (so the rebuild replays it at that point) and the operator.
/// </summary>
internal sealed class VehicleWeighingRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InstructionId { get; set; }
    public Guid WeighingId { get; set; }
    public int Direction { get; set; }
    public decimal FirstWeighingKg { get; set; }
    public decimal SecondWeighingKg { get; set; }
    public decimal NetKg { get; set; }
    public decimal LoadedKgAtWeighing { get; set; }
    public decimal DifferenceKg { get; set; }
    public int SharesBefore { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}
