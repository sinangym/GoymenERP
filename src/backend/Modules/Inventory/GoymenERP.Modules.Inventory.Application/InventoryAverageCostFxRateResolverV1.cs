using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>What <see cref="InventoryAverageCostFxRateResolverV1"/> answers for one purchase receipt's unit cost.</summary>
public enum InventoryAverageCostFxRateStatusV1
{
    /// <summary>The cost is already in the cost currency: no rate is needed and none was read.</summary>
    BaseCurrency = 1,

    /// <summary>The foreign cost was converted with the supplier invoice's official rate (BS-1), exactly, before any rounding.</summary>
    Converted = 2,

    /// <summary>The cost-currency value cannot be known yet (BS-1: no provisional rate, no correction movement).</summary>
    Unavailable = 3,
}

/// <summary>Stable codes for <see cref="InventoryAverageCostFxRateStatusV1.Unavailable"/>.</summary>
public static class InventoryAverageCostFxRateUnavailableCodesV1
{
    /// <summary>BS-1: the supplier invoice is not recorded yet, so its official rate is not known.</summary>
    public const string InvoiceNotRecorded = InventoryPurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded;

    /// <summary>The invoice rate source failed or answered something that cannot be trusted.</summary>
    public const string RateUnreadable = InventoryPurchaseInvoiceCostRateReasonsV1.Unreadable;

    /// <summary>A foreign cost was given without an invoice rate answer (the source was not read).</summary>
    public const string RateNotRead = "purchase_invoice_rate_not_read";

    /// <summary>The invoice rate source does not know the goods receipt line.</summary>
    public const string ReceiptNotFound = "purchase_receipt_not_found";

    /// <summary>
    /// The answer belongs to another receipt or company, or its rate is not from the cost's currency into the cost currency.
    /// </summary>
    public const string RateMismatch = "purchase_invoice_rate_mismatch";

    /// <summary>The exact converted unit cost is not representable; it is refused, never silently rounded.</summary>
    public const string NotRepresentable = "purchase_invoice_rate_not_representable";

    public static IReadOnlyList<string> All { get; } =
        [InvoiceNotRecorded, RateUnreadable, RateNotRead, ReceiptNotFound, RateMismatch, NotRepresentable];
}

/// <summary>
/// One purchase receipt's unit cost in both currencies (SAT-D295 (2): the foreign value and its cost-currency equivalent are
/// held together). <see cref="SourceCurrencyId"/>/<see cref="SourceUnitCostPerStockKg"/> are what the caller gave;
/// <see cref="BaseUnitCostPerStockKg"/> is the cost-currency value, null while <see cref="InventoryAverageCostFxRateStatusV1.Unavailable"/>.
/// <see cref="Rate"/> is the invoice rate that converted it (only for <see cref="InventoryAverageCostFxRateStatusV1.Converted"/>).
/// </summary>
public sealed record InventoryAverageCostFxRateResultV1(
    InventoryAverageCostFxRateStatusV1 Status,
    Guid SourceCurrencyId,
    decimal SourceUnitCostPerStockKg,
    decimal? BaseUnitCostPerStockKg,
    ExchangeRate? Rate,
    string? UnavailableCode)
{
    /// <summary>
    /// The purchase receipt movement the average cost core takes. A known cost-currency value enters as a base-currency cost; an
    /// unavailable one enters as a foreign-currency cost, which the core answers with the sticky
    /// <see cref="AverageCostUnreadableReason.ForeignCurrency"/> (BS-1: the TL cost is Unavailable until the invoice). Additional
    /// cost, when the caller has one, is set on the returned record; it never enters the v1 average (SAT-D302 (1)).
    /// </summary>
    public AverageCostMovement ToPurchaseReceiptMovement(decimal quantityKg, decimal stockOnHandBeforeKg) =>
        ToMovement(AverageCostMovementKind.PurchaseReceipt, quantityKg, stockOnHandBeforeKg);

    /// <summary>
    /// SAT-D306 (b): a purchase return leaves at the original purchase line's cost-currency unit cost; while that is Unavailable
    /// (the invoice is not recorded), the return's cost is Unavailable too and the core answers
    /// <see cref="AverageCostUnreadableReason.ForeignCurrency"/>.
    /// </summary>
    public AverageCostMovement ToPurchaseReturnMovement(decimal quantityKg, decimal stockOnHandBeforeKg) =>
        ToMovement(AverageCostMovementKind.PurchaseReturn, quantityKg, stockOnHandBeforeKg);

    private AverageCostMovement ToMovement(AverageCostMovementKind kind, decimal quantityKg, decimal stockOnHandBeforeKg) =>
        BaseUnitCostPerStockKg is { } baseCost
            ? new AverageCostMovement(kind, quantityKg, stockOnHandBeforeKg, baseCost)
            : new AverageCostMovement(kind, quantityKg, stockOnHandBeforeKg, SourceUnitCostPerStockKg, IsBaseCurrency: false);
}

