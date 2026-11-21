using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Expenses;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchaseIncomingInvoiceMatchSource
{
    public async Task<PurchaseIncomingInvoiceMatchSourceV1> ReadWithAdditionalCostsAsync(PurchaseIncomingInvoice invoice,
        IReadOnlyList<PurchaseInvoiceReceiptSelectionV1> selected, IReadOnlyList<PurchaseInvoiceAdditionalCostSelectionV1> expenses,
        CancellationToken cancellationToken = default)
    {
        var receipts = await ReadAsync(invoice, selected, cancellationToken);
        var matches = new List<PurchaseInvoiceAdditionalCostMatch>();
        foreach (var item in expenses)
        {
            cancellationToken.ThrowIfCancellationRequested(); RequireScope(invoice.CompanyId);
            var line = invoice.Lines.SingleOrDefault(x => x.Id == item.InvoiceLineId) ?? throw Mismatch();
            if (line.ItemType != "Expense" || line.LinePurpose != "expense") throw Mismatch();
            var row = await context.AdditionalCosts.AsNoTracking().SingleOrDefaultAsync(x => x.CostId == item.CostId, cancellationToken);
            RequireScope(invoice.CompanyId);
            if (row is null) throw Mismatch();
            var cost = JsonSerializer.Deserialize<PurchaseAdditionalCostState>(row.SnapshotJson, PurchaseAdditionalCostStoreV1.Json) ?? throw Unavailable();
            PurchaseAdditionalCost.Validate(cost);
            if (cost.CompanyId != invoice.CompanyId || cost.CostId != row.CostId || cost.OrderId != item.PurchaseOrderId
                || cost.SupplierPartyId != invoice.SupplierPartyId || cost.CompanyAccountId != invoice.CompanyAccountId
                || cost.Money.CurrencyId != invoice.CurrencyId || cost.ExpenseType?.Id != line.ExpenseTypeId || cost.Tax.VatPercent != line.VatPercent)
                throw Mismatch();
            var amount = Quantity(item.NetAmount);
            var billed = await context.AdditionalCostInvoiceBindings.AsNoTracking().Where(x => x.CostId == item.CostId)
                .SumAsync(x => (decimal?)x.NetAmount, cancellationToken) ?? 0m;
            RequireScope(invoice.CompanyId);
            if (decimal.Round(amount, cost.Money.Scale) != amount || amount > cost.NetAmount - billed) throw Mismatch();
            matches.Add(new(invoice.CompanyId, line.Id, cost.CostId, cost.OrderId, cost.SourceOrderVersion,
                cost.SupplierPartyId, cost.CompanyAccountId, cost.Money.CurrencyId, amount, cost.NetAmount, cost.Tax.VatPercent,
                cost.BusinessDate, cost.ExpenseType?.Id));
        }
        return receipts with { AdditionalCosts = matches };
    }
}
