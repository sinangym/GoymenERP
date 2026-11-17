using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Expenses;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchaseIncomingInvoiceWorkspace
{
    public async Task<PurchaseInvoiceAdditionalCostCandidatePageV1> AdditionalCostCandidatesAsync(Guid invoiceId, Guid invoiceLineId,
        int pageIndex, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(cancellationToken); ValidatePage(pageIndex);
        var snapshot = await workflow.ReadAsync(invoiceId, cancellationToken); await FenceAsync(scope, cancellationToken);
        if (snapshot.CompanyId != scope.CompanyId || snapshot.Id != invoiceId) throw Unavailable();
        var line = snapshot.Lines.SingleOrDefault(x => x.Id == invoiceLineId) ?? throw new PurchasingServiceException(404, "not_found");
        var items = new List<PurchaseInvoiceAdditionalCostCandidateV1>();
        if (line.ItemType != "Expense" || line.LinePurpose != "expense" || snapshot.Status != PurchaseIncomingInvoiceStatus.Recorded)
            return new(invoiceId, invoiceLineId, snapshot.Version.ToString(CultureInfo.InvariantCulture), snapshot.CurrencyId, pageIndex, false, []);
        // Bounded advisory pages of immutable facts; authoritative remaining value is re-read under owner locks by Match.
        var rows = await GuardReadAsync(scope, () => context.AdditionalCosts.AsNoTracking().OrderBy(x => x.RecordedAtUtc).ThenBy(x => x.CostId)
            .Skip(pageIndex * PageSize).Take(PageSize + 1).ToArrayAsync(cancellationToken), cancellationToken);
        foreach (var row in rows.Take(PageSize))
        {
            PurchaseAdditionalCostState cost;
            try { cost = JsonSerializer.Deserialize<PurchaseAdditionalCostState>(row.SnapshotJson, PurchaseAdditionalCostStoreV1.Json) ?? throw Unavailable(); PurchaseAdditionalCost.Validate(cost); }
            catch (Exception exception) when (exception is JsonException or ArgumentException or OverflowException) { throw Unavailable(); }
            if (cost.CompanyId != scope.CompanyId || cost.CostId != row.CostId || cost.OrderId != row.OrderId) throw Unavailable();
            if (cost.SupplierPartyId != snapshot.SupplierPartyId || cost.CompanyAccountId != snapshot.CompanyAccountId
                || cost.Money.CurrencyId != snapshot.CurrencyId || cost.ExpenseType?.Id != line.ExpenseTypeId || cost.Tax.VatPercent != line.VatPercent) continue;
            var billed = await GuardReadAsync(scope, () => context.AdditionalCostInvoiceBindings.AsNoTracking().Where(x => x.CostId == cost.CostId)
                .SumAsync(x => (decimal?)x.NetAmount, cancellationToken), cancellationToken) ?? 0m;
            await FenceAsync(scope, cancellationToken);
            if (billed > cost.NetAmount) throw Unavailable();
            var available = cost.NetAmount - billed;
            if (available <= 0) continue;
            items.Add(new(cost.CostId, cost.OrderId, cost.SourceOrderNumber, cost.Description, cost.ExpenseType?.Id, cost.ExpenseType?.Name,
                cost.BusinessDate, Text(cost.NetAmount), Text(available), line.VatPercent.ToString(CultureInfo.InvariantCulture)));
        }
        await FenceAsync(scope, cancellationToken);
        return new(invoiceId, invoiceLineId, snapshot.Version.ToString(CultureInfo.InvariantCulture), snapshot.CurrencyId, pageIndex, rows.Length > PageSize, items);
        static string Text(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    }
}
