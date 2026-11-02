using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// S-3 / SAT-D175…D176 (EK25): the Sales-owned implementation of <see cref="ISalesSubcontractServiceOrderReadPortV1"/> over the
/// company-filtered draft store. The asked company must be the server's resolved company (a copy of the context, never trusted
/// as an input): otherwise — and on any read fault — the answer is <see cref="SalesSubcontractServiceOrderOutcomeV1.Unreadable"/>
/// (fail-closed). Another company's order is invisible under the tenant filter (<c>NotFound</c>). It reads under the caller's
/// server context — the consumer measures its own authority before asking — and creates nothing.
/// </summary>
public sealed class SalesSubcontractServiceOrderReadPort(ISalesOrderDraftStore store, ICompanyContext companyContext)
    : ISalesSubcontractServiceOrderReadPortV1
{
    public async Task<SalesSubcontractServiceOrderAnswerV1> GetAsync(Guid companyId, Guid orderId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || !companyContext.IsResolved || companyContext.CompanyId != companyId)
        {
            return Unreadable(SalesSubcontractServiceOrderUnreadableReasonsV1.CompanyMismatch);
        }

        if (orderId == Guid.Empty)
        {
            return new SalesSubcontractServiceOrderAnswerV1(SalesSubcontractServiceOrderOutcomeV1.NotFound, null);
        }

        SalesOrderDraft? draft;
        try
        {
            draft = await store.FindDraftAsync(orderId, cancellationToken);
        }
        catch (Exception exception) when (exception is SalesDraftStoreException or InvalidOperationException)
        {
            return Unreadable(SalesSubcontractServiceOrderUnreadableReasonsV1.StoreUnavailable);
        }

        if (draft is null)
        {
            return new SalesSubcontractServiceOrderAnswerV1(SalesSubcontractServiceOrderOutcomeV1.NotFound, null);
        }

        if (draft.CompanyId != companyId)
        {
            return Unreadable(SalesSubcontractServiceOrderUnreadableReasonsV1.CompanyMismatch);
        }

        if (!draft.IsSubcontractService)
        {
            return new SalesSubcontractServiceOrderAnswerV1(SalesSubcontractServiceOrderOutcomeV1.NotSubcontractService, null);
        }

        try
        {
            return new SalesSubcontractServiceOrderAnswerV1(SalesSubcontractServiceOrderOutcomeV1.Found, Order(draft));
        }
        catch (InvalidOperationException)
        {
            // A stored service line without its confirmed item type is never answered as a service (fail-closed).
            return Unreadable(SalesSubcontractServiceOrderUnreadableReasonsV1.StoreUnavailable);
        }
    }

    internal static SalesSubcontractServiceOrderV1 Order(SalesOrderDraft draft) =>
        new(draft.CompanyId, draft.Id, draft.DocumentNo, draft.Revision, draft.Version, draft.ApprovalState.ToString(),
            draft.ApprovalState == InternalApprovalState.Approved,
            !draft.CustomerAcceptanceRequired
                ? SalesSubcontractCustomerAcceptanceStatesV1.NotRequired
                : draft.IsCustomerAccepted
                    ? SalesSubcontractCustomerAcceptanceStatesV1.Accepted
                    : SalesSubcontractCustomerAcceptanceStatesV1.Pending,
            draft.State.ToString(), draft.State == SalesDraftState.Draft, draft.Header.PartyId, draft.Header.CompanyAccountId,
            [.. draft.Lines.Select(line => new SalesSubcontractServiceLineV1(line.Id, line.LineNo, line.Product.ProductId,
                line.Product.ProductVersion, line.Product.Code, line.Product.Name, line.QuantityText, line.Quantity.Value,
                line.OrderUnit.UnitId, line.OrderUnit.UnitCode,
                line.ItemType ?? throw new InvalidOperationException("Fason hizmet satırının kalem tipi kayıtlı değil.")))]);

    private static SalesSubcontractServiceOrderAnswerV1 Unreadable(string reason) =>
        new(SalesSubcontractServiceOrderOutcomeV1.Unreadable, null, reason);
}
