namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

/// <summary>
/// STK-S27 (L-4, SAT-D285 (1)): one consumed single-shipment risk exception. (Company, Sales order, exception number) is the
/// key, so an exception is consumed by one definite shipment only; a second use is the terminal
/// <c>logistics_shipment_risk_exception_used</c> refusal (or, in a race, a unique-key loss answered as concurrent change).
/// Append only; no amount is stored.
/// </summary>
internal sealed class ShipmentRiskExceptionUseRow : ILogisticsTenantRow
{
    public Guid CompanyId { get; set; }

    public Guid SalesOrderId { get; set; }

    public int ExceptionNo { get; set; }

    public Guid ShipmentId { get; set; }

    public Guid InstructionId { get; set; }

    public string RecordedBy { get; set; } = string.Empty;

    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>
/// STK-S27 (LOJ-D19, ADR-009): Logistics's transactional outbox. One integration event per row, written in the same save as
/// the state change that produced it and never changed afterwards. <see cref="Position"/> is the SQL Server rowversion the
/// Work dispatcher's cursor moves over (the registered source reads only below the lowest active rowversion, so a row
/// committed later never lands behind the cursor). (Company, event type, subject) is unique: one shipment, one event per type.
/// </summary>
internal sealed class LogisticsOutboxMessageRow : ILogisticsTenantRow
{
    public Guid EventId { get; set; }

    public Guid CompanyId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public int SchemaVersion { get; set; }

    public Guid SubjectId { get; set; }

    public string PayloadJson { get; set; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public string? CausationId { get; set; }

    public ulong Position { get; set; }
}
