namespace GoymenERP.Modules.Sales.Application.Contracts;

public enum SalesOrderDraftPendingApprovalReadOutcomeV1
{
    Found,
    NotFound,
    Denied,
    Unavailable,
}

public sealed record SalesOrderDraftPendingApprovalReadContextV1(
    Guid CompanyId,
    string ActorId);

public sealed record SalesOrderDraftPendingApprovalItemV1(
    Guid DraftId,
    Guid CompanyId,
    string DocumentNo,
    string Version,
    string Revision,
    string SubmittedBy,
    DateTimeOffset SubmittedAtUtc,
    bool IsOwnDraft);

public sealed record SalesOrderDraftPendingApprovalPageV1(
    Guid CompanyId,
    IReadOnlyList<SalesOrderDraftPendingApprovalItemV1> Items,
    string? NextCursor);

public sealed record SalesOrderDraftPendingApprovalDetailV1(
    SalesOrderDraftPendingApprovalItemV1 Item);

public sealed record SalesOrderDraftPendingApprovalReadResultV1<T>(
    SalesOrderDraftPendingApprovalReadOutcomeV1 Outcome,
    T? Resource) where T : class;

/// <summary>
/// Sales-owned read side for order revisions that are truly waiting for an internal approval decision.
/// It uses the existing draft-read and order-approve authorities and preserves the Sales persona rule:
/// only a caller whose owner detail says <c>IsOwnDecisionRestricted</c> is excluded.
/// </summary>
public interface ISalesOrderDraftPendingApprovalReadServiceV1
{
    Task<SalesOrderDraftPendingApprovalReadResultV1<SalesOrderDraftPendingApprovalPageV1>> ListAsync(
        SalesOrderDraftPendingApprovalReadContextV1 context,
        string? cursor,
        CancellationToken cancellationToken);

    Task<SalesOrderDraftPendingApprovalReadResultV1<SalesOrderDraftPendingApprovalDetailV1>> GetAsync(
        SalesOrderDraftPendingApprovalReadContextV1 context,
        Guid draftId,
        CancellationToken cancellationToken);
}
