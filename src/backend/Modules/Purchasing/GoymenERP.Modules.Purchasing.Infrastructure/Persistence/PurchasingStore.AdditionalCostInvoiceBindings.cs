using System.Text.Json;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Expenses;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore
{
    private async Task<IReadOnlyList<PurchaseAdditionalCostInvoiceBindingRow>> PrepareAdditionalCostInvoiceBindingsAsync(
        PurchaseIncomingInvoice invoice, Guid[] lockedOrders, CancellationToken token)
    {
        var output = new List<PurchaseAdditionalCostInvoiceBindingRow>();
        foreach (var group in invoice.AdditionalCosts.GroupBy(x => x.CostId))
        {
            var row = await dbContext.AdditionalCosts.AsNoTracking().SingleOrDefaultAsync(x => x.CostId == group.Key, token);
            if (row is null) throw ExpenseInvoiceMismatch();
            var cost = JsonSerializer.Deserialize<PurchaseAdditionalCostState>(row.SnapshotJson, PurchaseAdditionalCostStoreV1.Json) ?? throw new InvalidOperationException();
            PurchaseAdditionalCost.Validate(cost);
            if (cost.CompanyId != invoice.CompanyId || cost.CostId != row.CostId || cost.OrderId != row.OrderId
                || !lockedOrders.Contains(cost.OrderId) || cost.SupplierPartyId != invoice.SupplierPartyId
                || cost.CompanyAccountId != invoice.CompanyAccountId || cost.Money.CurrencyId != invoice.CurrencyId) throw ExpenseInvoiceMismatch();
            var billed = await dbContext.AdditionalCostInvoiceBindings.AsNoTracking().Where(x => x.CostId == cost.CostId)
                .SumAsync(x => (decimal?)x.NetAmount, token) ?? 0m;
            if (CommercialLinePriceCalculator.ExactSum(group.Select(x => x.NetAmount)) > cost.NetAmount - billed) throw ExpenseInvoiceMismatch();
            foreach (var match in group)
            {
                var line = invoice.Lines.Single(x => x.Id == match.InvoiceLineId);
                if (match.OrderId != cost.OrderId || match.CompanyId != invoice.CompanyId || match.SourceOrderVersion != cost.SourceOrderVersion
                    || match.SourceNetAmount != cost.NetAmount || match.ExpenseTypeId != cost.ExpenseType?.Id || line.ExpenseTypeId != cost.ExpenseType?.Id
                    || match.VatPercent != cost.Tax.VatPercent || line.VatPercent != cost.Tax.VatPercent
                    || match.NetAmount <= 0 || decimal.Round(match.NetAmount, cost.Money.Scale) != match.NetAmount) throw ExpenseInvoiceMismatch();
                output.Add(new() { CompanyId = invoice.CompanyId, InvoiceId = invoice.Id, InvoiceLineId = line.Id, CostId = cost.CostId, NetAmount = match.NetAmount });
            }
        }
        return output;
    }
    private static PurchasingRuleException ExpenseInvoiceMismatch() => new(PurchaseIncomingInvoice.SourceMismatch, "Gerçek masrafın carisi veya kalan fatura tutarı uyuşmuyor.");
}
