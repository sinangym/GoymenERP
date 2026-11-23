namespace GoymenERP.Modules.Sales.Application.Contracts;

public enum SalesShipmentClaimReadOutcomeV1 { Available = 1, NotInvoiced = 2, Unavailable = 3 }

/// <summary>Current persisted lifecycle evidence, not an external-document cancellation assertion.</summary>
public sealed record SalesShipmentInvoiceLifecycleV1(Guid InvoiceId, long Version, string State,
    DateTimeOffset? ReleasedAtUtc, string? CancelledBy, DateTimeOffset? CancelledAtUtc);

/// <summary>
/// The actual frozen whole-shipment claim of a normal finalized Sales invoice. No price, cost, tax or journal is returned.
/// FinalizedPayloadSha256 identifies the immutable owner event; it is not a substitute for its validated shipment metadata.
/// Commercial InvoicedQuantity is never a ratio for Inventory's physical kilograms or cost.
/// </summary>
public sealed record SalesFinalizedShipmentClaimV1(Guid CompanyId, Guid InvoiceId, Guid InvoiceLineId, int InvoiceLineNumber,
    Guid AdmissionId, long FinalizedVersion, SalesInvoiceShipmentSourceV1 ShipmentSource,
    Guid FinalizedEventId, string FinalizedPayloadSha256, DateTimeOffset FinalizedAtUtc,
    SalesShipmentInvoiceLifecycleV1 Lifecycle);

/// <summary>Immutable finalization and official cancellation evidence; never an active quantity or a new economic claim.</summary>
public sealed record SalesHistoricalFinalizedShipmentClaimV1(SalesFinalizedShipmentClaimV1 Finalization,
    Guid CancellationId, Guid CancellationEventId, string CancellationPayloadSha256,
    Guid CancelledBy, DateTimeOffset CancelledAtUtc, string Reason);

public sealed record SalesShipmentClaimReadV1(Guid ShipmentId, SalesShipmentClaimReadOutcomeV1 Outcome,
    SalesFinalizedShipmentClaimV1? Claim, IReadOnlyList<SalesShipmentInvoiceLifecycleV1> Lifecycles,
    string? SourceVersion, string? Reason = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SalesHistoricalFinalizedShipmentClaimV1>? HistoricalFinalizedClaims = null);

public sealed record SalesShipmentClaimBatchV1(Guid CompanyId, SalesShipmentClaimReadOutcomeV1 Outcome,
    IReadOnlyList<SalesShipmentClaimReadV1> Items, string? SourceVersion, string? Reason = null);

/// <summary>
/// Bounded internal owner evidence for an already-authorized consumer; no public route or new Sales permission.
/// The caller supplies its real fresh authority fence. It is measured around every database await and before release;
/// an inbox consumer uses its actual system-consumer fence, never an impersonated user. A missing fence is refused.
/// Empty requests return an authorized empty result; duplicate/empty identifiers and batches over128 are refused.
/// A foreign or unknown shipment is NotInvoiced in this company; corrupt, ambiguous or legacy proof is Unavailable.
/// The source version is a point-in-time read token, not a transaction lock or authorization for a later mutation.
/// </summary>
public interface ISalesFinalizedShipmentClaimReadPortV1
{
    Task<SalesShipmentClaimBatchV1> ReadAsync(Guid companyId, IReadOnlyCollection<Guid> shipmentIds,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
