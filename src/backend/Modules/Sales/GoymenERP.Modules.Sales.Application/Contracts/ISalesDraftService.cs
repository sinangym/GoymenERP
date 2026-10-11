namespace GoymenERP.Modules.Sales.Application.Contracts;

// Implementations obtain company/actor/session from authenticated server scope.
// Every call, including replay, re-reads current authorization. No runtime here.
public interface ISalesDraftService
{
    Task<SalesOrderDraftResponse> CreateDraftAsync(CreateSalesOrderDraftRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<SalesOrderDraftDetailView> GetDraftDetailAsync(Guid draftId,
        CancellationToken cancellationToken = default);
    Task<SalesOrderDraftListResponse> GetDraftListAsync(SalesOrderDraftListQuery query,
        CancellationToken cancellationToken = default);

    // SALES-A1 — the internal approval axis (DEC-SAL-001/002; Business 5737989917). Each command
    // names the expected version, is keyed by its own Idempotency-Key (ADR-011) and answers the
    // draft after the transition — 200, with the replay headers when the key had answered before.
    // Submit needs the draft's create permission; approve and reject need `sales.orders.approve`.

    Task<SalesOrderDraftResponse> SubmitDraftAsync(Guid draftId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<SalesOrderDraftResponse> ApproveDraftAsync(Guid draftId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<SalesOrderDraftResponse> RecordCustomerAcceptanceAsync(Guid draftId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    /// <summary>D2 (Sinan 5749046401): the preparer withdraws their OWN not-yet-approved draft — the create name plus identity (actor == creator); anyone else's is refused; approved is a 409.</summary>
    Task<SalesOrderDraftResponse> WithdrawDraftAsync(Guid draftId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    Task<SalesOrderDraftResponse> RejectDraftAsync(Guid draftId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    // K3 Head B (Sinan 5748258299) — the commercial revision under `sales.orders.price.write`: every line's
    // price, price unit and discount replaced; an APPROVED draft archives its approved revision first and
    // advances to the next one, back in preparation; and the read of the archived revisions (`sales.drafts.read`).
    Task<SalesOrderDraftResponse> ReviseCommercialAsync(Guid draftId, SalesDraftCommercialRevisionRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<SalesOrderDraftResponse> ApprovePriceFloorExceptionsAsync(Guid draftId,
        SalesDraftPriceFloorExceptionApprovalRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default);
    /// <summary>AT-018 / AT-021: every line's reference, adjustments and net (price read).</summary>
    Task<SalesOrderDraftLinePricingResponse> GetDraftLinePricingAsync(Guid draftId, CancellationToken cancellationToken = default);

    /// <summary>AT-018: the Product owner's list prices for one line (price write).</summary>
    Task<SalesOrderDraftPriceReferencesResponse> GetLinePriceReferencesAsync(Guid draftId, Guid lineId,
        CancellationToken cancellationToken = default);

    Task<SalesOrderDraftRevisionsResponse> GetDraftRevisionsAsync(Guid draftId,
        CancellationToken cancellationToken = default);

    // DEC-SAL-002 / AT-023 — the quantity revision under `sales.orders.quantity.write`: every line's ordered quantity
    // replaced with a reason; an APPROVED order archives its approved revision first (with that reason) and advances to the
    // next one, back in preparation, for re-approval. Refused while an owner links a record to the order (fail-closed).
    Task<SalesOrderDraftResponse> ReviseQuantityAsync(Guid draftId, SalesDraftQuantityRevisionRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);

    // DEC-SAL-003 — the delivery-schedule update under `sales.orders.delivery-schedule.write` (dates only, no
    // re-approval, audited), and the read of the schedule history (`sales.drafts.read`).
    Task<SalesOrderDraftResponse> RescheduleDeliveryAsync(Guid draftId, SalesDraftDeliveryScheduleRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<SalesOrderDraftDeliverySchedulesResponse> GetDraftDeliverySchedulesAsync(Guid draftId,
        CancellationToken cancellationToken = default);

    // AT-024 — per line the delivered and remaining quantity, from the delivery owner's recorded deliveries (Logistics,
    // through ISalesOrderDeliveryReadPortV1), under the draft read; read-only, nothing is written.
    Task<SalesOrderDraftDeliveryProgressResponse> GetDraftDeliveryProgressAsync(Guid draftId,
        CancellationToken cancellationToken = default);

    // DEC-SAL-005 — per line the current loading-instruction facts from Logistics, under the draft read. The read creates
    // nothing and adds no Logistics or Work authority; the loading owner remains the only source of these quantities.
    Task<SalesOrderDraftLoadingProgressResponse> GetDraftLoadingProgressAsync(Guid draftId,
        CancellationToken cancellationToken = default);

    // DEC-SAL-005 / AT-007 — the impact preview and the cancellation of an approved order under `sales.orders.cancel`
    // (owners read through their ports; fail-closed: any recorded effect or an unreadable owner refuses), and the read of the
    // recorded cancellation (`sales.drafts.read`).
    Task<SalesOrderDraftCancellationImpactResponse> GetCancellationImpactAsync(Guid draftId,
        CancellationToken cancellationToken = default);
    Task<SalesOrderDraftResponse> CancelDraftAsync(Guid draftId, SalesDraftCancelRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<SalesOrderDraftCancellationResponse> GetDraftCancellationAsync(Guid draftId,
        CancellationToken cancellationToken = default);

    // Partial delivery (user decision 2026-09-30) — the undelivered remainder of a partly delivered line is cancelled or
    // its quantity revised: requested under `sales.orders.quantity.write`, approved or rejected under `sales.orders.approve`;
    // the floor (loaded ≥ shipped ≥ delivered) is read from the delivery owner. The read of the lines' remainder facts and the
    // recorded changes is under `sales.drafts.read`.
    Task<SalesOrderDraftResponse> RequestRemainderChangeAsync(Guid draftId, SalesDraftRemainderChangeRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<SalesOrderDraftResponse> ApproveRemainderChangeAsync(Guid draftId, int changeNo, SalesDraftRemainderDecisionRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<SalesOrderDraftResponse> RejectRemainderChangeAsync(Guid draftId, int changeNo, SalesDraftRemainderDecisionRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default);
    Task<SalesOrderDraftRemainderChangesResponse> GetRemainderChangesAsync(Guid draftId,
        CancellationToken cancellationToken = default);
}

// Constructed by the server, never bound from the request body.
public sealed record SalesCommandMetadata(string IdempotencyKey, string CorrelationId,
    string? CausationId, DateTimeOffset RequestedAtUtc);
