using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Modules.Purchasing.Domain.Invoices;

/// <summary>
/// AK-3b (SAT-D436 TK6): which supplier invoices count toward an order's realization (invoiced pieces, amount, real kg and real kg
/// price). This is the single named place of that rule.
/// <para>
/// NEEDS-SINAN (asked on Issue #1189): until Sinan answers, only a FINALISED invoice counts — one that is <c>FinancePending</c> or
/// <c>FinanceAccepted</c>. A recorded or matched invoice that is not finalised yet does not. The incoming invoice has no cancelled
/// or rejected state today; when one is added it must be excluded here.
/// </para>
/// </summary>
public static class PurchaseInvoiceRealizationRule
{
    public static bool CountsTowardRealization(PurchaseIncomingInvoiceStatus status) =>
        status is PurchaseIncomingInvoiceStatus.FinancePending or PurchaseIncomingInvoiceStatus.FinanceAccepted;
}

/// <summary>
/// AK-3b: the kilograms of ONE goods receipt share of an invoice, exact (golden <c>ADET_KG_FORMUL_GOLDEN_V1</c> §1 TK2/TK6, §2 step 4).
/// </summary>
public static class PurchaseInvoiceKilograms
{
    /// <summary>
    /// ALS-D121 (golden A09, A13): the weighed kilograms that fall to the invoiced pieces — kilograms weighed at receipt × invoiced
    /// pieces ÷ accepted pieces, exact. A kilogram line's share is its matched quantity. Null when the share has no kg facts or
    /// the acceptance was not weighed (K14) — never the theoretical kg.
    /// </summary>
    /// <remarks>
    /// ALS-D122/D123 (<paramref name="remainingShareAccepted"/>, default <see cref="PurchasePieceKilograms.RemainingShareGoldenAccepted"/>):
    /// a piece share matched after a return or reversal of its acceptance has no real kg here (the returned kilograms are
    /// Inventory's); the cost itself is applied by Inventory with the current remaining share.
    /// </remarks>
    public static PurchaseExactRatio? Weighed(PurchaseInvoiceReceiptMatch match, bool? remainingShareAccepted = null)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (match.Kilograms is not { } kilograms) return null;
        if (kilograms.UnitIsKilogram) return PurchaseExactRatio.Of(match.Quantity);
        if ((remainingShareAccepted ?? PurchasePieceKilograms.RemainingShareGoldenAccepted) && match.ReceiptAdjustments is { Count: > 0 }) return null;
        if (kilograms.AcceptanceMeasuredKg is not { } measured || match.AcceptedQuantity is not { } accepted || accepted <= 0m) return null;
        return PurchaseExactRatio.Of(measured).Multiply(match.Quantity).Divide(accepted);
    }

    /// <summary>
    /// SAT-D437 / TK6: the theoretical kilograms of the invoiced quantity — invoiced pieces × the kg per piece recorded on the order
    /// line (URN-D9, three decimals); a kilogram line's is its matched quantity. Null without a recorded kg per piece (K13).
    /// </summary>
    public static decimal? Theoretical(PurchaseInvoiceReceiptMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (match.Kilograms is not { } kilograms) return null;
        if (kilograms.UnitIsKilogram) return match.Quantity;
        return kilograms.TheoreticalKilogramsPerUnit is { } perUnit && decimal.Truncate(match.Quantity) == match.Quantity
            ? PurchasePieceKilograms.TheoreticalKilograms(match.Quantity, perUnit)
            : null;
    }
}

/// <summary>One invoice line's kg price inputs: its official net amount and the kilograms of its receipt shares.</summary>
public sealed record PurchaseInvoiceLineKilograms(Guid InvoiceLineId, bool IsGoodsLine, decimal Net, decimal? TheoreticalKg,
    PurchaseExactRatio? WeighedKg);

/// <summary>
/// AK-3b (SAT-D436, SAT-D439): the kg price indicator of a supplier invoice, derived from the invoice ↔ receipt match alone. The
/// amount is the official KDV-exclusive line net (TK1) in the invoice's currency (no rate, SAT-D439). A line whose matched
/// quantity differs from its invoiced quantity (an approved quantity variance) has no kg for its amount, so it shows none.
/// </summary>
public static class PurchaseInvoiceKgPrice
{
    public static IReadOnlyList<PurchaseInvoiceLineKilograms> Lines(PurchaseIncomingInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return [.. invoice.Lines.Select(line =>
        {
            var goods = line.LinePurpose is "production-input" or "resale";
            var shares = invoice.Matches.Where(match => match.InvoiceLineId == line.Id).ToArray();
            if (!goods || shares.Length == 0 || shares.Sum(share => share.Quantity) != line.Quantity)
                return new PurchaseInvoiceLineKilograms(line.Id, goods, line.Amounts.Net, null, null);
            var theoretical = shares.Select(PurchaseInvoiceKilograms.Theoretical).ToArray();
            var weighed = shares.Select(share => PurchaseInvoiceKilograms.Weighed(share)).ToArray();
            return new PurchaseInvoiceLineKilograms(line.Id, goods, line.Amounts.Net,
                theoretical.Any(value => value is null) ? null : theoretical.Sum(value => value!.Value),
                weighed.Any(value => value is null) ? null
                    : weighed.Aggregate(PurchaseExactRatio.Zero, (sum, value) => sum.Add(value!.Value)));
        })];
    }

    public static PurchaseKgPriceResult Theoretical(PurchaseInvoiceLineKilograms line) =>
        PurchaseKgPrice.Line(line.Net, line.TheoreticalKg, PurchaseKgPriceUnavailableCodes.TheoreticalKgUnavailable);

    public static PurchaseKgPriceResult Real(PurchaseInvoiceLineKilograms line) =>
        PurchaseKgPrice.Exact(PurchaseExactRatio.Of(line.Net), line.WeighedKg, PurchaseKgPriceUnavailableCodes.WeighedKgUnavailable);

    /// <summary>
    /// The document level (TK4/TK5, K17/K18): Σ net ÷ Σ kg, shown only while EVERY line is a goods line with that kg. A service or
    /// expense line (kg'siz hizmet, masraf) hides it — N2 is open, so nothing is computed for such a document.
    /// </summary>
    public static (PurchaseKgPriceResult Theoretical, PurchaseKgPriceResult Real) Document(IReadOnlyList<PurchaseInvoiceLineKilograms> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var hidden = PurchaseKgPriceResult.Unavailable(PurchaseKgPriceUnavailableCodes.NotAllLinesHaveKg);
        if (lines.Count == 0 || lines.Any(line => !line.IsGoodsLine)) return (hidden, hidden);
        var net = lines.Aggregate(PurchaseExactRatio.Zero, (sum, line) => sum.Add(PurchaseExactRatio.Of(line.Net)));
        var theoretical = lines.Any(line => line.TheoreticalKg is null) ? hidden
            : PurchaseKgPrice.Exact(net, PurchaseExactRatio.Of(lines.Sum(line => line.TheoreticalKg!.Value)), PurchaseKgPriceUnavailableCodes.NotAllLinesHaveKg);
        var real = lines.Any(line => line.WeighedKg is null) ? hidden
            : PurchaseKgPrice.Exact(net, lines.Aggregate(PurchaseExactRatio.Zero, (sum, line) => sum.Add(line.WeighedKg!.Value)),
                PurchaseKgPriceUnavailableCodes.NotAllLinesHaveKg);
        return (theoretical, real);
    }
}
