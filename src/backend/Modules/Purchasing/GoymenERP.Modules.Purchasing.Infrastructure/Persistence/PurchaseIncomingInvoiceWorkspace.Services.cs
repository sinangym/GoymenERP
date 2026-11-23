using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchaseIncomingInvoiceWorkspace
{
    private async Task<PurchaseInvoiceReceiptCandidatePageV1> ServiceCandidatesAsync((Guid CompanyId, string Actor) scope,
        PurchaseIncomingInvoiceSnapshot invoice, PurchaseInvoiceLine line, int page, CancellationToken token)
    {
        if (line.ProductId != Guid.Empty || line.ItemType != "Service" || line.LinePurpose != "service") throw Unavailable();
        var query = from fact in context.ServiceAcceptances.AsNoTracking()
                    join order in context.ServiceOrders.AsNoTracking() on new { fact.CompanyId, fact.OrderId } equals new { order.CompanyId, order.OrderId }
                    join revision in context.ServiceOrderRevisions.AsNoTracking()
                        on new { order.CompanyId, order.OrderId, order.Version } equals new { revision.CompanyId, revision.OrderId, revision.Version }
                    let billed = context.ServiceInvoiceBindings.Where(x => x.AcceptanceId == fact.AcceptanceId).Sum(x => (decimal?)x.Quantity) ?? 0m
                    where fact.Quantity > billed && fact.SupplierPartyId == invoice.SupplierPartyId && fact.CompanyAccountId == invoice.CompanyAccountId
                        && fact.CurrencyId == invoice.CurrencyId && fact.ServiceTypeId == line.ServiceTypeId && fact.UnitId == line.UnitId
                    orderby fact.OrderId, fact.LineId, fact.AcceptanceId
                    select new { fact, revision.SnapshotJson, Available = fact.Quantity - billed };
        var rows = await GuardReadAsync(scope, () => query.Skip(page * PageSize).Take(PageSize + 1).ToArrayAsync(token), token);
        await FenceAsync(scope, token);
        var candidates = new List<PurchaseInvoiceReceiptCandidateV1>();
        foreach (var row in rows)
        {
            // K3 review: candidates are judged one by one. A source whose stored service-order snapshot is unreadable or no longer
            // matches the invoice (supplier, account, currency, approval, service type, unit) is NOT offered — fail-closed for that
            // candidate only; the match command re-reads and re-checks the source itself. A snapshot of another company is a broken
            // read, never a skipped row: the whole page stays 503.
            PurchaseServiceOrderState? order;
            try
            {
                order = JsonSerializer.Deserialize<PurchaseServiceOrderState>(row.SnapshotJson, PurchaseServiceOrderStoreV1.Json);
                if (order is not null) PurchaseServiceOrder.Validate(order);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException
                                                  or NotSupportedException or FormatException)
            {
                order = null;
            }

            if (order is not null && order.CompanyId != scope.CompanyId) throw Unavailable();
            if (ServiceCandidate(order, row.fact, row.Available, invoice, line) is { } candidate) candidates.Add(candidate);
        }
        await FenceAsync(scope, token);
        // The page boundary is the query's (rows, not offered candidates), so a skipped source never hides the next page.
        return new(invoice.Id, line.Id, invoice.Version.ToString(CultureInfo.InvariantCulture), page,
            rows.Length > PageSize, candidates.Take(PageSize).ToArray());
    }

    /// <summary>One offered service candidate, or null when this source cannot be offered (fail-closed for the candidate only).</summary>
    internal static PurchaseInvoiceReceiptCandidateV1? ServiceCandidate(PurchaseServiceOrderState? order, PurchaseServiceAcceptanceRow fact,
        decimal available, PurchaseIncomingInvoiceSnapshot invoice, PurchaseInvoiceLine line)
    {
        if (order is null || order.OrderId != fact.OrderId
            || order.SupplierPartyId != invoice.SupplierPartyId || order.CompanyAccountId != invoice.CompanyAccountId
            || order.Money.CurrencyId != invoice.CurrencyId || order.Status != PurchaseServiceOrderStatus.Approved
            || available <= 0m || available > PurchaseIncomingInvoice.MaximumStoredQuantity) return null;
        var sourceLines = order.Lines.Where(x => x.LineId == fact.LineId).ToArray();
        var acceptances = order.Acceptances.Where(x => x.AcceptanceId == fact.AcceptanceId).ToArray();
        if (sourceLines.Length != 1 || acceptances.Length != 1) return null;
        var sourceLine = sourceLines[0];
        if (sourceLine.ServiceTypeId != line.ServiceTypeId || sourceLine.UnitId != line.UnitId) return null;
        var accepted = acceptances[0];
        return new(order.OrderId, order.DocumentNumber, sourceLine.LineId,
            order.Lines.ToList().FindIndex(x => x.LineId == sourceLine.LineId) + 1, accepted.AcceptanceId, accepted.RecordedAtUtc,
            Guid.Empty, sourceLine.UnitId, PurchaseAcceptanceParticipantContractV1.FormatQuantity(available), sourceLine.ServiceTypeId);
    }
}
