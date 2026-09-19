using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// SALES-A1: the Sales-owned implementation of <see cref="ISalesOrderLineReferencePortV1"/> over the
/// company-filtered draft store (Business #66 5737989917 = B). It reads under the caller's server
/// context — the consumer (INV-AL-1, LOG-4) measures its own authority before asking — and creates
/// nothing. An approved draft's line answers its stored identity facts; any other approval state is
/// <see cref="SalesOrderLineReferenceOutcomeV1.NotApproved"/> with the state's name; another
/// company's draft is invisible under the tenant filter and answers
/// <see cref="SalesOrderLineReferenceOutcomeV1.NotFound"/>, never 403.
/// </summary>
public sealed class SalesOrderLineReferencePort(ISalesOrderDraftStore store) : ISalesOrderLineReferencePortV1
{
    public async Task<SalesOrderLineReferenceAnswerV1> GetApprovedLineAsync(Guid orderId, Guid lineId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (orderId == Guid.Empty || lineId == Guid.Empty)
        {
            return NotFound;
        }

        var draft = await store.FindDraftAsync(orderId, cancellationToken);
        var line = draft?.Lines.SingleOrDefault(candidate => candidate.Id == lineId);
        if (draft is null || line is null)
        {
            return NotFound;
        }

        if (draft.ApprovalState != InternalApprovalState.Approved)
        {
            return new SalesOrderLineReferenceAnswerV1(SalesOrderLineReferenceOutcomeV1.NotApproved, null, draft.ApprovalState.ToString());
        }

        return new SalesOrderLineReferenceAnswerV1(SalesOrderLineReferenceOutcomeV1.Approved, new SalesOrderLineReferenceV1(
            draft.CompanyId, draft.Id, line.Id, line.LineNo, draft.Header.PartyId, draft.Header.CompanyAccountId,
            line.Product.ProductId, line.Product.ProductVersion, line.OrderUnit.UnitId, line.QuantityText, draft.Revision, draft.Version), null);
    }

    private static readonly SalesOrderLineReferenceAnswerV1 NotFound = new(SalesOrderLineReferenceOutcomeV1.NotFound, null, null);
}
