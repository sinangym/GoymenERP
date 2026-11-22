using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Modules.Purchasing.Domain.Orders;

/// <summary>
/// AK-3b (SAT-D436 TK6): one goods receipt share of a supplier invoice that counts toward an order's realization
/// (<see cref="PurchaseInvoiceRealizationRule"/>). <see cref="Amount"/> is the share of the official KDV-exclusive invoice line net
/// (TK1) that falls to this share — null when the line's matched quantity differs from its invoiced quantity (an approved quantity
/// variance: the amount cannot be apportioned without choosing a rule). The kilograms are the share's own (ALS-D121 / SAT-D437).
/// </summary>
public sealed record PurchaseOrderInvoiceShare(Guid InvoiceId, Guid InvoiceLineId, Guid OrderLineId, Guid AcceptanceId, decimal Quantity,
    PurchaseExactRatio? Amount, decimal? TheoreticalKg, PurchaseExactRatio? WeighedKg);

/// <summary>
/// The realization of one order line or of the whole order: what has been invoiced, the kilograms weighed for it, and how that
/// compares with the theoretical kilograms (TK8: difference = weighed − theoretical; deviation = difference ÷ theoretical × 100).
/// Every figure is rounded once for display; a figure that cannot be shown is null (never zero or an estimate).
/// </summary>
public sealed record PurchaseOrderRealization(decimal? InvoicedQuantity, decimal? InvoicedAmount, decimal? WeighedKg, decimal? TheoreticalKg,
    decimal? DifferenceKg, decimal? DeviationPercent, PurchaseKgPriceResult RealKgPrice);

/// <summary>
/// AK-3b (SAT-D436, SAT-D439; golden <c>ADET_KG_FORMUL_GOLDEN_V1</c> §1 K01–K04, K10, K14, TK4, TK6, TK8): an order's realization from
/// the receipt shares of its finalised invoices. Sums are exact; the real kg price is Σ invoiced amount ÷ Σ weighed kg rounded once
/// (never an average of invoice kg prices, K03). It is information only: no amount, cost or stock value is written from it.
/// </summary>
public static class PurchaseOrderRealizations
{
    /// <summary>The scale of a deviation percent (0,01; half away from zero).</summary>
    public const int PercentScale = 2;

    /// <summary>The goods receipt shares of <paramref name="invoice"/> on <paramref name="orderId"/>; empty unless the invoice counts.</summary>
    public static IReadOnlyList<PurchaseOrderInvoiceShare> SharesOf(PurchaseIncomingInvoice invoice, Guid orderId)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        if (!PurchaseInvoiceRealizationRule.CountsTowardRealization(invoice.Status)) return [];
        var shares = new List<PurchaseOrderInvoiceShare>();
        foreach (var line in invoice.Lines.Where(line => line.LinePurpose is "production-input" or "resale"))
        {
            var matches = invoice.Matches.Where(match => match.InvoiceLineId == line.Id).ToArray();
            var apportionable = matches.Length > 0 && matches.Sum(match => match.Quantity) == line.Quantity;
            foreach (var match in matches.Where(match => match.OrderId == orderId && match.ServiceTypeId is null))
            {
                shares.Add(new(invoice.Id, line.Id, match.OrderLineId, match.AcceptanceId, match.Quantity,
                    apportionable ? PurchaseExactRatio.Of(line.Amounts.Net).Multiply(match.Quantity).Divide(line.Quantity) : null,
                    PurchaseInvoiceKilograms.Theoretical(match), PurchaseInvoiceKilograms.Weighed(match)));
            }
        }

        return shares;
    }

    /// <summary>
    /// A general line's realization: its own shares and those of its technical slices (a slice is priced on its general line).
    /// </summary>
    public static PurchaseOrderRealization Line(PurchaseOrder order, PurchaseOrderLine line, IReadOnlyList<PurchaseOrderInvoiceShare> shares,
        int moneyScale)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(shares);
        var lineIds = order.Lines.Where(candidate => candidate.Id == line.Id || candidate.ParentLineId == line.Id)
            .Select(candidate => candidate.Id).ToHashSet();
        var own = shares.Where(share => lineIds.Contains(share.OrderLineId)).ToArray();
        return Of(own, own.Sum(share => share.Quantity), moneyScale, hideKgPrice: false);
    }

    /// <summary>
    /// The order's realization over every share. The document-level kg figures follow the N2 interim rule (K17/K18): they are shown
    /// only while <paramref name="everyLineIsAPlainGoodsLine"/> — otherwise the real kg price is hidden, the amount stays.
    /// </summary>
    public static PurchaseOrderRealization Document(IReadOnlyList<PurchaseOrderInvoiceShare> shares, int moneyScale,
        bool everyLineIsAPlainGoodsLine)
    {
        ArgumentNullException.ThrowIfNull(shares);
        return Of(shares, null, moneyScale, hideKgPrice: !everyLineIsAPlainGoodsLine);
    }

    private static PurchaseOrderRealization Of(IReadOnlyList<PurchaseOrderInvoiceShare> shares, decimal? quantity, int moneyScale, bool hideKgPrice)
    {
        if (shares.Count == 0)
        {
            // K01: nothing invoiced — the invoiced quantity and amount are a true zero; no kg figure and no real kg price.
            return new(quantity, 0m, null, null, null, null, PurchaseKgPriceResult.Unavailable(PurchaseKgPriceUnavailableCodes.NotInvoiced));
        }

        PurchaseExactRatio? amount = shares.Any(share => share.Amount is null) ? null
            : shares.Aggregate(PurchaseExactRatio.Zero, (sum, share) => sum.Add(share.Amount!.Value));
        PurchaseExactRatio? weighed = shares.Any(share => share.WeighedKg is null) ? null
            : shares.Aggregate(PurchaseExactRatio.Zero, (sum, share) => sum.Add(share.WeighedKg!.Value));
        decimal? theoretical = shares.Any(share => share.TheoreticalKg is null) ? null : shares.Sum(share => share.TheoreticalKg!.Value);
        var real = hideKgPrice
            ? PurchaseKgPriceResult.Unavailable(PurchaseKgPriceUnavailableCodes.NotAllLinesHaveKg)
            : PurchaseKgPrice.Exact(amount, weighed, PurchaseKgPriceUnavailableCodes.WeighedKgUnavailable);
        PurchaseExactRatio? difference = weighed is { } actual && theoretical is { } expected
            ? actual.Subtract(PurchaseExactRatio.Of(expected)) : null;
        return new(quantity, amount?.Round(moneyScale), weighed?.Round(PurchasePieceKilograms.KilogramScale), theoretical,
            difference?.Round(PurchasePieceKilograms.KilogramScale),
            difference is { } delta && theoretical is > 0m ? delta.Multiply(100m).Divide(theoretical.Value).Round(PercentScale) : null,
            real);
    }
}
