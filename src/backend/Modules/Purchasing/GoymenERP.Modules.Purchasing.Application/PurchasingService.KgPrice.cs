using System.Globalization;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchasingService
{
    /// <summary>AK-5 switch: <see cref="PurchaseOrderKgPriceViews.PurchaseKgPriceGoldenAccepted"/>.</summary>
    private static bool PurchaseKgPriceGoldenAccepted => PurchaseOrderKgPriceViews.PurchaseKgPriceGoldenAccepted;

    private static PurchaseKgPriceView? LineKgPriceView(PurchaseOrderLine line, PurchaseOrderMoney? money) =>
        PurchaseOrderKgPriceViews.Line(line, money);

    private static PurchaseKgPriceView DocumentKgPriceView(PurchaseOrder order) => PurchaseOrderKgPriceViews.Document(order);
}

/// <summary>AK-5 (SAT-D436, SAT-D439): the purchase order read's kg price indicator views.</summary>
public static class PurchaseOrderKgPriceViews
{
    /// <summary>
    /// AK-5 (SAT-D436, SAT-D439) switch. The independent golden (<c>docs/decisions/ADET_KG_FORMUL_GOLDEN_V1.md</c> §1, rows K01–K19)
    /// is not in main yet (SAT-D265 (5), SAT-D431): until it is, no kg price is written on a purchase order read.
    /// </summary>
    public static bool PurchaseKgPriceGoldenAccepted => false;

    /// <summary>AK-5: a general line's theoretical kg price; a technical slice has none (it is priced on its general line).</summary>
    public static PurchaseKgPriceView? Line(PurchaseOrderLine line, PurchaseOrderMoney? money)
    {
        if (line.Kind != PurchaseOrderLineKind.General) return null;
        var input = KgPriceInput(line, money);
        return View(PurchaseKgPrice.Line(input.Amount, input.Kilograms, PurchaseKgPriceUnavailableCodes.TheoreticalKgUnavailable));
    }

    /// <summary>AK-5: the order's theoretical kg price over its general lines (Σ amount ÷ Σ kg, TK4; N2 interim rule).</summary>
    public static PurchaseKgPriceView Document(PurchaseOrder order) => View(PurchaseKgPrice.Document(
        [.. order.Lines.Where(line => line.Kind == PurchaseOrderLineKind.General).Select(line => KgPriceInput(line, order.Money))]));

    /// <summary>TK1 amount (the line's KDV-exclusive net amount after its discount) and TK2 theoretical kg (pieces × recorded k, or kg).</summary>
    private static PurchaseKgPriceLine KgPriceInput(PurchaseOrderLine line, PurchaseOrderMoney? money)
    {
        decimal? amount = line.Price is { } price && money is not null ? price.LineAmount(line.Quantity.Value, money) : null;
        decimal? kilograms = line.Quantity.Unit == QuantityUnit.Kilogram ? line.Quantity.Value
            : line.TheoreticalKilograms is { } snapshot
                ? PurchasePieceKilograms.TheoreticalKilograms(line.Quantity.Value, snapshot.KilogramsPerUnit)
                : null;
        return new PurchaseKgPriceLine(amount, kilograms);
    }

    private static PurchaseKgPriceView View(PurchaseKgPriceResult theoretical) => new(
        theoretical.Value?.ToString("F" + PurchaseKgPrice.Scale, CultureInfo.InvariantCulture), theoretical.UnavailableCode,
        null, PurchaseKgPriceUnavailableCodes.RealizationNotAvailable);
}
