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
    Task<SalesOrderDraftRevisionsResponse> GetDraftRevisionsAsync(Guid draftId,
        CancellationToken cancellationToken = default);
}

// Constructed by the server, never bound from the request body.
public sealed record SalesCommandMetadata(string IdempotencyKey, string CorrelationId,
    string? CausationId, DateTimeOffset RequestedAtUtc);
