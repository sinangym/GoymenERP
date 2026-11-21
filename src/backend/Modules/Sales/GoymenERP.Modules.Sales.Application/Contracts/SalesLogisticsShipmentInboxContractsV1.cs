using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.Modules.Sales.Domain.RiskExceptions;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// LOJ-D19 (SINAN-CEVAP 33, 2026-11-15) and SAT-D285: Logistics publishes "exception used" and "shipment cancelled" as versioned
// outbox events; Sales listens through the Work inbox (dedup per company, consumer and event id) and mirrors the fact on its own
// risk exception record. Sales never queries Logistics for it and never writes a Logistics table (SAT-D209). The event names and
// versions are reversible (LOJ-D19); the names below are the board's draft (PANO row 112, provider STK-S27) in the Work
// registration form "<name>.v1". SAT-S10: the host registers the consumer (AddSalesRiskExceptionUsageInbox) after the Logistics
// source (STK-S27); AddWorkInboxConsumer validates on start that the source is registered and declares both events. Logistics
// publishes only the recorded event today; the cancelled event has no producer until the shipment cancel is decided. The
// dispatcher itself stays off until Work:OutboxDispatcher:Enabled is set (a human gate).

/// <summary>The Sales side of the Logistics definite-shipment events (consumer name, source, event types, version).</summary>
public static class SalesLogisticsShipmentInboxV1
{
    /// <summary>The Sales consumer's registered name (dedup key part).</summary>
    public const string ConsumerName = "sales.risk-exception-usage";

    /// <summary>The Logistics outbox source name (must equal the name Logistics registers with AddWorkOutboxEventSource).</summary>
    public const string SourceName = "logistics";

    /// <summary>A definite shipment was recorded; when it used a single-shipment risk exception the payload names it.</summary>
    public const string RecordedEventType = "logistics.definite-shipment.recorded.v1";

    /// <summary>A recorded definite shipment was cancelled; when it had used an exception the payload names it.</summary>
    public const string CancelledEventType = "logistics.definite-shipment.cancelled.v1";

    /// <summary>The one version this consumer reads.</summary>
    public const int Version = 1;

    /// <summary>The consumer's subscriptions, for the composition root's AddWorkInboxConsumer call (SAT-S10).</summary>
    public static IReadOnlyList<WorkInboxSubscriptionV1> Subscriptions { get; } =
    [
        new(SourceName, RecordedEventType, Version),
        new(SourceName, CancelledEventType, Version),
    ];

    /// <summary>
    /// The audit row's permission field for a mirrored usage. No user acts and no permission is checked: the writer is the inbox
    /// consumer, so the field names the consumer, outside the grantable <c>sales.*</c> permission names (nothing is registered).
    /// </summary>
    public const string AuditPermissionCode = "inbox." + ConsumerName;

    /// <summary>The audit event of a mirrored "exception used by a shipment" (SAT-D285 (1)).</summary>
    public const string ConsumedAuditEventType = "sales.order.risk-exception.consumed.v1";

    /// <summary>The audit event of a mirrored "the shipment was cancelled, the exception dropped" (SAT-D285 (2)).</summary>
    public const string DroppedAuditEventType = "sales.order.risk-exception.dropped.v1";

    /// <summary>The usage a known event type mirrors; null for any other type.</summary>
    public static SalesRiskExceptionUsageKind? KindOf(string? eventType) => eventType switch
    {
        RecordedEventType => SalesRiskExceptionUsageKind.Consumed,
        CancelledEventType => SalesRiskExceptionUsageKind.Dropped,
        _ => null,
    };
}

/// <summary>
/// The payload fields Sales reads from both Logistics definite-shipment events (consumer-side decode, case-insensitive; any other
/// field, such as the board's Amount, is ignored and never stored). <see cref="RiskExceptionNo"/> is the per-order number the
/// shipment risk gate embedded (ISalesShipmentRiskExceptionReadPortV1 answers it); <see cref="RiskExceptionId"/> is accepted as
/// well. Both null means the shipment used no exception and the event changes nothing in Sales. The instant, correlation and
/// causation are the event envelope's (WorkInboxEventV1), not the payload's.
/// </summary>
public sealed record SalesLogisticsDefiniteShipmentEventV1(
    Guid CompanyId,
    Guid ShipmentId,
    Guid SalesOrderId,
    Guid LoadingInstructionId,
    int? RiskExceptionNo,
    Guid? RiskExceptionId);

/// <summary>One mirrored usage as the inbox consumer hands it to the store; the company is the ambient one.</summary>
public sealed record SalesRiskExceptionUsageV1(
    SalesRiskExceptionUsageKind Kind,
    Guid ShipmentId,
    Guid OrderId,
    Guid LoadingInstructionId,
    int? ExceptionNo,
    Guid? ExceptionId,
    string SourceName,
    Guid SourceEventId,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? CausationId);

/// <summary>What the store did with one mirrored usage; every refusal stores nothing.</summary>
public enum SalesRiskExceptionUsageOutcome
{
    /// <summary>Stored once, with its audit event.</summary>
    Recorded = 1,

    /// <summary>The same usage of the same shipment was stored before (at-least-once delivery); nothing changed.</summary>
    AlreadyRecorded = 2,

    /// <summary>The company has no such exception of that order.</summary>
    ExceptionNotFound = 3,

    /// <summary>The exception releases another loading instruction (SAT-D154).</summary>
    InstructionMismatch = 4,

    /// <summary>The exception is not approved, so the Logistics gate could not have used it.</summary>
    NotApproved = 5,

    /// <summary>The exception was already used (or dropped) by another shipment, or the shipment already names another exception.</summary>
    ConflictingShipment = 6,
}

/// <summary>
/// LOJ-D19: the append-only usage mirror of the single-shipment risk exceptions. Implemented by the Sales store over its own
/// tables; it writes under the ambient company only and never reads Logistics.
/// </summary>
public interface ISalesRiskExceptionUsageStore
{
    Task<SalesRiskExceptionUsageOutcome> RecordRiskExceptionUsageAsync(SalesRiskExceptionUsageV1 usage,
        CancellationToken cancellationToken = default);
}
