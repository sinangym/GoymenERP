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
}

// Constructed by the server, never bound from the request body.
public sealed record SalesCommandMetadata(string IdempotencyKey, string CorrelationId,
    string? CausationId, DateTimeOffset RequestedAtUtc);
