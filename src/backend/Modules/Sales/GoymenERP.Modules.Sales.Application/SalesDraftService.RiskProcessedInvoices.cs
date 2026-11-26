using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesDraftService
{
    private sealed record RiskInvoiceMembership(Guid ShipmentId, Guid InvoiceId, bool Cancelled);
    private sealed record RiskInvoiceScope(IReadOnlyList<RiskInvoiceMembership> Memberships, string Fingerprint, string? Reason);

    private async Task<(PartyRiskInputs Party, SalesPartyRiskReadAnswerV2? Finance)> PartyRiskSnapshotAsync(
        Scope scope, string? permission, SalesOrderDraft draft, RiskInvoiceScope? invoices, CancellationToken token)
    {
        var owner = partyRiskV2 ?? partyRisk as ISalesPartyRiskReadPortV2;
        if (owner is null) return (await PartyRiskAsync(scope, permission, draft, token), null);
        var query = new SalesPartyRiskReadQueryV2(draft.Header.PartyId, draft.Header.CompanyAccountId,
            invoices?.Memberships.Select(x => x.InvoiceId).Distinct().ToArray() ?? []);
        try
        {
            var answer = await GuardedAsync(scope, permission, () => owner.ReadPartyRiskAsync(query, token), token);
            if (answer?.CompanyId != scope.CompanyId || !SalesPartyRiskReadAnswerV2.IsProcessedSetWellFormed(answer, query))
                return (PartyRiskUnavailable(SalesDraftRiskReasonCodesV1.PartyRiskAnswerInvalid), null);
            return (PartyRiskFromAnswer(scope, draft, answer.ToV1()), answer);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            return (PartyRiskUnavailable(SalesDraftRiskReasonCodesV1.PartyRiskProviderUnavailable), null);
        }
    }

    private async Task<RiskInvoiceScope> RiskInvoiceScopeAsync(Scope scope, string? permission, SalesOrderDraft draft,
        IReadOnlyList<SalesInvoiceableShipmentV1> shares, CancellationToken token)
    {
        static RiskInvoiceScope Failed(string reason) => new([], string.Empty, reason);
        if (invoiceRecords is null) return Failed(SalesDraftRiskReasonCodesV1.InvoiceRecordUnavailable);
        if (shares.Count == 0) return new([], string.Empty, null);
        try
        {
            var shareIds = shares.Select(x => x.ShipmentId).ToArray();
            var active = await GuardedAsync(scope, permission,
                () => invoiceRecords.FindFinalizedInvoiceShipmentsAsync(shareIds, token), token);
            var orders = shares.Select(x => x.SalesOrderId).Distinct().ToArray();
            if (orders.Length > MaximumRiskOrders) return Failed(SalesDraftRiskReasonCodesV1.ShipmentReadBoundExceeded);
            var candidates = new Dictionary<Guid, SalesInvoice>();
            foreach (var orderId in orders)
            {
                // This existing owner query retains released invoice/order links, including official cancellations.
                var page = await GuardedAsync(scope, permission,
                    () => invoiceRecords.ListInvoicesOfOrderAsync(orderId, 100, token), token);
                if (page.HasMore) return Failed(SalesDraftRiskReasonCodesV1.ShipmentReadBoundExceeded);
                foreach (var invoice in page.Items)
                {
                    if (invoice.CompanyId != scope.CompanyId || invoice.Header.PartyId != draft.Header.PartyId
                        || invoice.Header.CompanyAccountId != draft.Header.CompanyAccountId)
                        return Failed(SalesDraftRiskReasonCodesV1.InvoiceRecordUnavailable);
                    if (invoice.State is not (SalesInvoiceState.Finalized or SalesInvoiceState.OfficiallyCancelled)) continue;
                    if (candidates.TryGetValue(invoice.Id, out var prior) && (prior.Version != invoice.Version || prior.State != invoice.State))
                        return Failed(SalesDraftRiskReasonCodesV1.InvoiceRecordUnavailable);
                    candidates[invoice.Id] = invoice;
                }
            }
            if (candidates.Count > SalesPartyRiskReadQueryV2.MaximumSalesInvoiceIds)
                return Failed(SalesDraftRiskReasonCodesV1.ShipmentReadBoundExceeded);
            var memberships = new List<RiskInvoiceMembership>();
            var fingerprints = new List<string>();
            foreach (var listed in candidates.Values.OrderBy(x => x.Id))
            {
                // The detail query also loads the retained finalization ledger; the list intentionally does not.
                var invoice = await GuardedAsync(scope, permission, () => invoiceRecords.FindInvoiceAsync(listed.Id, token), token);
                if (invoice is null || invoice.CompanyId != scope.CompanyId || invoice.Version != listed.Version
                    || invoice.State != listed.State || invoice.Finalization is null
                    || invoice.Header.PartyId != draft.Header.PartyId || invoice.Header.CompanyAccountId != draft.Header.CompanyAccountId)
                    return Failed(SalesDraftRiskReasonCodesV1.InvoiceRecordUnavailable);
                fingerprints.Add($"{invoice.Id:N}:{invoice.Version}:{(int)invoice.State}:{invoice.Finalization.FinalizedVersion}");
                foreach (var line in invoice.Lines)
                {
                    var claim = line.Share;
                    var posted = shares.SingleOrDefault(x => x.ShipmentId == claim.ShipmentId);
                    if (posted is null) continue;
                    if (claim.DispatchNoteId != posted.DispatchNoteId || claim.NoteLineNo != posted.NoteLineNo
                        || claim.SalesOrderId != posted.SalesOrderId || claim.SalesOrderLineId != posted.SalesOrderLineId
                        || claim.ProductId != posted.ProductId || claim.UnitId != posted.UnitId || claim.Quantity != posted.Quantity)
                        return Failed(SalesDraftRiskReasonCodesV1.InvoiceRecordUnavailable);
                    memberships.Add(new(posted.ShipmentId, invoice.Id, invoice.State == SalesInvoiceState.OfficiallyCancelled));
                    fingerprints.Add($"{invoice.Id:N}:{posted.ShipmentId:N}");
                }
            }
            if (!active.SetEquals(memberships.Where(x => !x.Cancelled).Select(x => x.ShipmentId)))
                return Failed(SalesDraftRiskReasonCodesV1.FinanceProcessedSetUnavailable);
            if (memberships.GroupBy(x => (x.InvoiceId, x.ShipmentId)).Any(x => x.Count() != 1)
                || memberships.Where(x => !x.Cancelled).GroupBy(x => x.ShipmentId).Any(x => x.Count() != 1))
                return Failed(SalesDraftRiskReasonCodesV1.InvoiceRecordUnavailable);
            return new(memberships, string.Join("|", fingerprints.Order(StringComparer.Ordinal)), null);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            return Failed(SalesDraftRiskReasonCodesV1.InvoiceRecordUnavailable);
        }
    }
}
