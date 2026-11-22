using System.Globalization;
using System.Numerics;
using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>
/// AK-8 gate on the Purchasing side (SAT-D433, SAT-D440–SAT-D445; golden <c>docs/decisions/ADET_KG_FORMUL_GOLDEN_V1.md</c>
/// section 4, part of this train). It mirrors Inventory's <c>SupplierReturnLineageGoldenV1</c>; with it false a return that
/// names a source label is refused 503 <see cref="PurchaseSupplierReturnCodesV1.LineageUnavailable"/> before anything is read
/// or recorded, exactly the earlier boundary. <see cref="PurchaseSupplierReturnPolicyV1.LineageGoldenAccepted"/> carries it so a
/// test can pin the closed behaviour.
/// </summary>
public static class PurchaseSupplierReturnLineageGoldenV1
{
    public const bool SupplierReturnLineageGoldenAccepted = true;

    /// <summary>
    /// SAT-D453 (round a net price above four decimals), SAT-D454 (an approved manual stock cost may differ from the order price)
    /// and SAT-D455 (a quantity return from a piece order line). They are money formulas whose independent golden rows are not
    /// in main yet, so the gate is closed: every one of them keeps today's refusal (422 / 409 / 409) and nothing is recorded.
    /// <see cref="PurchaseSupplierReturnPolicyV1.QuantityRulesGoldenAccepted"/> carries it so tests pin both states.
    /// </summary>
    public const bool SupplierReturnQuantityRulesGoldenAccepted = false;
}

/// <summary>How a return leaves a foreign source label (stored as 1/2; shown as its name).</summary>
public enum PurchaseSupplierReturnLineageModeV1
{
    /// <summary>SAT-D433: the label's lineage is only the returning supplier's receipts.</summary>
    Lineage = 1,

    /// <summary>SAT-D441/SAT-D444/SAT-D445: any other lineage; the whole return is taken from the chosen receipt.</summary>
    Quantity = 2,
}

/// <summary>
/// One lineage root as Inventory answers it; <see cref="GoodsAcceptanceId"/> only for a purchase receipt root. Roots are answered
/// in receipt order (SAT-D440/SAT-D442 ties). <see cref="Receipt"/> is the receipt's open state when the owner supplied it.
/// </summary>
public sealed record PurchaseSupplierReturnLineageRootV1(bool IsPurchaseReceipt, Guid? GoodsAcceptanceId, string ShareNumerator,
    string ShareDenominator, PurchaseSupplierReturnReceiptStateV1? Receipt = null);

/// <summary>
/// A label's lineage roots (company-fenced); the shares add up to one. The stock facts are price-free:
/// <see cref="AverageCostReadable"/> says whether the label's location/product moving average can be read (SAT-D444).
/// </summary>
public sealed record PurchaseSupplierReturnLineageV1(Guid LabelId, IReadOnlyList<PurchaseSupplierReturnLineageRootV1> Roots,
    bool TracksPieces = false, string FreeKg = "0", string BlockedKg = "0", int? FreePieces = null, int? BlockedPieces = null,
    bool AverageCostReadable = false);

/// <summary>
/// Reads a label's lineage plan and a receipt's open state from the Inventory owner (wired by the host). Purchasing never reads
/// Inventory tables. An unreadable answer is given by throwing; a host without the port leaves foreign-label returns 503.
/// </summary>
public interface IPurchaseSupplierReturnLineageReadPortV1
{
    /// <summary>The lineage with what a return of <paramref name="quantityKg"/> / <paramref name="pieceCount"/> takes from each receipt.</summary>
    Task<PurchaseSupplierReturnLineageV1> PlanAsync(Guid companyId, Guid labelId, string quantityKg, int? pieceCount,
        CancellationToken cancellationToken);

    /// <summary>
    /// SAT-D456/SAT-D460: the kuruş-rounded stock exit amount of <paramref name="quantityKg"/> of the label at its moving average
    /// and that average; null when the average cannot be read. A host that does not answer leaves it unreadable.
    /// </summary>
    Task<PurchaseSupplierReturnStockAmountV1?> ReadStockAmountAsync(Guid companyId, Guid labelId, string quantityKg,
        CancellationToken cancellationToken) => Task.FromResult<PurchaseSupplierReturnStockAmountV1?>(null);

    /// <summary>The open state of one goods receipt (the chosen receipt of a quantity return).</summary>
    Task<PurchaseSupplierReturnReceiptStateV1> ReadReceiptAsync(Guid companyId, Guid goodsAcceptanceId, CancellationToken cancellationToken);
}

