namespace GoymenERP.Modules.Inventory.Domain.Costing;

/// <summary>
/// AK-8 gate (SAT-D433, SAT-D440–SAT-D445; <c>docs/decisions/ADET_KG_FORMUL_GOLDEN_V1.md</c> section 4). The golden is part of
/// this train, so the lineage path is open. Setting <see cref="SupplierReturnLineageGoldenAccepted"/> to false restores the
/// earlier boundary exactly: a supplier return whose source label is not the receipt's own label is refused with the existing
/// 503 receipt-trace code and nothing is staged.
/// </summary>
public static class SupplierReturnLineageGoldenV1
{
    public const bool SupplierReturnLineageGoldenAccepted = true;

    /// <summary>The gate read through one seam so tests can pin both the open and the closed behaviour.</summary>
    public static bool IsAccepted(bool? overrideForTests = null) => overrideForTests ?? SupplierReturnLineageGoldenAccepted;
}

/// <summary>Where one root of a label's lineage came from (SAT-D441: anything that is not a purchase receipt is "other").</summary>
public enum SupplierReturnLineageRootKind
{
    PurchaseReceipt = 1,
    Other = 2,
}

/// <summary>
/// One root of the returned label's lineage with its exact share (TI1). <see cref="ReceiptOrder"/> is the receipt order used for
/// ties (SAT-D440, SAT-D442); <see cref="SupplierId"/> is the receipt's supplier and is null for a non-purchase root.
/// </summary>
public sealed record SupplierReturnLineageRoot(
    Guid RootId, int ReceiptOrder, SupplierReturnLineageRootKind Kind, Guid? SupplierId, ExactCostRatio Share);

public enum SupplierReturnLineageMode
{
    /// <summary>SAT-D433: the lineage is only the returning supplier's receipts; distribute by lineage share.</summary>
    Lineage = 1,

    /// <summary>SAT-D441/SAT-D444/SAT-D445: any other lineage; the whole return is taken from one chosen receipt.</summary>
    Quantity = 2,
}

/// <summary>One root's exact share and receipt order, without its supplier (see <see cref="SupplierReturnLineageCalculator.DistributeByShare"/>).</summary>
public sealed record SupplierReturnLineageShare(Guid RootId, int ReceiptOrder, ExactCostRatio Share);

/// <summary>One receipt's part of a lineage return: kilograms always; pieces only for a piece-tracking label (KLN-D119).</summary>
public sealed record SupplierReturnLineagePart(Guid RootId, int ReceiptOrder, decimal QuantityKg, int? Pieces);

/// <summary>
/// The preparer's explicit invoiced/pending selection inside one receipt (K3-B2, SAT-D433 step 3) and the receipt's facts.
/// <see cref="RemainingKg"/> is the receipt's received kg minus earlier returns; the two remainders bound the selection (TI3).
/// Unit costs are base-currency TL per stock kg; null means unavailable (BS-1, TI4), never zero.
/// <see cref="InvoiceUnitCostTryPerKg"/> is the related invoice's unit cost used by the SAT-D392 comparison; null when the
/// invoice cost cannot be read.
/// </summary>
public sealed record SupplierReturnLineageReceiptSelection(
    Guid RootId,
    decimal RemainingKg,
    decimal InvoicedRemainingKg,
    decimal PendingRemainingKg,
    decimal InvoicedKg,
    decimal PendingKg,
    ExactCostRatio? InvoicedUnitCostTryPerKg,
    ExactCostRatio? PendingUnitCostTryPerKg,
    ExactCostRatio? InvoiceUnitCostTryPerKg);

public enum SupplierReturnLineageOutcome
{
    /// <summary>Every share is valued, no SAT-D392 difference: the return may be staged.</summary>
    Accepted = 1,

    /// <summary>TI3 / I07 / I14: a receipt (or its invoiced/pending part) has less open quantity than its share.</summary>
    ReceiptLimitExceeded = 2,

    /// <summary>The selection does not add up to the receipt's share (K3-B2: the preparer chooses explicitly).</summary>
    SelectionMismatch = 3,

