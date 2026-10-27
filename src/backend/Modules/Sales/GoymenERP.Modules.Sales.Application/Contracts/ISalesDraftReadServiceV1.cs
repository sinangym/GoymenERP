namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// Sales-owned enriched read seam prepared for a later atomic central endpoint switch.
/// It is intentionally not registered by the Sales module until the central Cari adapter,
/// API contract and OpenAPI baseline can move together.
/// </summary>
public interface ISalesDraftReadServiceV1
{
    Task<SalesOrderDraftReadDetailViewV1> GetDraftDetailAsync(
        Guid draftId,
        CancellationToken cancellationToken = default);

    Task<SalesOrderDraftReadListResponseV1> GetDraftListAsync(
        SalesOrderDraftListQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record SalesOrderDraftReadSummaryViewV1(
    Guid Id, Guid CompanyId, string DocumentNo, string Kind, string State,
    string Version, string Revision, Guid PartyId, Guid CompanyAccountId,
    Guid CurrencyId, DateOnly BusinessDate, DateTimeOffset CreatedAtUtc, int LineCount,
    string ApprovalState, SalesCommercialStageV1? CommercialStage,
    IReadOnlyList<SalesOrderDraftProductSnapshotViewV1>? ProductSnapshots,
    SalesPartyDisplayV1 PartyDisplay);

/// <summary>
/// The enriched detail. MOD-08 (additive): <c>UserDisplays</c> names, once each, the people the detail's approval and
/// customer acceptance already identify by id, as the Identity owner shows them in the record's company. An id the owner
/// does not answer (or one that is not a user id) has no entry and keeps its raw id.
/// </summary>
public sealed record SalesOrderDraftReadDetailViewV1(
    SalesOrderDraftSummaryView Draft, DateOnly? RequestedDeliveryDate, string? Note,
    IReadOnlyList<SalesOrderDraftLineDetail> Lines,
    SalesDraftOwnerReferenceV1? DeliveryAddress,
    SalesDraftOwnerReferenceV1? Contact,
    SalesDraftOwnerReferenceV1? Responsible,
    SalesDraftOwnerReferenceV1? PaymentTerms,
    SalesDraftOwnerReferenceV1? SourceStrategy,
    SalesDraftOwnerReferenceV1? Package,
    SalesDraftApprovalV1 Approval,
    SalesDraftCustomerAcceptanceV1 CustomerAcceptance,
    bool IsOwnDraft,
    bool IsOwnDecisionRestricted,
    SalesPartyDisplayV1 PartyDisplay,
    IReadOnlyList<SalesUserDisplayV1> UserDisplays);

public sealed record SalesOrderDraftReadListResponseV1(
    IReadOnlyList<SalesOrderDraftReadSummaryViewV1> Items,
    string? NextCursor);
