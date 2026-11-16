using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// Stable "hesaplanamadı" codes of <see cref="InventoryUnlinkedReceiptFxRateResolverV1"/> (FNS-D92). Each is a warning on
/// the receipt's TL cost, never an estimated rate.
/// </summary>
public static class InventoryUnlinkedReceiptFxRateUnavailableCodesV1
{
    /// <summary>The caller did not read the stock-entry day's rate (a wiring fault; fail-closed).</summary>
    public const string RateNotRead = "stock_entry_rate_not_read";

    /// <summary>The Settings rate record has no TCMB forex selling rate for the stock-entry day ("kur bulunamadı").</summary>
    public const string RateNotFound = "stock_entry_rate_not_found";

    /// <summary>The answered rate is not the asked day, currency pair or source, or its value is not a positive number.</summary>
    public const string RateMismatch = "stock_entry_rate_mismatch";

    /// <summary>The exact TL value does not fit a decimal.</summary>
    public const string NotRepresentable = "stock_entry_rate_not_representable";

    public static IReadOnlyList<string> All { get; } = [RateNotRead, RateNotFound, RateMismatch, NotRepresentable];
}

/// <summary>
/// FNS-D92: a foreign-currency goods receipt that cannot be linked to a purchase order (so neither the supplier invoice's
/// official rate of BS-1 / SAT-D295 nor the order's rate applies) is converted to the TL cost currency with the TCMB forex
/// selling rate of the stock-entry date, read from the Settings rate record (<see cref="ISettingsExchangeRateResolverV1"/>,
/// DEC-FIN-010: TCMB is the only admitted source). When no such rate is found the TL cost is "hesaplanamadı" with a warning
/// code; no estimated, previous-day or provisional rate is ever used. A lira cost needs no rate. Pure and deterministic:
/// the caller reads the rate (its own use case authorizes the read) and passes the answer in; nothing is written here.
/// </summary>
public static class InventoryUnlinkedReceiptFxRateResolverV1
{
    public static InventoryAverageCostFxRateResultV1 Resolve(Guid companyId, DateOnly stockEntryDate, Guid costCurrencyId,
        decimal unitCostPerStockKg, ExchangeRateResolutionV1? stockEntryDayRate)
    {
        if (companyId == Guid.Empty || costCurrencyId == Guid.Empty)
        {
            throw new ArgumentException("Şirket ve maliyet para birimi kimlikleri boş olamaz.", nameof(costCurrencyId));
        }

        if (unitCostPerStockKg < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(unitCostPerStockKg), "Birim maliyet negatif olamaz.");
        }

        var lira = InventoryAverageCostFxRateResolverV1.CostCurrencyId;
        if (costCurrencyId == lira)
        {
            return new(InventoryAverageCostFxRateStatusV1.BaseCurrency, costCurrencyId, unitCostPerStockKg, unitCostPerStockKg,
                null, null);
        }

        if (stockEntryDayRate is null)
        {
            return Unavailable(costCurrencyId, unitCostPerStockKg, InventoryUnlinkedReceiptFxRateUnavailableCodesV1.RateNotRead);
        }

        if (stockEntryDayRate.RateDay != stockEntryDate
            || stockEntryDayRate.SourceCurrencyId != costCurrencyId
            || stockEntryDayRate.TargetCurrencyId != lira)
        {
            return Unavailable(costCurrencyId, unitCostPerStockKg, InventoryUnlinkedReceiptFxRateUnavailableCodesV1.RateMismatch);
        }

        if (!string.Equals(stockEntryDayRate.State, ExchangeRateResolutionStatesV1.Resolved, StringComparison.Ordinal))
        {
            return Unavailable(costCurrencyId, unitCostPerStockKg, InventoryUnlinkedReceiptFxRateUnavailableCodesV1.RateNotFound);
        }

        if (!string.Equals(stockEntryDayRate.Source, ExchangeRateSourcesV1.Tcmb, StringComparison.Ordinal)
            || !decimal.TryParse(stockEntryDayRate.SellingRate, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out var selling)
            || selling <= 0m)
        {
            return Unavailable(costCurrencyId, unitCostPerStockKg, InventoryUnlinkedReceiptFxRateUnavailableCodesV1.RateMismatch);
        }

        var rate = ExchangeRate.Of(costCurrencyId, lira, selling);
        decimal baseUnitCost;
        try
        {
            // The scale only carries the given amount exactly (its own significant decimals); it is not a money profile choice.
            var source = Money.Of(unitCostPerStockKg, costCurrencyId, Money.SignificantDecimals(unitCostPerStockKg));
            baseUnitCost = rate.ConvertExact(source);
        }
        catch (OverflowException)
        {
            return Unavailable(costCurrencyId, unitCostPerStockKg, InventoryUnlinkedReceiptFxRateUnavailableCodesV1.NotRepresentable);
        }

        return new(InventoryAverageCostFxRateStatusV1.Converted, costCurrencyId, unitCostPerStockKg, baseUnitCost, rate, null);
    }

    private static InventoryAverageCostFxRateResultV1 Unavailable(Guid costCurrencyId, decimal unitCostPerStockKg, string code) =>
        new(InventoryAverageCostFxRateStatusV1.Unavailable, costCurrencyId, unitCostPerStockKg, null, null, code);
}
