using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchaseIncomingInvoiceMatchSource
{
    private async Task<PurchaseInvoiceReceiptMatch> ReadServiceMatchAsync(PurchaseIncomingInvoice invoice, PurchaseInvoiceLine line,
        PurchaseInvoiceReceiptSelectionV1 selection, decimal quantity, CancellationToken token)
    {
        RequireScope(invoice.CompanyId);
        if (line.ItemType != "Service" || line.LinePurpose != "service" || line.ProductId != Guid.Empty || line.ServiceTypeId is not { } type || type == Guid.Empty)
            throw Mismatch();
        var order = await context.ServiceOrders.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == selection.PurchaseOrderId, token);
        RequireScope(invoice.CompanyId);
        if (order is null) throw Mismatch();
        var revision = await context.ServiceOrderRevisions.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == order.OrderId && x.Version == order.Version, token);
        RequireScope(invoice.CompanyId);
        if (revision is null) throw Unavailable();
        var value = JsonSerializer.Deserialize<PurchaseServiceOrderState>(revision.SnapshotJson, PurchaseServiceOrderStoreV1.Json) ?? throw Unavailable();
        PurchaseServiceOrder.Validate(value);
        var accepted = value.Acceptances.SingleOrDefault(x => x.AcceptanceId == selection.AcceptanceId);
        var service = accepted is null ? null : value.Lines.SingleOrDefault(x => x.LineId == accepted.LineId);
        if (value.CompanyId != invoice.CompanyId || value.OrderId != order.OrderId || value.Version != order.Version
            || value.SupplierPartyId != invoice.SupplierPartyId || value.CompanyAccountId != invoice.CompanyAccountId
            || value.Money.CurrencyId != invoice.CurrencyId || value.Status != PurchaseServiceOrderStatus.Approved
            || accepted is null || service is null || service.ServiceTypeId != type || service.UnitId != line.UnitId) throw Mismatch();
        if (service.UnitPrice is not { } unitPrice) throw Unavailable();
        var fact = await context.ServiceAcceptances.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == order.OrderId && x.AcceptanceId == accepted.AcceptanceId, token);
        RequireScope(invoice.CompanyId);
        if (fact is null || fact.LineId != service.LineId || fact.Quantity != accepted.Quantity || fact.OrderVersionAfter != accepted.OrderVersionAfter) throw Unavailable();
        var billed = await context.ServiceInvoiceBindings.AsNoTracking().Where(x => x.AcceptanceId == accepted.AcceptanceId).SumAsync(x => (decimal?)x.Quantity, token) ?? 0m;
        RequireScope(invoice.CompanyId);
        if (quantity > accepted.Quantity - billed) throw Mismatch();
        decimal? control = null;
        if (invoice.OfficialRate is not null)
        {
            ExchangeRateResolutionV1 answer;
            try { answer = await rates.ResolveAsync(accepted.AcceptedOn, invoice.CurrencyId, invoice.FunctionalCurrencyId, token); }
            catch (SettingsPrerequisiteException exception) { RequireScope(invoice.CompanyId); throw SettingsFailure(exception); }
            RequireScope(invoice.CompanyId);
            if (answer is not { State: ExchangeRateResolutionStatesV1.Resolved, BulletinDate: { } bulletin, SellingRate: { } text }
                || answer.RateDay != accepted.AcceptedOn || answer.SourceCurrencyId != invoice.CurrencyId || answer.TargetCurrencyId != invoice.FunctionalCurrencyId
                || bulletin > accepted.AcceptedOn || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate) || rate <= 0) throw Unavailable();
            control = rate;
        }
        return new(invoice.CompanyId, line.Id, accepted.AcceptanceId, order.OrderId, service.LineId, order.Version,
            value.SupplierPartyId, value.CompanyAccountId, value.Money.CurrencyId, service.UnitId, quantity,
            CommercialLinePriceCalculator.ExactLineAmount(quantity, unitPrice, null, null, null, null), control, accepted.AcceptedOn, type);
    }
}
