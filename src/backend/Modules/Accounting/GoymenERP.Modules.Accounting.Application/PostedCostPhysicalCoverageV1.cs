namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Combines proved original roots, not journal amounts. Every physical quantity can be economically claimed only once.</summary>
public static class PostedCostPhysicalCoverageV1
{
    public static bool IsComplete(IReadOnlyList<PostedManufacturingCostShareV1> manufacturing,
        IReadOnlyList<PostedPurchaseCostShareV1> purchasing)
    {
        var zero = AccountingInventoryCostAmountsV1.FromDecimal(0m);
        var one = AccountingInventoryCostAmountsV1.FromDecimal(1m);
        var coverage = zero;
        var productionReceipts = new HashSet<Guid>();
        foreach (var root in manufacturing.GroupBy(x => (x.OutputId, x.ReceiptMovementId)))
        {
            var fractions = root.Select(x => x.Shipment.RootShare).Distinct().ToArray();
            if (fractions.Length != 1 || !PositiveAtMostOne(fractions[0]) || !productionReceipts.Add(root.Key.ReceiptMovementId)) return false;
            coverage = AccountingInventoryCostAmountsV1.Add(coverage, fractions[0]);
        }
        var candidates = purchasing.SelectMany(x => x.Candidates).ToArray();
        if (candidates.Select(x => (x.SourceEventId, x.InvoiceLineId, x.AcceptanceId, x.Shipment.MovementId,
                x.Shipment.MovementLineNumber)).Distinct().Count() != candidates.Length) return false;
        foreach (var root in candidates.GroupBy(x => (x.ReceiptMovementId, x.RootLabelId)))
        {
            if (productionReceipts.Contains(root.Key.ReceiptMovementId)) return false;
            var original = root.Select(x => x.Shipment.RootShare).Distinct().ToArray();
            if (original.Length != 1 || !PositiveAtMostOne(original[0])) return false;
            var sum = zero;
            foreach (var candidate in root)
            {
                if (!PositiveAtMostOne(candidate.InvoicedRootShare)) return false;
                sum = AccountingInventoryCostAmountsV1.Add(sum, candidate.InvoicedRootShare);
            }
            if (!AccountingInventoryCostAmountsV1.TryRead(sum, out var n, out var d)
                || !AccountingInventoryCostAmountsV1.TryRead(original[0], out var on, out var od) || n * od > on * d) return false;
            coverage = AccountingInventoryCostAmountsV1.Add(coverage, sum);
        }
        return coverage == one;
    }

    private static bool PositiveAtMostOne(AccountingInventoryCostRatioV1 value) =>
        AccountingInventoryCostAmountsV1.TryRead(value, out var n, out var d) && n > 0 && n <= d;
}
