using System.Text.Json;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore
{
    private async Task<PurchaseServiceInvoiceBindingRow> PrepareServiceInvoiceBindingAsync(PurchaseIncomingInvoice invoice, PurchaseInvoiceReceiptMatch match, CancellationToken token)
    {
        var parent = await dbContext.ServiceOrders.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == match.OrderId, token);
        if (parent is null || parent.Version != match.OrderVersion) throw ServiceInvoiceMismatch();
        var revision = await dbContext.ServiceOrderRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == parent.OrderId && x.Version == parent.Version, token);
        if (revision is null) throw new InvalidOperationException("Hizmet siparişi görüntüsü yok.");
        var source = JsonSerializer.Deserialize<PurchaseServiceOrderState>(revision.SnapshotJson, PurchaseServiceOrderStoreV1.Json) ?? throw new InvalidOperationException();
        PurchaseServiceOrder.Validate(source);
        var line = source.Lines.SingleOrDefault(x => x.LineId == match.OrderLineId);
        var accepted = source.Acceptances.SingleOrDefault(x => x.AcceptanceId == match.AcceptanceId && x.LineId == match.OrderLineId);
        var invoiceLine = invoice.Lines.Single(x => x.Id == match.InvoiceLineId);
        if (source.CompanyId != invoice.CompanyId || source.OrderId != match.OrderId || source.Version != match.OrderVersion
            || source.SupplierPartyId != invoice.SupplierPartyId || source.CompanyAccountId != invoice.CompanyAccountId
            || source.Money.CurrencyId != invoice.CurrencyId || line is null || accepted is null || line.UnitId != match.UnitId
            || line.ServiceTypeId != match.ServiceTypeId || invoiceLine.ServiceTypeId != match.ServiceTypeId
            || invoiceLine.ProductId != Guid.Empty || invoiceLine.ItemType != "Service" || invoiceLine.LinePurpose != "service") throw ServiceInvoiceMismatch();
        var fact = await dbContext.ServiceAcceptances.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == match.OrderId && x.AcceptanceId == match.AcceptanceId, token);
        if (fact is null || fact.LineId != line.LineId || fact.Quantity != accepted.Quantity || fact.OrderVersionAfter != accepted.OrderVersionAfter)
            throw new InvalidOperationException("Hizmet kabul faktörü görüntüsü uyuşmuyor.");
        var billed = await dbContext.ServiceInvoiceBindings.AsNoTracking().Where(x => x.AcceptanceId == match.AcceptanceId)
            .SumAsync(x => (decimal?)x.Quantity, token) ?? 0m;
        if (match.Quantity > fact.Quantity - billed) throw ServiceInvoiceMismatch();
        return new() { CompanyId = invoice.CompanyId, InvoiceId = invoice.Id, InvoiceLineId = match.InvoiceLineId,
            OrderId = match.OrderId, LineId = match.OrderLineId, AcceptanceId = match.AcceptanceId, Quantity = match.Quantity };
    }
    private static PurchasingRuleException ServiceInvoiceMismatch() => new(PurchaseIncomingInvoice.SourceMismatch, "Fatura ve gerçek hizmet kabulü uyuşmuyor.");
}