/// <summary>The kuruş-rounded stock exit amount of a quantity return and the TL/kg moving average it leaves at.</summary>
public sealed record PurchaseSupplierReturnStockAmountV1(decimal StockExitAmountTry, decimal AverageCostTryPerKg);

/// <summary>The planned way a foreign-label return leaves stock.</summary>
public sealed record PurchaseSupplierReturnLineagePlanV1(PurchaseSupplierReturnLineageModeV1 Mode);

/// <summary>
/// AK-8 planning rules (pure). SAT-D445: lineage distribution only when every root is a purchase receipt of the returning
/// supplier; any other lineage is a quantity return from the chosen receipt. In a lineage return the named acceptance is the
/// document's own receipt and must be one of the label's receipts; nothing is re-routed to another receipt.
/// </summary>
public static class PurchaseSupplierReturnLineagePlannerV1
{
    public static PurchaseSupplierReturnLineagePlanV1 Plan(PurchaseSupplierReturnLineageV1 lineage,
        IReadOnlyDictionary<Guid, Guid?> supplierByAcceptance, Guid supplierId, Guid goodsAcceptanceId)
    {
        ArgumentNullException.ThrowIfNull(lineage);
        ArgumentNullException.ThrowIfNull(supplierByAcceptance);
        var roots = lineage.Roots;
        if (roots is null || roots.Count == 0 || supplierId == Guid.Empty || goodsAcceptanceId == Guid.Empty
            || roots.Any(root => root is null || root.IsPurchaseReceipt != (root.GoodsAcceptanceId is not null)
                || root.GoodsAcceptanceId == Guid.Empty)
            || !SharesAddUpToOne(roots))
            throw Unavailable();
        var purchase = roots.Where(root => root.IsPurchaseReceipt).Select(root => root.GoodsAcceptanceId!.Value).ToArray();
        if (purchase.Distinct().Count() != purchase.Length
            || purchase.Any(acceptance => !supplierByAcceptance.TryGetValue(acceptance, out var supplier) || supplier is null))
            throw Unavailable();
        var onlySupplier = roots.All(root => root.IsPurchaseReceipt && supplierByAcceptance[root.GoodsAcceptanceId!.Value] == supplierId);
        if (!onlySupplier) return new(PurchaseSupplierReturnLineageModeV1.Quantity);
        if (!purchase.Contains(goodsAcceptanceId))
            throw new PurchasingServiceException(409, PurchaseSupplierReturnCodesV1.LineageReceiptMismatch,
                [new("goodsAcceptanceId", PurchaseSupplierReturnCodesV1.LineageReceiptMismatch)]);
        return new(PurchaseSupplierReturnLineageModeV1.Lineage);
    }

    internal static PurchasingServiceException Unavailable() => new(503, PurchaseSupplierReturnCodesV1.LineageUnavailable);

    private static bool SharesAddUpToOne(IReadOnlyList<PurchaseSupplierReturnLineageRootV1> roots)
    {
        BigInteger numerator = BigInteger.Zero, denominator = BigInteger.One;
        foreach (var root in roots)
        {
            if (!BigInteger.TryParse(root.ShareNumerator, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                || !BigInteger.TryParse(root.ShareDenominator, NumberStyles.None, CultureInfo.InvariantCulture, out var d)
                || n.IsZero || d.IsZero) return false;
            numerator = numerator * d + n * denominator;
            denominator *= d;
        }

        return numerator == denominator;
    }
}

/// <summary>What one order line offers a quantity return as its price (the facts the rule needs, nothing else).</summary>
public sealed record PurchaseSupplierReturnQuantityPriceLineV1(bool IsTurkishLira, bool IsKilogramLine, bool PricedPerOrderUnit,
    decimal? UnitPrice, decimal? DiscountPercent = null, decimal? DiscountAmount = null, bool IsPieceLine = false,
    bool HasDocumentDiscount = false);

public enum PurchaseSupplierReturnQuantityPriceOutcomeV1
{
    Priced = 1,

    /// <summary>409: not a priced kilogram line in TL (and, with the SAT-D455 gate closed, a piece line).</summary>
    NotAKilogramTryLine = 2,

    /// <summary>
    /// 422: the net price after the discount has more than four decimals (or does not fit) and the SAT-D453 gate is closed; it
    /// is never rounded without that rule.
    /// </summary>
    NotRepresentable = 3,

