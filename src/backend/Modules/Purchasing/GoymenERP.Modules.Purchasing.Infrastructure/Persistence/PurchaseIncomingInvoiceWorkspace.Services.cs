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
            var order = JsonSerializer.Deserialize<PurchaseServiceOrderState>(row.SnapshotJson, PurchaseServiceOrderStoreV1.Json) ?? throw Unavailable();
            PurchaseServiceOrder.Validate(order);
            if (order.CompanyId != scope.CompanyId || order.OrderId != row.fact.OrderId) throw Unavailable();
            if (order.SupplierPartyId != invoice.SupplierPartyId || order.CompanyAccountId != invoice.CompanyAccountId
                || order.Money.CurrencyId != invoice.CurrencyId || order.Status != PurchaseServiceOrderStatus.Approved) throw Unavailable();
            var sourceLine = order.Lines.Single(x => x.LineId == row.fact.LineId);
            if (sourceLine.ServiceTypeId != line.ServiceTypeId || sourceLine.UnitId != line.UnitId) throw Unavailable();
            var accepted = order.Acceptances.Single(x => x.AcceptanceId == row.fact.AcceptanceId);
            candidates.Add(new(order.OrderId, order.DocumentNumber, sourceLine.LineId,
                order.Lines.ToList().FindIndex(x => x.LineId == sourceLine.LineId) + 1, accepted.AcceptanceId, accepted.RecordedAtUtc,
                Guid.Empty, sourceLine.UnitId, PurchaseAcceptanceParticipantContractV1.FormatQuantity(row.Available), sourceLine.ServiceTypeId));
        }
        await FenceAsync(scope, token);
        return new(invoice.Id, line.Id, invoice.Version.ToString(CultureInfo.InvariantCulture), page,
            candidates.Count > PageSize, candidates.Take(PageSize).ToArray());
    }
}
