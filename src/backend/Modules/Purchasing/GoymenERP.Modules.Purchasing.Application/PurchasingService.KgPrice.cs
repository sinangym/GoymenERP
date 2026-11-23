using System.Globalization;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchasingService
{
    /// <summary>AK-5 switch: <see cref="PurchaseOrderKgPriceViews.PurchaseKgPriceGoldenAccepted"/>.</summary>
    private static bool PurchaseKgPriceGoldenAccepted => PurchaseOrderKgPriceViews.PurchaseKgPriceGoldenAccepted;

    private static PurchaseKgPriceView? LineKgPriceView(StoredPurchaseOrder stored, PurchaseOrderLine line) =>
        PurchaseOrderKgPriceViews.Line(stored.Order, line, stored.InvoiceShares);

    private static PurchaseKgPriceView DocumentKgPriceView(StoredPurchaseOrder stored) =>
        PurchaseOrderKgPriceViews.Document(stored.Order, stored.InvoiceShares);

    /// <summary>
    /// AK-3b (SAT-D436 TK6): the order READ adds the receipt shares of the order's finalised invoices so the realization can be
    /// shown. The read is skipped while the indicator is off or the prices are withheld (PRS-02); an invoice history that cannot be
    /// read leaves the realization out (shown as "not read"), it never fails the order read and never shows a partial figure.
    /// </summary>
    private async Task<StoredPurchaseOrder> WithInvoiceSharesAsync(Scope scope, StoredPurchaseOrder stored, bool pricesVisible,
        CancellationToken cancellationToken)
    {
        if (!PurchaseKgPriceGoldenAccepted || !pricesVisible) return stored;
        try
        {
            var shares = await GuardedAsync(scope, bindings.ReadPermission,
                () => store.ReadOrderInvoiceSharesAsync(stored.Order.Id, cancellationToken), cancellationToken);
            return shares is null ? stored : stored with { InvoiceShares = shares };
        }
        catch (PurchasingServiceException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not UnauthorizedAccessException)
        {
            await RequireStillAsync(scope, bindings.ReadPermission, cancellationToken);
            return stored;
        }
    }
}

/// <summary>AK-5 / AK-3b (SAT-D436, SAT-D439): the purchase order read's kg price indicator and realization views.</summary>
public static class PurchaseOrderKgPriceViews
{
    /// <summary>
    /// AK-5 (SAT-D436, SAT-D439) switch. The independent golden (<c>docs/decisions/ADET_KG_FORMUL_GOLDEN_V1.md</c> §1, rows K01–K19)
    /// travels with this train (SAT-D265 (5), SAT-D431), so the indicator is written on the purchase order and invoice reads.
    /// </summary>
    public static bool PurchaseKgPriceGoldenAccepted => true;

    /// <summary>
    /// A general line's kg price: theoretical (line amount ÷ theoretical kg) and, when the order's invoice shares were read, real
    /// (invoiced amount ÷ weighed kg, TK6). A technical slice has none (it is priced on its general line).
    /// </summary>
    public static PurchaseKgPriceView? Line(PurchaseOrder order, PurchaseOrderLine line, IReadOnlyList<PurchaseOrderInvoiceShare>? shares = null)
    {
        if (line.Kind != PurchaseOrderLineKind.General) return null;
        var input = KgPriceInput(line, order.Money);
        return View(PurchaseKgPrice.Line(input.Amount, input.Kilograms, PurchaseKgPriceUnavailableCodes.TheoreticalKgUnavailable),
            shares is null || order.Money is not { } money ? null : PurchaseOrderRealizations.Line(order, line, shares, money.Scale).RealKgPrice);
    }

    /// <summary>The order's kg price over its general lines (Σ amount ÷ Σ kg, TK4; N2 interim rule, K17/K18).</summary>
    public static PurchaseKgPriceView Document(PurchaseOrder order, IReadOnlyList<PurchaseOrderInvoiceShare>? shares = null) => View(
        PurchaseKgPrice.Document([.. order.Lines.Where(line => line.Kind == PurchaseOrderLineKind.General).Select(line => KgPriceInput(line, order.Money))]),
        shares is null || order.Money is not { } money ? null
            : PurchaseOrderRealizations.Document(shares, money.Scale, EveryLineIsAPlainGoodsLine(order)).RealKgPrice);