    /// <summary>422 (SAT-D458): the order carries a document-level discount; it is not deducted and not spread over the lines.</summary>
    DocumentDiscount = 4,
}

/// <summary><see cref="PerPiece"/>: the price is TL per piece of a piece order line (SAT-D455), otherwise TL per kg.</summary>
public sealed record PurchaseSupplierReturnQuantityPriceV1(PurchaseSupplierReturnQuantityPriceOutcomeV1 Outcome, decimal? UnitPriceTry,
    bool PerPiece = false)
{
    public decimal? UnitPriceTryPerKg => PerPiece ? null : UnitPriceTry;
}

/// <summary>
/// SAT-D444 / SAT-D451 / SAT-D453 / SAT-D455, THE single rule point for "the chosen purchase order's unit price" of a quantity
/// return (Sinan, 2026-11-22, <c>docs/decisions/SINAN_KARARLARI_2026-11-22_WORK_AI_DALGALAR.md</c>).
/// <list type="bullet">
/// <item>SAT-D451: the chosen order line's NET unit price AFTER the discount; the maturity difference is NOT included. The
/// discount arithmetic is the shared <see cref="CommercialLinePriceCalculator"/> (exact).</item>
/// <item>SAT-D453 (behind the quantity-rules gate): a net price above four decimals is rounded to four, half away from zero.
/// Gate closed: refused, never rounded.</item>
/// <item>SAT-D455 (behind the same gate): a piece order line is priced per piece. Gate closed: not a priced kilogram line.</item>
/// <item>SAT-D458 (behind the same gate): every LINE discount is deducted — the purchase order line carries a percent or an
/// amount, the only line discounts the model has (no chain) — and a document-level discount is never deducted: an order that
/// has one is refused. The purchase order model has no document-level discount today, so the fact is always false.</item>
/// </list>
/// </summary>
public static class PurchaseSupplierReturnQuantityPriceRuleV1
{
    /// <summary>SAT-D451: net of the discount, without the deferral.</summary>
    public const string DiscountedLinePriceDecision = "SAT-D451: net-after-discount, deferral-excluded";

    public const int UnitPriceScale = 4;
    public const int AmountScale = 2;

    /// <summary>SAT-D453/SAT-D456: quantity × the (rounded) net price, rounded once to kuruş, half away from zero.</summary>
    public static decimal Amount(decimal quantity, decimal unitPriceTry) =>
        decimal.Round(quantity * unitPriceTry, AmountScale, MidpointRounding.AwayFromZero);
    private const decimal UnitCostUpperBound = 100_000_000_000_000m;

    public static PurchaseSupplierReturnQuantityPriceV1 Resolve(PurchaseSupplierReturnQuantityPriceLineV1 line,
        bool quantityRulesAccepted = PurchaseSupplierReturnLineageGoldenV1.SupplierReturnQuantityRulesGoldenAccepted)
    {
        ArgumentNullException.ThrowIfNull(line);
        var perPiece = line.IsPieceLine && !line.IsKilogramLine;
        if (!line.IsTurkishLira || !line.PricedPerOrderUnit || line.UnitPrice is not { } gross || gross <= 0m
            || !(line.IsKilogramLine || perPiece && quantityRulesAccepted))
            return new(PurchaseSupplierReturnQuantityPriceOutcomeV1.NotAKilogramTryLine, null);
        if (line.HasDocumentDiscount && quantityRulesAccepted)
            return new(PurchaseSupplierReturnQuantityPriceOutcomeV1.DocumentDiscount, null);
        decimal price;
        try { price = CommercialLinePriceCalculator.NetUnitPrice(gross, line.DiscountPercent, line.DiscountAmount); }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or InvalidOperationException)
        { return new(PurchaseSupplierReturnQuantityPriceOutcomeV1.NotRepresentable, null); }
        // A fully discounted line has no supplier price to bill a return at.
        if (price <= 0m) return new(PurchaseSupplierReturnQuantityPriceOutcomeV1.NotAKilogramTryLine, null);
        if (decimal.Round(price, UnitPriceScale) != price)
        {
            if (!quantityRulesAccepted) return new(PurchaseSupplierReturnQuantityPriceOutcomeV1.NotRepresentable, null);
            // SAT-D453: four decimals, half away from zero; the return amount uses the rounded price.
            price = decimal.Round(price, UnitPriceScale, MidpointRounding.AwayFromZero);
        }

        if (price <= 0m || price >= UnitCostUpperBound) return new(PurchaseSupplierReturnQuantityPriceOutcomeV1.NotRepresentable, null);
        return new(PurchaseSupplierReturnQuantityPriceOutcomeV1.Priced, price, perPiece);
    }
}
