using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.RiskExceptions;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>One current (not removed) priced line of an open order draft, with its commercial fields restored by the owner's mapping.</summary>
internal sealed record SalesWorkInboxDraftLineV1(
    Guid LineId,
    int LineNo,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    CommercialDraftFields Commercial);

/// <summary>An order draft whose lines are open (internal approval Preparing or Rejected, not cancelled), with its current priced lines.</summary>
internal sealed record SalesWorkInboxDraftV1(
    Guid Id,
    Guid CompanyId,
    string DocumentNo,
    bool IsSubcontractService,
    IReadOnlyList<SalesWorkInboxDraftLineV1> Lines);

/// <summary>A pending (Requested) single-shipment risk exception of an order that its approval accepts, with the order's number.</summary>
internal sealed record SalesWorkInboxRiskExceptionV1(
    Guid Id,
    Guid CompanyId,
    Guid OrderId,
    int ExceptionNo,
    string OrderDocumentNo,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc);

/// <summary>
/// W1-A: the Sales Work inbox sources' read-only queries over Sales's own tables. Every query filters
/// <c>CompanyId == companyId</c> explicitly beside the context's company query filter, tracks nothing and writes nothing.
/// </summary>
internal interface ISalesWorkInboxStoreV1
{
    /// <summary>Every open draft of the company (lines open, not cancelled) with its current priced lines; drafts without one are left out.</summary>
    Task<IReadOnlyList<SalesWorkInboxDraftV1>> ListOpenDraftsWithPricedLinesAsync(Guid companyId, CancellationToken cancellationToken);

    /// <summary>The oldest <paramref name="take"/> pending risk exceptions whose approval the order still accepts, oldest first, then id.</summary>
    Task<IReadOnlyList<SalesWorkInboxRiskExceptionV1>> ListPendingRiskExceptionsAsync(Guid companyId, int take,
        CancellationToken cancellationToken);
}

internal sealed class SalesWorkInboxStoreV1(SalesDbContext dbContext) : ISalesWorkInboxStoreV1
{
    private const int CancelledState = (int)SalesDraftState.Cancelled;
    private const int DraftState = (int)SalesDraftState.Draft;
    private const int Preparing = (int)InternalApprovalState.Preparing;
    private const int Rejected = (int)InternalApprovalState.Rejected;
    private const int Approved = (int)InternalApprovalState.Approved;
    private const int Requested = (int)SalesShipmentRiskExceptionState.Requested;

    public async Task<IReadOnlyList<SalesWorkInboxDraftV1>> ListOpenDraftsWithPricedLinesAsync(Guid companyId,
        CancellationToken cancellationToken)
    {
        // The approval commands accept a draft that is not cancelled and whose lines are open (LinesAreOpen: Preparing or
        // Rejected). Only priced lines can be below the floor or need the discount check; a removed line is not a current line.
        var rows = await (
                from draft in dbContext.Drafts.AsNoTracking()
                where draft.CompanyId == companyId && draft.State != CancelledState
                    && (draft.ApprovalState == Preparing || draft.ApprovalState == Rejected)
                join line in dbContext.DraftLines.AsNoTracking()
                    on new { draft.CompanyId, DraftId = draft.Id } equals new { line.CompanyId, line.DraftId }
                where line.CompanyId == companyId && line.RemovedInRevision == null && line.UnitPrice != null
                select new
                {
                    draft.Id,
                    draft.CompanyId,
                    draft.DocumentNo,
                    draft.IsSubcontractService,
                    line.LineId,
                    line.LineNo,
                    line.ProductId,
                    line.ProductCode,
                    line.ProductName,
                    line.UnitPrice,
                    line.PriceUnitId,
                    line.DiscountPercent,
                    line.PricingJson,
                })
            .ToListAsync(cancellationToken);

        return [.. rows
            .GroupBy(row => row.Id)
            .Select(group =>
            {
                var first = group.First();
                return new SalesWorkInboxDraftV1(first.Id, first.CompanyId, first.DocumentNo, first.IsSubcontractService,
                    [.. group
                        .OrderBy(row => row.LineNo)
                        .Select(row => new SalesWorkInboxDraftLineV1(row.LineId, row.LineNo, row.ProductId, row.ProductCode,
                            row.ProductName,
                            SalesLinePricingJson.Restore(row.UnitPrice, row.PriceUnitId, row.DiscountPercent, row.PricingJson)))]);
            })];
    }

    public async Task<IReadOnlyList<SalesWorkInboxRiskExceptionV1>> ListPendingRiskExceptionsAsync(Guid companyId, int take,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);

        // The approval accepts a Requested exception of an order that is still a draft (not cancelled or closed) and is approved
        // or has an approved revision (RequireEligible: Approved, or ArchivedRevisions > 0 — the stored Revision equals that count).
        return await (
                from exception in dbContext.ShipmentRiskExceptions.AsNoTracking()
                where exception.CompanyId == companyId && exception.State == Requested
                join draft in dbContext.Drafts.AsNoTracking()
                    on new { exception.CompanyId, Id = exception.OrderId } equals new { draft.CompanyId, draft.Id }
                where draft.CompanyId == companyId && draft.State == DraftState
                    && (draft.ApprovalState == Approved || draft.Revision > 0)
                orderby exception.RequestedAtUtc, exception.Id
                select new SalesWorkInboxRiskExceptionV1(exception.Id, exception.CompanyId, exception.OrderId,
                    exception.ExceptionNo, draft.DocumentNo, exception.RequestedBy, exception.RequestedAtUtc))
            .Take(take)
            .ToListAsync(cancellationToken);
    }
}
