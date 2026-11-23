namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

/// <summary>
/// STK-S24: the sales weighing-difference record of one dispatch note (at most one per note). Its evaluation columns are written
/// once; only the approval (status, version, approver, time) moves, forward only, from pending to approved.
/// </summary>
internal sealed class WeighingDifferenceRecordRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid RecordId { get; set; }
    public Guid InstructionId { get; set; }
    public Guid DispatchNoteId { get; set; }
    public decimal TheoreticalKg { get; set; }
    public decimal ScaleKg { get; set; }
    public decimal DifferenceKg { get; set; }
    public int BandLevel { get; set; }
    public decimal? WarningBandPercent { get; set; }
    public decimal? ApprovalBandPercent { get; set; }
    public decimal? BlockBandPercent { get; set; }
    public decimal TotalExcessKg { get; set; }
    public decimal? ApprovalRatePercent { get; set; }
    public int Decision { get; set; }
    public bool EnteredByComplete { get; set; }
    public int Status { get; set; }
    public int Version { get; set; }
    public string? KilogramAllocationJson { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public string RecordedBy { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
}

/// <summary>One product's result in the record, in note order; append only.</summary>
internal sealed class WeighingDifferenceProductRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid RecordId { get; set; }
    public int Ordinal { get; set; }
    public Guid ProductId { get; set; }
    public decimal NoteKg { get; set; }
    public decimal ScaleKg { get; set; }
    public decimal DifferenceKg { get; set; }
    public decimal RemainingPhysicalKg { get; set; }
    public decimal PhysicallyCoveredKg { get; set; }
    public decimal ExcessKg { get; set; }
    public int? TargetLineNo { get; set; }
}

/// <summary>SAT-D321 (2): one actor who entered the weighing, a shipment or the record itself; append only.</summary>
internal sealed class WeighingDifferenceEnteredByRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid RecordId { get; set; }
    public string ActorId { get; set; } = string.Empty;
}

/// <summary>ADR-011 attempt record of a weighing-difference command (the transfer outgoing-document shape).</summary>
internal sealed class WeighingDifferenceAttemptRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public Guid RecordId { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}
