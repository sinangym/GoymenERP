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

        return Answer(draft);
    }

    /// <summary>
    /// The answer for an order found in the context company. K3 review A P3-1: when the customer rejected the last approved revision
    /// and the accepted revision the order would continue on names another customer or currency than the header (SAT-D115's fallback
    /// is unusable — the line port ships nothing then), no revision is a basis: the answer is <c>Unreadable</c>
    /// (<see cref="SalesSubcontractServiceOrderUnreadableReasonsV1.NoBasis"/>), never the rejected revision's lines.
    /// </summary>
    internal static SalesSubcontractServiceOrderAnswerV1 Answer(SalesOrderDraft draft)
    {
        if (!draft.IsSubcontractService)
        {
            return new SalesSubcontractServiceOrderAnswerV1(SalesSubcontractServiceOrderOutcomeV1.NotSubcontractService, null);
        }

        if (draft.CustomerAcceptedFallback is { } fallback && SalesOrderDraft.HeaderDiffersFrom(draft.Header, fallback))
        {
            return Unreadable(SalesSubcontractServiceOrderUnreadableReasonsV1.NoBasis);
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

    /// <summary>
    /// The order's facts. K3 review B P2-2 (SAT-D115): a customer-rejected current revision is <c>Rejected</c> (never "Pending"),
    /// and the lines are those of the revision the order continues on — the customer-accepted fallback under the same customer and
    /// currency (the shipping line port's rule), else the current revision.
    /// </summary>
    internal static SalesSubcontractServiceOrderV1 Order(SalesOrderDraft draft)
    {
        var acceptance = !draft.CustomerAcceptanceRequired
            ? SalesSubcontractCustomerAcceptanceStatesV1.NotRequired
            : draft.IsCustomerAccepted
                ? SalesSubcontractCustomerAcceptanceStatesV1.Accepted
                : draft.CurrentRevisionCustomerRejection is not null
                    ? SalesSubcontractCustomerAcceptanceStatesV1.Rejected
                    : SalesSubcontractCustomerAcceptanceStatesV1.Pending;
        var fallback = draft.CustomerAcceptedFallback;
        var onFallback = fallback is not null && !SalesOrderDraft.HeaderDiffersFrom(draft.Header, fallback);
        IReadOnlyList<SalesSubcontractServiceLineV1> lines = onFallback
            ? [.. fallback!.Lines.Select(archived => ArchivedLine(draft, archived))]
            : [.. draft.Lines.Select(Line)];
        return new(draft.CompanyId, draft.Id, draft.DocumentNo, draft.Revision, draft.Version, draft.ApprovalState.ToString(),
            draft.ApprovalState == InternalApprovalState.Approved, acceptance, draft.State.ToString(), draft.State == SalesDraftState.Draft,
            draft.Header.PartyId, draft.Header.CompanyAccountId, onFallback ? fallback!.Revision : draft.Revision,
            onFallback ? SalesSubcontractServiceOrderBasesV1.CustomerAcceptedRevision : SalesSubcontractServiceOrderBasesV1.CurrentRevision,
            lines);
    }

    private static SalesSubcontractServiceLineV1 Line(SalesOrderDraftLine line) =>
        new(line.Id, line.LineNo, line.Product.ProductId, line.Product.ProductVersion, line.Product.Code, line.Product.Name,
            line.QuantityText, line.Quantity.Value, line.OrderUnit.UnitId, line.OrderUnit.UnitCode, ItemType(line));

    /// <summary>
    /// An archived line of the accepted revision: its own identity, product version, unit and approved quantity; the display texts and
    /// the confirmed item type of the same line (no line is added or re-producted by a revision). Anything that does not fit is
    /// unreadable (fail-closed), never repaired.
    /// </summary>
    private static SalesSubcontractServiceLineV1 ArchivedLine(SalesOrderDraft draft, RevisionLineSnapshot archived)
    {
        var line = draft.Lines.SingleOrDefault(candidate => candidate.Id == archived.LineId);
        if (line is null || line.Product.ProductId != archived.ProductId || line.OrderUnit.UnitId != archived.OrderUnitId
            || !SalesLinePriceCalculator.TryParseExact(archived.QuantityText, out var quantity) || quantity <= 0m)
        {
            throw new InvalidOperationException("Kabul edilen revizyonun satırı siparişin satırıyla uyuşmuyor.");
        }

        return new(archived.LineId, archived.LineNo, archived.ProductId, archived.ProductVersion, line.Product.Code, line.Product.Name,
            archived.QuantityText, quantity, archived.OrderUnitId, line.OrderUnit.UnitCode, ItemType(line));
    }

    private static string ItemType(SalesOrderDraftLine line) =>
        line.ItemType ?? throw new InvalidOperationException("Fason hizmet satırının kalem tipi kayıtlı değil.");

    private static SalesSubcontractServiceOrderAnswerV1 Unreadable(string reason) =>
        new(SalesSubcontractServiceOrderOutcomeV1.Unreadable, null, reason);
}
