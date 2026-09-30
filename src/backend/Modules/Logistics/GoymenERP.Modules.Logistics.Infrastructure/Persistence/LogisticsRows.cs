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
/// AT-055: a dispatch note (irsaliye) of an instruction. <see cref="NoteNo"/> is its order among the instruction's notes (not a
/// document number); <see cref="SharesBefore"/> is the share count it was made after, so the rebuild replays it at that point.
/// Status 1 = Draft, 2 = Approved, 3 = Cancelled (user decision 2026-09-30). The row is never deleted: approval fills the
/// approval columns and the system number (<see cref="SeriesCode"/> + <see cref="SequenceNo"/>), cancellation the cancellation
/// columns, and the e-İrsaliye number its own columns — each once, each with the instruction's dispatch-note step
/// (<c>…Step</c>) the rebuild replays it at. SQL Server holds the shape of each group as a check constraint.
/// </summary>
internal sealed class DispatchNoteRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InstructionId { get; set; }
    public Guid DispatchNoteId { get; set; }
    public int NoteNo { get; set; }
    public int Status { get; set; }
    public int SharesBefore { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public int CreatedStep { get; set; }
    public string? SeriesCode { get; set; }
    public long? SequenceNo { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public int? ApprovedStep { get; set; }
    public string? CancelledBy { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public string? CancelReason { get; set; }
    public int? CancelledStep { get; set; }
    public string? EDispatchNumber { get; set; }
    public string? EDispatchNumberSetBy { get; set; }
    public DateTimeOffset? EDispatchNumberSetAtUtc { get; set; }
    public int? EDispatchNumberStep { get; set; }
}

/// <summary>
/// AT-055 (user decision 2026-09-30): a company's dispatch-note number series — the last sequence number given. Approval
/// increments it in the note's own save, with <see cref="LastSequenceNo"/> as the optimistic concurrency token: two approvals
/// racing for the same number cannot both commit (the loser changes nothing and is told to retry), so the sequence is
/// gap-free and never duplicated; the notes' filtered unique key (company, series, sequence) holds it too.
/// </summary>
internal sealed class DispatchNoteSeriesRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public string SeriesCode { get; set; } = string.Empty;
    public long LastSequenceNo { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>
/// AT-055: one line of a dispatch note — one definite shipment of the instruction and its shipped quantity, with the
/// instruction line's Sales binding, product and unit copied for the Sales order read. <see cref="Released"/> turns true (once)
/// when the note is cancelled; the key (company, instruction, shipment) is UNIQUE over the lines NOT released: a shipment is on
/// at most one non-cancelled dispatch note, in SQL Server too.
/// </summary>
internal sealed class DispatchNoteLineRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid InstructionId { get; set; }
    public Guid DispatchNoteId { get; set; }
    public int LineNo { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid InstructionLineId { get; set; }
    public Guid SourceOrderId { get; set; }
    public Guid SourceOrderLineId { get; set; }
    public Guid ProductId { get; set; }
    public Guid UnitId { get; set; }
    public decimal Quantity { get; set; }
    public bool Released { get; set; }
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