    /// <summary>SAT-D392/SAT-D371: a readable cost difference; refused until the Finance spec names its account.</summary>
    CostDifference = 4,

    /// <summary>TI4 / I13: a cost (or the difference) cannot be read; the SAT-D393/D396/D408 exception path applies.</summary>
    CostUnavailable = 5,
}

/// <summary>The result of a lineage (SAT-D433) return.</summary>
public sealed record SupplierReturnLineageResult(
    SupplierReturnLineageOutcome Outcome,
    IReadOnlyList<SupplierReturnLineagePart> Parts,
    ExactCostRatio? ExactCostTry,
    decimal? CostTry,
    ExactCostRatio? ExactDifferenceTry);

/// <summary>The result of a quantity return (SAT-D441, SAT-D444, TI8).</summary>
public sealed record SupplierReturnQuantityResult(
    SupplierReturnLineageOutcome Outcome,
    decimal QuantityKg,
    decimal? StockExitTry,
    decimal InvoiceTry,
    ExactCostRatio? ExactDifferenceTry,
    decimal? DifferenceTry);

/// <summary>
/// Pure, exact arithmetic of SAT-D433 and its follow-up decisions. No intermediate rounding: kilograms are cut to three
/// decimals once (SAT-D440), pieces to whole numbers once (SAT-D442), amounts are displayed at 0.01 and the internal unit cost
/// at 0.0001, both half away from zero (SAT-D261, SAT-D265). Nothing here reads storage, chooses a price or picks a share for
/// the preparer.
/// </summary>
public static class SupplierReturnLineageCalculator
{
    public const int KilogramScale = 3;
    public const int AmountScale = 2;
    private static readonly ExactCostRatio Zero = ExactCostRatio.Of(0m);
    private static readonly ExactCostRatio One = ExactCostRatio.Of(1m);

    /// <summary>
    /// TI1: each root's exact share of <paramref name="labelId"/>. A split child keeps its parent's shares; a merge output is the
    /// kilogram-weighted mean of its inputs; manufacturing starts new material. Null when the graph is not a valid whole-label
    /// lineage or the shares do not add up to one (an unknown root is never guessed).
    /// </summary>
    public static IReadOnlyDictionary<Guid, ExactCostRatio>? ResolveShares(
        Guid labelId, IReadOnlyCollection<Guid> rootLabelIds, IReadOnlyCollection<InventoryCostLineageTransformation> transformations)
    {
        ArgumentNullException.ThrowIfNull(rootLabelIds);
        ArgumentNullException.ThrowIfNull(transformations);
        if (labelId == Guid.Empty || rootLabelIds.Count == 0 || rootLabelIds.Distinct().Count() != rootLabelIds.Count) return null;
        var result = new Dictionary<Guid, ExactCostRatio>();
        var total = Zero;
        foreach (var root in rootLabelIds)
        {
            if (!InventoryCostLineageShares.TryResolve(root, transformations, out var shares, out _)) return null;
            if (!shares.TryGetValue(labelId, out var share) || share.Sign <= 0) continue;
            result.Add(root, share);
            total = total.Add(share);
        }

        return result.Count != 0 && total.CompareTo(One) == 0 ? result : null;
    }

