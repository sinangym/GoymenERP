namespace GoymenERP.Modules.Inventory.Domain.Costing;

public sealed record PurchaseFirstCostStockShare(string Key, string LocationCode, int SourceLine,
    ExactCostRatio RootQuantityKg);

public sealed record PurchaseFirstCostStockValue(string Key, ExactCostRatio InvoicedQuantityKg,
    ExactCostRatio TlValue);

public sealed record PurchaseFirstCostReceiptShare(ExactCostRatio InvoiceTlValue,
    ExactCostRatio AcceptedPhysicalKg, ExactCostRatio InvoicedPhysicalKg, ExactCostRatio NetShippedKg,
    IReadOnlyList<PurchaseFirstCostStockShare> Stock);

public sealed record PurchaseFirstCostAllocation(ExactCostRatio InvoicedQuantityKg,
    ExactCostRatio ConsumedQuantityKg, decimal ConsumedTlAmount, ExactCostRatio ConsumedExactTlAmount,
    ExactCostRatio ConsumedRoundingResidue, IReadOnlyList<PurchaseFirstCostStockValue> Stock);

/// <summary>BS-1, human answers 5/6/11: the caller supplies proven physical source shares and the actual
/// company profile scale. No physical binding is invented. A partial invoice without such a binding
/// follows the remaining stock/net-shipment ratio; the uninvoiced share remains pending. Historical
/// movement stamps are never repriced. Only the consumed monetary share is rounded, once; stock
/// retains the authoritative residual. Stock residual priority is kg, source line, location code.</summary>
public static class PurchaseFirstCostAllocationCalculator
{
    public const string Version = "inventory.purchase-first-cost-allocation.v1";

    public static PurchaseFirstCostAllocation Allocate(ExactCostRatio invoiceTlValue,
        ExactCostRatio acceptedPhysicalKg, ExactCostRatio invoicedPhysicalKg,
        ExactCostRatio netShippedKg, IReadOnlyList<PurchaseFirstCostStockShare> stock, int moneyScale) =>
        AllocateLine([new(invoiceTlValue, acceptedPhysicalKg, invoicedPhysicalKg, netShippedKg, stock)], moneyScale);

    /// <summary>One original invoice line can cover several actual receipts. Sum its exact consumed
    /// cost before rounding. Pool shares retain their own receipt's exact cost basis; only the final
    /// conserved monetary residue follows the decided kg/source-line/location priority.</summary>
    public static PurchaseFirstCostAllocation AllocateLine(IReadOnlyList<PurchaseFirstCostReceiptShare> receipts,
        int moneyScale)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        if (moneyScale is < 0 or > 28 || receipts.Count is < 1 or > 10000)
            throw new ArgumentException("The first purchase-cost source is invalid.");
        var zero = ExactCostRatio.Of(0m);
        var invoiceTotal = zero; var invoicedTotal = zero; var consumedKg = zero; var consumedExact = zero;
        var positions = new Dictionary<string, (string Code, int Line, ExactCostRatio Kg, ExactCostRatio Value)>(StringComparer.Ordinal);
        foreach (var receipt in receipts)
        {
            if (receipt is null || receipt.InvoiceTlValue is null || receipt.AcceptedPhysicalKg is null
                || receipt.InvoicedPhysicalKg is null || receipt.NetShippedKg is null || receipt.Stock is null
                || receipt.InvoiceTlValue.Sign < 0 || receipt.AcceptedPhysicalKg.Sign <= 0
                || receipt.InvoicedPhysicalKg.Sign <= 0 || receipt.InvoicedPhysicalKg.CompareTo(receipt.AcceptedPhysicalKg) > 0
                || receipt.NetShippedKg.Sign < 0 || receipt.Stock.Count > 65536
                || receipt.Stock.Any(x => x is null || string.IsNullOrWhiteSpace(x.Key)
                    || string.IsNullOrWhiteSpace(x.LocationCode) || x.SourceLine < 1 || x.RootQuantityKg is null || x.RootQuantityKg.Sign <= 0)
                || receipt.Stock.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != receipt.Stock.Count)
                throw new ArgumentException("The first purchase-cost source is invalid.");
            var remaining = receipt.Stock.Aggregate(zero, (sum, x) => sum.Add(x.RootQuantityKg));
            if (remaining.Add(receipt.NetShippedKg).CompareTo(receipt.AcceptedPhysicalKg) != 0)
                throw new ArgumentException("The receipt's physical source does not reconcile.");
            var fraction = receipt.InvoicedPhysicalKg.Divide(receipt.AcceptedPhysicalKg);
            invoiceTotal = invoiceTotal.Add(receipt.InvoiceTlValue);
            invoicedTotal = invoicedTotal.Add(receipt.InvoicedPhysicalKg);
            consumedKg = consumedKg.Add(receipt.NetShippedKg.Multiply(fraction));
            consumedExact = consumedExact.Add(receipt.InvoiceTlValue.Multiply(receipt.NetShippedKg).Divide(receipt.AcceptedPhysicalKg));
            foreach (var stock in receipt.Stock)
            {
                var quantity = stock.RootQuantityKg.Multiply(fraction);
                var value = receipt.InvoiceTlValue.Multiply(stock.RootQuantityKg).Divide(receipt.AcceptedPhysicalKg);
                if (positions.TryGetValue(stock.Key, out var prior))
                {
                    if (prior.Code != stock.LocationCode) throw new ArgumentException("A stock position has conflicting location codes.");
                    positions[stock.Key] = (prior.Code, Math.Min(prior.Line, stock.SourceLine), prior.Kg.Add(quantity), prior.Value.Add(value));
                }
                else positions.Add(stock.Key, (stock.LocationCode, stock.SourceLine, quantity, value));
            }
        }
        var consumedRounded = consumedExact.Round(moneyScale);
        var consumedValue = ExactCostRatio.Of(consumedRounded);
        if (positions.Count > 0 && consumedValue.CompareTo(invoiceTotal) > 0)
        {
            consumedRounded = invoiceTotal.Round(moneyScale);
            consumedValue = ExactCostRatio.Of(consumedRounded);
            if (consumedValue.CompareTo(invoiceTotal) > 0)
                throw new InvalidOperationException("The invoice source cannot fund the rounded consumed share.");
        }
        var stockTotal = positions.Count == 0 ? zero : invoiceTotal.Subtract(consumedValue);
        var shares = positions.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
        var values = shares.Select(x => ExactCostRatio.Of(x.Value.Value.Round(moneyScale))).ToArray();
        var residual = stockTotal.Subtract(values.Aggregate(zero, (sum, x) => sum.Add(x)));
        var priority = Enumerable.Range(0, shares.Length).OrderByDescending(i => shares[i].Value.Kg,
                Comparer<ExactCostRatio>.Create((left, right) => left.CompareTo(right)))
            .ThenBy(i => shares[i].Value.Line).ThenBy(i => shares[i].Value.Code, StringComparer.Ordinal).ToArray();
        foreach (var index in priority)
        {
            if (residual.Sign == 0) break;
            var corrected = values[index].Add(residual);
            if (corrected.Sign >= 0) { values[index] = corrected; residual = zero; }
            else { residual = corrected; values[index] = zero; }
        }
        if (residual.Sign != 0) throw new InvalidOperationException("The stock cost residual is not conserved.");
        return new(invoicedTotal, consumedKg, consumedRounded, consumedExact, consumedExact.Subtract(consumedValue),
            shares.Select((x, i) => new PurchaseFirstCostStockValue(x.Key, x.Value.Kg, values[i])).ToArray());
    }
}
