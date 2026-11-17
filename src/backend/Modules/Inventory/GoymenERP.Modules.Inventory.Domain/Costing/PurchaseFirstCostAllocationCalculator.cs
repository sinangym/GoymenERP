namespace GoymenERP.Modules.Inventory.Domain.Costing;

public sealed record PurchaseFirstCostStockShare(string Key, string LocationCode, int SourceLine,
    ExactCostRatio RootQuantityKg);

public sealed record PurchaseFirstCostStockValue(string Key, ExactCostRatio InvoicedQuantityKg,
    ExactCostRatio TlValue);

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
        ExactCostRatio netShippedKg, IReadOnlyList<PurchaseFirstCostStockShare> stock, int moneyScale)
    {
        ArgumentNullException.ThrowIfNull(invoiceTlValue);
        ArgumentNullException.ThrowIfNull(stock);
        if (invoiceTlValue.Sign < 0 || acceptedPhysicalKg.Sign <= 0 || invoicedPhysicalKg.Sign <= 0
            || invoicedPhysicalKg.CompareTo(acceptedPhysicalKg) > 0 || netShippedKg.Sign < 0
            || moneyScale is < 0 or > 28 || stock.Count > 65536
            || stock.Any(x => x is null || string.IsNullOrWhiteSpace(x.Key)
                || string.IsNullOrWhiteSpace(x.LocationCode) || x.SourceLine < 1 || x.RootQuantityKg.Sign <= 0)
            || stock.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != stock.Count)
            throw new ArgumentException("The first purchase-cost source is invalid.");

        var zero = ExactCostRatio.Of(0m);
        var remaining = stock.Aggregate(zero, (sum, x) => sum.Add(x.RootQuantityKg));
        if (remaining.Add(netShippedKg).CompareTo(acceptedPhysicalKg) != 0)
            throw new ArgumentException("The receipt's physical source does not reconcile.");
        var fraction = invoicedPhysicalKg.Divide(acceptedPhysicalKg);
        var consumedKg = netShippedKg.Multiply(fraction);
        var consumedExact = invoiceTlValue.Multiply(netShippedKg).Divide(acceptedPhysicalKg);
        var consumedRounded = consumedExact.Round(moneyScale);
        // A sub-minor source with stock remaining must not allocate more than its total.
        // Preserve the actual signed rounding residue instead of inventing a negative stock value.
        var consumedValue = ExactCostRatio.Of(consumedRounded);
        if (remaining.Sign > 0 && consumedValue.CompareTo(invoiceTlValue) > 0)
        {
            consumedRounded = invoiceTlValue.Round(moneyScale);
            consumedValue = ExactCostRatio.Of(consumedRounded);
            if (consumedValue.CompareTo(invoiceTlValue) > 0)
                throw new InvalidOperationException("The invoice source cannot fund the rounded consumed share.");
        }
        var stockTotal = remaining.Sign == 0 ? zero : invoiceTlValue.Subtract(consumedValue);
        var values = stock.Select(x => invoiceTlValue.Sign == 0 ? zero
            : ExactCostRatio.Of(stockTotal.Multiply(x.RootQuantityKg).Divide(remaining).Round(moneyScale))).ToArray();
        var residual = stockTotal.Subtract(values.Aggregate(zero, (sum, x) => sum.Add(x)));
        var priority = Enumerable.Range(0, stock.Count).OrderByDescending(i => stock[i].RootQuantityKg,
                Comparer<ExactCostRatio>.Create((left, right) => left.CompareTo(right)))
            .ThenBy(i => stock[i].SourceLine).ThenBy(i => stock[i].LocationCode, StringComparer.Ordinal).ToArray();
        foreach (var index in priority)
        {
            if (residual.Sign == 0) break;
            var corrected = values[index].Add(residual);
            if (corrected.Sign >= 0) { values[index] = corrected; residual = zero; }
            else { residual = corrected; values[index] = zero; }
        }
        if (residual.Sign != 0) throw new InvalidOperationException("The stock cost residual is not conserved.");
        return new(invoicedPhysicalKg, consumedKg, consumedRounded, consumedExact,
            consumedExact.Subtract(consumedValue), stock.Select((x, i) =>
                new PurchaseFirstCostStockValue(x.Key, x.RootQuantityKg.Multiply(fraction), values[i])).ToArray());
    }
}