    /// <summary>
    /// SAT-D445: lineage distribution only when every root is a purchase receipt of <paramref name="supplierId"/>; any other
    /// lineage (another supplier, production, opening, customer return, or no receipt of the supplier at all) is a quantity
    /// return, taken whole — the supplier's own share is not separated.
    /// </summary>
    public static SupplierReturnLineageMode Classify(IReadOnlyList<SupplierReturnLineageRoot> roots, Guid supplierId)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (supplierId == Guid.Empty) throw new ArgumentException("A supplier is required.", nameof(supplierId));
        RequireRoots(roots);
        return roots.All(root => root.Kind == SupplierReturnLineageRootKind.PurchaseReceipt && root.SupplierId == supplierId)
            ? SupplierReturnLineageMode.Lineage
            : SupplierReturnLineageMode.Quantity;
    }

    /// <summary>
    /// SAT-D433 step 2 with SAT-D440 and SAT-D442: kilograms by exact share cut to three decimals, the remainder to the largest
    /// share (ties: earliest receipt); pieces by exact share cut to whole numbers, the remainder to the largest share (ties:
    /// earliest receipt). Kilograms stay proportional on their own.
    /// </summary>
    public static IReadOnlyList<SupplierReturnLineagePart> Distribute(
        IReadOnlyList<SupplierReturnLineageRoot> roots, decimal quantityKg, int? pieces)
    {
        ArgumentNullException.ThrowIfNull(roots);
        RequireRoots(roots);
        return DistributeByShare(roots.Select(root => new SupplierReturnLineageShare(root.RootId, root.ReceiptOrder, root.Share)).ToArray(),
            quantityKg, pieces);
    }

    /// <summary>
    /// The same SAT-D440/SAT-D442 distribution for an owner that knows the shares but not the suppliers (Inventory re-checks the
    /// distribution it is asked to stage). The shares are distinct, ordered, positive and add up to exactly one.
    /// </summary>
    public static IReadOnlyList<SupplierReturnLineagePart> DistributeByShare(
        IReadOnlyList<SupplierReturnLineageShare> shares, decimal quantityKg, int? pieces)
    {
        ArgumentNullException.ThrowIfNull(shares);
        if (shares.Count == 0 || shares.Any(share => share is null || share.RootId == Guid.Empty || share.Share.Sign <= 0)
            || shares.Select(share => share.RootId).Distinct().Count() != shares.Count
            || shares.Select(share => share.ReceiptOrder).Distinct().Count() != shares.Count
            || shares.Aggregate(Zero, (sum, share) => sum.Add(share.Share)).CompareTo(One) != 0)
            throw new ArgumentException("Lineage shares must be distinct, ordered, positive and add up to one.", nameof(shares));
        if (quantityKg <= 0m || decimal.Round(quantityKg, KilogramScale) != quantityKg)
            throw new ArgumentOutOfRangeException(nameof(quantityKg), "A positive kg with at most three decimals is required.");
        if (pieces is < 1) throw new ArgumentOutOfRangeException(nameof(pieces), "A piece return moves at least one piece.");

        var ordered = shares.OrderBy(root => root.ReceiptOrder).ToArray();
        var largest = ordered.Aggregate((best, next) => next.Share.CompareTo(best.Share) > 0 ? next : best);
        var kilograms = ordered.ToDictionary(root => root.RootId, root => Floor(ExactCostRatio.Of(quantityKg).Multiply(root.Share), KilogramScale));
        kilograms[largest.RootId] += quantityKg - kilograms.Values.Sum();
        Dictionary<Guid, int>? counts = null;
        if (pieces is { } total)
        {
            counts = ordered.ToDictionary(root => root.RootId, root => (int)Floor(ExactCostRatio.Of(total).Multiply(root.Share), 0));
            counts[largest.RootId] += total - counts.Values.Sum();
        }

        return ordered.Select(root => new SupplierReturnLineagePart(root.RootId, root.ReceiptOrder, kilograms[root.RootId],
            counts?[root.RootId])).ToArray();
    }

    /// <summary>
    /// SAT-D433 steps 3–5 and 8 with TI2–TI5: validates the explicit selection against each receipt's share and remainders,
    /// values each part at its own TL cost per kg, and applies the SAT-D392 comparison of each invoiced part with its invoice.
    /// Order of refusals: an open quantity shortfall (TI3), then a selection that does not cover the share, then an unreadable
    /// cost (TI4), then a readable difference (SAT-D371).
    /// </summary>
    public static SupplierReturnLineageResult Value(
        IReadOnlyList<SupplierReturnLineagePart> parts, IReadOnlyList<SupplierReturnLineageReceiptSelection> selections)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(selections);
        if (parts.Count == 0 || parts.Select(part => part.RootId).Distinct().Count() != parts.Count
            || selections.Select(selection => selection.RootId).Distinct().Count() != selections.Count)
            throw new ArgumentException("Each receipt part and selection appears once.");

        var byRoot = selections.ToDictionary(selection => selection.RootId);
        foreach (var part in parts.Where(part => part.QuantityKg > 0m))
        {
            if (!byRoot.TryGetValue(part.RootId, out var selection))
                return Refused(SupplierReturnLineageOutcome.SelectionMismatch, parts);
            if (part.QuantityKg > selection.RemainingKg)
                return Refused(SupplierReturnLineageOutcome.ReceiptLimitExceeded, parts);
        }

        foreach (var part in parts.Where(part => part.QuantityKg > 0m))
        {
            var selection = byRoot[part.RootId];
            if (selection.InvoicedKg < 0m || selection.PendingKg < 0m
                || selection.InvoicedKg + selection.PendingKg != part.QuantityKg)
                return Refused(SupplierReturnLineageOutcome.SelectionMismatch, parts);
            if (selection.InvoicedKg > selection.InvoicedRemainingKg || selection.PendingKg > selection.PendingRemainingKg)
                return Refused(SupplierReturnLineageOutcome.ReceiptLimitExceeded, parts);
        }

        if (selections.Any(selection => parts.All(part => part.RootId != selection.RootId || part.QuantityKg == 0m)
                && (selection.InvoicedKg != 0m || selection.PendingKg != 0m)))
            return Refused(SupplierReturnLineageOutcome.SelectionMismatch, parts);

        var cost = Zero;
        var difference = Zero;
        var unavailable = false;
        foreach (var part in parts.Where(part => part.QuantityKg > 0m))
        {
            var selection = byRoot[part.RootId];
            if (selection.InvoicedKg > 0m)
            {
                if (selection.InvoicedUnitCostTryPerKg is not { } invoiced || selection.InvoiceUnitCostTryPerKg is not { } invoice)
                    unavailable = true;
                else
                {
                    cost = cost.Add(invoiced.Multiply(selection.InvoicedKg));
                    difference = difference.Add(invoice.Subtract(invoiced).Multiply(selection.InvoicedKg));
                }
            }

            if (selection.PendingKg > 0m)
            {
                if (selection.PendingUnitCostTryPerKg is not { } pending) unavailable = true;
                else cost = cost.Add(pending.Multiply(selection.PendingKg));
            }
        }

        if (unavailable)
            return new SupplierReturnLineageResult(SupplierReturnLineageOutcome.CostUnavailable, parts, null, null, null);
        return new SupplierReturnLineageResult(
            difference.Sign == 0 ? SupplierReturnLineageOutcome.Accepted : SupplierReturnLineageOutcome.CostDifference,
            parts, cost, cost.Round(AmountScale), difference);
    }

    /// <summary>
    /// SAT-D441, SAT-D444, TI8: the whole return leaves the chosen receipt (never moved to another one, I14); stock leaves at
    /// the warehouse/product moving average; the supplier is billed at the chosen purchase order's unit price; the difference
    /// is kg × (average − price). An unreadable average leaves the stock value and the difference unavailable (exception path).
    /// </summary>
    public static SupplierReturnQuantityResult QuantityReturn(
        decimal quantityKg, decimal selectedReceiptOpenKg, decimal? averageCostTryPerKg, decimal orderUnitPriceTryPerKg)
    {
        if (quantityKg <= 0m || decimal.Round(quantityKg, KilogramScale) != quantityKg)
            throw new ArgumentOutOfRangeException(nameof(quantityKg));
        if (orderUnitPriceTryPerKg <= 0m) throw new ArgumentOutOfRangeException(nameof(orderUnitPriceTryPerKg));
        if (averageCostTryPerKg is <= 0m) throw new ArgumentOutOfRangeException(nameof(averageCostTryPerKg));
        var invoice = ExactCostRatio.Of(quantityKg).Multiply(orderUnitPriceTryPerKg);
        if (quantityKg > selectedReceiptOpenKg)
            return new SupplierReturnQuantityResult(SupplierReturnLineageOutcome.ReceiptLimitExceeded, quantityKg, null,
                invoice.Round(AmountScale), null, null);
        if (averageCostTryPerKg is not { } average)
            return new SupplierReturnQuantityResult(SupplierReturnLineageOutcome.CostUnavailable, quantityKg, null,
                invoice.Round(AmountScale), null, null);
        var exit = ExactCostRatio.Of(quantityKg).Multiply(average);
        var difference = exit.Subtract(invoice);
        return new SupplierReturnQuantityResult(
            difference.Sign == 0 ? SupplierReturnLineageOutcome.Accepted : SupplierReturnLineageOutcome.CostDifference,
            quantityKg, exit.Round(AmountScale), invoice.Round(AmountScale), difference, difference.Round(AmountScale));
    }

    /// <summary>
    /// SAT-D456: the return difference is the kuruş-rounded stock exit amount (kg × the moving average) minus the kuruş-rounded
    /// amount the document bills. Both are rounded once, half away from zero (SAT-D261); nothing else is rounded.
    /// </summary>
    public static (decimal StockExitTry, decimal DifferenceTry) QuantityReturnAmountDifference(
        decimal quantityKg, decimal averageCostTryPerKg, decimal billedAmountTry)
    {
        if (quantityKg <= 0m || decimal.Round(quantityKg, KilogramScale) != quantityKg) throw new ArgumentOutOfRangeException(nameof(quantityKg));
        if (averageCostTryPerKg <= 0m) throw new ArgumentOutOfRangeException(nameof(averageCostTryPerKg));
        if (billedAmountTry <= 0m || decimal.Round(billedAmountTry, AmountScale) != billedAmountTry)
            throw new ArgumentOutOfRangeException(nameof(billedAmountTry));
        var exit = ExactCostRatio.Of(quantityKg).Multiply(averageCostTryPerKg).Round(AmountScale);
        return (exit, exit - billedAmountTry);
    }

    /// <summary>SAT-D306 (2), I05: the remaining pool's average after the exact return value leaves; null at zero stock.</summary>
    public static decimal? RemainingAverage(decimal poolKg, decimal poolAverageTryPerKg, decimal exitKg, ExactCostRatio exitValueTry)
    {
        ArgumentNullException.ThrowIfNull(exitValueTry);
        if (poolKg <= 0m || exitKg <= 0m || exitKg > poolKg) throw new ArgumentOutOfRangeException(nameof(exitKg));
        if (exitKg == poolKg) return null;
        return ExactCostRatio.Of(poolKg).Multiply(poolAverageTryPerKg).Subtract(exitValueTry)
            .Divide(poolKg - exitKg).Round(AverageCostPrecision.InternalScale);
    }

    private static SupplierReturnLineageResult Refused(SupplierReturnLineageOutcome outcome,
        IReadOnlyList<SupplierReturnLineagePart> parts) => new(outcome, parts, null, null, null);

    private static void RequireRoots(IReadOnlyList<SupplierReturnLineageRoot> roots)
    {
        if (roots.Count == 0 || roots.Any(root => root is null || root.RootId == Guid.Empty || root.Share.Sign <= 0
                || !Enum.IsDefined(root.Kind)
                || (root.Kind == SupplierReturnLineageRootKind.PurchaseReceipt) != (root.SupplierId is { } id && id != Guid.Empty))
            || roots.Select(root => root.RootId).Distinct().Count() != roots.Count
            || roots.Select(root => root.ReceiptOrder).Distinct().Count() != roots.Count
            || roots.Aggregate(Zero, (sum, root) => sum.Add(root.Share)).CompareTo(One) != 0)
            throw new ArgumentException("Lineage roots must be distinct, ordered, positive and add up to one.", nameof(roots));
    }

    /// <summary>Cuts a non-negative exact value down to <paramref name="scale"/> decimals (never up).</summary>
    private static decimal Floor(ExactCostRatio value, int scale)
    {
        if (value.Sign < 0) throw new ArgumentOutOfRangeException(nameof(value));
        var rounded = value.Round(scale);
        return ExactCostRatio.Of(rounded).CompareTo(value) > 0 ? rounded - Step(scale) : rounded;
    }

    private static decimal Step(int scale) => new(1, 0, 0, false, (byte)scale);
}