    /// <summary>AK-3b: a general line's realization, or null when the shares were not read or the line is a technical slice.</summary>
    public static PurchaseOrderRealizationView? LineRealization(PurchaseOrder order, PurchaseOrderLine line,
        IReadOnlyList<PurchaseOrderInvoiceShare>? shares)
    {
        if (shares is null || line.Kind != PurchaseOrderLineKind.General || order.Money is not { } money) return null;
        return View(PurchaseOrderRealizations.Line(order, line, shares, money.Scale), money.Scale);
    }

    /// <summary>AK-3b: the order's realization, or null when the shares were not read.</summary>
    public static PurchaseOrderRealizationView? DocumentRealization(PurchaseOrder order, IReadOnlyList<PurchaseOrderInvoiceShare>? shares)
    {
        if (shares is null || order.Money is not { } money) return null;
        return View(PurchaseOrderRealizations.Document(shares, money.Scale, EveryLineIsAPlainGoodsLine(order)), money.Scale);
    }

    /// <summary>
    /// N2 interim rule (K17/K18, fail-closed): the document level is shown only while every general line is a priced goods line with
    /// its kg and without a maturity surcharge (vade farkı). Purchase orders carry no document-level discount, service or return line.
    /// </summary>
    private static bool EveryLineIsAPlainGoodsLine(PurchaseOrder order)
    {
        var general = order.Lines.Where(line => line.Kind == PurchaseOrderLineKind.General).ToArray();
        return general.Length > 0 && general.All(line =>
        {
            var input = KgPriceInput(line, order.Money);
            return input.IsGoodsLine && input.Amount is not null && input.Kilograms is not null;
        });
    }

    /// <summary>
    /// TK1 amount (the line's KDV-exclusive net amount after its discount) and TK2 theoretical kg (pieces × recorded k, or kg). A
    /// line with a maturity surcharge is not a plain goods line for the document level (K18, N2 open); its own line level is shown.
    /// </summary>
    private static PurchaseKgPriceLine KgPriceInput(PurchaseOrderLine line, PurchaseOrderMoney? money)
    {
        decimal? amount = line.Price is { } price && money is not null ? price.LineAmount(line.Quantity.Value, money) : null;
        decimal? kilograms = line.Quantity.Unit == QuantityUnit.Kilogram ? line.Quantity.Value
            : line.TheoreticalKilograms is { } snapshot
                ? PurchasePieceKilograms.TheoreticalKilograms(line.Quantity.Value, snapshot.KilogramsPerUnit)
                : null;
        return new PurchaseKgPriceLine(amount, kilograms, IsGoodsLine: line.Price is not { HasDeferral: true });
    }

    private static PurchaseKgPriceView View(PurchaseKgPriceResult theoretical, PurchaseKgPriceResult? real) => new(
        KgPriceText(theoretical.Value), theoretical.UnavailableCode,
        KgPriceText(real?.Value), real is null ? PurchaseKgPriceUnavailableCodes.RealizationNotAvailable : real.UnavailableCode);

    private static PurchaseOrderRealizationView View(PurchaseOrderRealization realization, int moneyScale) => new(
        Quantity(realization.InvoicedQuantity),
        realization.InvoicedAmount?.ToString("F" + moneyScale, CultureInfo.InvariantCulture),
        Quantity(realization.WeighedKg), Quantity(realization.TheoreticalKg), Quantity(realization.DifferenceKg),
        realization.DeviationPercent?.ToString("F" + PurchaseOrderRealizations.PercentScale, CultureInfo.InvariantCulture),
        KgPriceText(realization.RealKgPrice.Value), realization.RealKgPrice.UnavailableCode);

    internal static string? KgPriceText(decimal? value) => value?.ToString("F" + PurchaseKgPrice.Scale, CultureInfo.InvariantCulture);

    private static string? Quantity(decimal? value) =>
        value is { } known ? PurchaseAcceptanceParticipantContractV1.FormatQuantity(known) : null;
}