/// <summary>
/// BS-1 / SAT-D295: the deterministic step that puts a purchase receipt's unit cost into the average cost's currency. The
/// average is a TL value (BS-1, <see cref="CostCurrencyId"/>). A cost already in TL needs no rate. A foreign cost converts only
/// with the supplier invoice's official rate; until the invoice is recorded the TL cost is
/// <see cref="InventoryAverageCostFxRateStatusV1.Unavailable"/>: no provisional rate (neither the order's nor the receipt's day
/// stands in), no correction movement later.
/// <para>
/// Pure: no clock, storage, company or role; the caller reads the invoice rate through
/// <see cref="IInventoryPurchaseInvoiceCostRateSourceV1"/> and hands it in. The caller also chooses which order-line price is the
/// unit cost and gives it per stock kilogram; this step neither picks a price nor converts units (a needed unit conversion stays
/// with its own rule, SAT-D298/D299). The conversion is exact (<see cref="ExchangeRate.ConvertExact"/>): the average core rounds
/// once, at the average, never here.
/// </para>
/// </summary>
public static class InventoryAverageCostFxRateResolverV1
{
    /// <summary>BS-1: the moving average is held in Turkish lira, the Settings-published currency identity.</summary>
    public static Guid CostCurrencyId => SettingsCurrencyIdentitiesV1.TurkishLira;

    /// <summary>
    /// Resolves the cost-currency unit cost of goods receipt line <paramref name="goodsReceiptLineId"/> of
    /// <paramref name="companyId"/>, whose unit cost per stock kilogram is <paramref name="unitCostPerStockKg"/> in
    /// <paramref name="costCurrencyId"/>. <paramref name="invoiceRate"/> is the invoice rate source's answer for that receipt;
    /// it is not consulted for a cost already in the cost currency and may be null then.
    /// </summary>
    public static InventoryAverageCostFxRateResultV1 Resolve(Guid companyId, Guid goodsReceiptLineId, Guid costCurrencyId,
        decimal unitCostPerStockKg, InventoryPurchaseInvoiceCostRateV1? invoiceRate)
    {
        if (companyId == Guid.Empty || goodsReceiptLineId == Guid.Empty || costCurrencyId == Guid.Empty)
        {
            throw new ArgumentException("Şirket, mal kabul satırı ve maliyet para birimi kimlikleri boş olamaz.", nameof(costCurrencyId));
        }

        if (unitCostPerStockKg < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(unitCostPerStockKg), "Birim maliyet negatif olamaz.");
        }

        if (costCurrencyId == CostCurrencyId)
        {
            return new(InventoryAverageCostFxRateStatusV1.BaseCurrency, costCurrencyId, unitCostPerStockKg, unitCostPerStockKg, null, null);
        }

        if (invoiceRate is null)
        {
            return Unavailable(costCurrencyId, unitCostPerStockKg, InventoryAverageCostFxRateUnavailableCodesV1.RateNotRead);
        }

        if (invoiceRate.CompanyId != companyId || invoiceRate.GoodsReceiptLineId != goodsReceiptLineId)
        {
            return Unavailable(costCurrencyId, unitCostPerStockKg, InventoryAverageCostFxRateUnavailableCodesV1.RateMismatch);
        }

        switch (invoiceRate.Status)
        {
            case InventoryPurchaseInvoiceCostRateStatusV1.NotFound:
                return Unavailable(costCurrencyId, unitCostPerStockKg, InventoryAverageCostFxRateUnavailableCodesV1.ReceiptNotFound);
            case InventoryPurchaseInvoiceCostRateStatusV1.Unavailable:
                return Unavailable(costCurrencyId, unitCostPerStockKg,
                    string.Equals(invoiceRate.UnavailableReason, InventoryPurchaseInvoiceCostRateReasonsV1.InvoiceNotRecorded, StringComparison.Ordinal)
                        ? InventoryAverageCostFxRateUnavailableCodesV1.InvoiceNotRecorded
                        : InventoryAverageCostFxRateUnavailableCodesV1.RateUnreadable);
            case InventoryPurchaseInvoiceCostRateStatusV1.Rate:
                break;
            default:
                return Unavailable(costCurrencyId, unitCostPerStockKg, InventoryAverageCostFxRateUnavailableCodesV1.RateUnreadable);
        }

        if (invoiceRate.Rate is not { } rate || invoiceRate.OrderCurrencyId != costCurrencyId
            || rate.SourceCurrencyId != costCurrencyId || rate.TargetCurrencyId != CostCurrencyId)
        {
            return Unavailable(costCurrencyId, unitCostPerStockKg, InventoryAverageCostFxRateUnavailableCodesV1.RateMismatch);
        }

        decimal baseUnitCost;
        try
        {
            // The scale only carries the given amount exactly (its own significant decimals); it is not a money profile choice.
            var source = Money.Of(unitCostPerStockKg, costCurrencyId, Money.SignificantDecimals(unitCostPerStockKg));
            baseUnitCost = rate.ConvertExact(source);
        }
        catch (OverflowException)
        {
            return Unavailable(costCurrencyId, unitCostPerStockKg, InventoryAverageCostFxRateUnavailableCodesV1.NotRepresentable);
        }

        return new(InventoryAverageCostFxRateStatusV1.Converted, costCurrencyId, unitCostPerStockKg, baseUnitCost, rate, null);
    }

    private static InventoryAverageCostFxRateResultV1 Unavailable(Guid costCurrencyId, decimal unitCostPerStockKg, string code) =>
        new(InventoryAverageCostFxRateStatusV1.Unavailable, costCurrencyId, unitCostPerStockKg, null, null, code);
}
