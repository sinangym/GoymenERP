using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// FNS-D92 (STK-SC36-a): a foreign-currency receipt that cannot be linked to a purchase order is converted with the TCMB
/// forex selling rate of the stock-entry date. Without that rate the TL cost is "hesaplanamadı" with a warning code and the
/// position stays unreadable; no estimated or previous-day rate is used. Codes and numbers are pinned. SENTETIK / NOT_ENV.
/// </summary>
public sealed class InventoryUnlinkedReceiptFxRateResolverTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Usd = Guid.Parse("66666666-6666-4666-8666-666666666661");
    private static readonly Guid Eur = Guid.Parse("66666666-6666-4666-8666-666666666662");
    private static readonly Guid Lira = SettingsCurrencyIdentitiesV1.TurkishLira;
    private static readonly DateOnly EntryDay = new(2026, 11, 10);

    [Fact]
    public void Unlinked_receipt_codes_are_pinned()
    {
        Assert.Equal(
            ["stock_entry_rate_not_read", "stock_entry_rate_not_found", "stock_entry_rate_mismatch", "stock_entry_rate_not_representable"],
            InventoryUnlinkedReceiptFxRateUnavailableCodesV1.All);
    }

    [Fact]
    public void A_lira_cost_needs_no_rate()
    {
        var result = InventoryUnlinkedReceiptFxRateResolverV1.Resolve(CompanyA, EntryDay, Lira, 80m, stockEntryDayRate: null);

        Assert.Equal((InventoryAverageCostFxRateStatusV1.BaseCurrency, (decimal?)80m, (string?)null),
            (result.Status, result.BaseUnitCostPerStockKg, result.UnavailableCode));
    }

    [Fact]
    public void A_foreign_cost_converts_exactly_with_the_TCMB_selling_rate_of_the_stock_entry_date()
    {
        var result = InventoryUnlinkedReceiptFxRateResolverV1.Resolve(CompanyA, EntryDay, Usd, 1.23m, Resolved(Usd, "34.56789"));

        Assert.Equal(InventoryAverageCostFxRateStatusV1.Converted, result.Status);
        Assert.Equal(42.5185047m, result.BaseUnitCostPerStockKg);
        Assert.Equal(ExchangeRate.Of(Usd, Lira, 34.56789m), result.Rate);
        Assert.Null(result.UnavailableCode);

        var position = AverageCostCalculator.Apply(null, result.ToPurchaseReceiptMovement(100m, 0m));
        Assert.Equal((AverageCostStatus.Known, (decimal?)42.5185m), (position.Status, position.AverageCostPerKg));
    }

    [Fact]
    public void Without_a_rate_for_the_stock_entry_date_the_cost_is_not_calculated_and_no_estimate_is_used()
    {
        var notFound = InventoryUnlinkedReceiptFxRateResolverV1.Resolve(CompanyA, EntryDay, Usd, 1.23m, Unresolved(Usd));
        Assert.Equal((InventoryAverageCostFxRateStatusV1.Unavailable, (decimal?)null, (ExchangeRate?)null,
                InventoryUnlinkedReceiptFxRateUnavailableCodesV1.RateNotFound),
            (notFound.Status, notFound.BaseUnitCostPerStockKg, notFound.Rate, notFound.UnavailableCode));

        var notRead = InventoryUnlinkedReceiptFxRateResolverV1.Resolve(CompanyA, EntryDay, Usd, 1.23m, null);
        Assert.Equal(InventoryUnlinkedReceiptFxRateUnavailableCodesV1.RateNotRead, notRead.UnavailableCode);

        // The unavailable TL cost keeps the position unreadable (sticky foreign currency), never a guessed average.
        var position = AverageCostCalculator.Apply(null, notFound.ToPurchaseReceiptMovement(100m, 0m));
        Assert.Equal((AverageCostStatus.Unreadable, AverageCostUnreadableReason.ForeignCurrency),
            (position.Status, position.UnreadableReason));
    }

    [Fact]
    public void A_rate_of_another_day_currency_or_source_or_an_unusable_value_is_a_mismatch()
    {
        string?[] cases = ["mismatch-day", "mismatch-currency", "mismatch-source", "0", "-1", "abc", null];
        foreach (var item in cases)
        {
            var answer = item switch
            {
                "mismatch-day" => Resolved(Usd, "34.5") with { RateDay = EntryDay.AddDays(-1) },
                "mismatch-currency" => Resolved(Eur, "34.5"),
                "mismatch-source" => Resolved(Usd, "34.5") with { Source = "Manual" },
                _ => Resolved(Usd, item),
            };
            var result = InventoryUnlinkedReceiptFxRateResolverV1.Resolve(CompanyA, EntryDay, Usd, 1.23m, answer);
            Assert.Equal((InventoryAverageCostFxRateStatusV1.Unavailable, InventoryUnlinkedReceiptFxRateUnavailableCodesV1.RateMismatch),
                (result.Status, result.UnavailableCode));
        }
    }

    [Fact]
    public void Empty_identities_and_a_negative_cost_are_caller_errors()
    {
        Assert.Throws<ArgumentException>(() =>
            InventoryUnlinkedReceiptFxRateResolverV1.Resolve(Guid.Empty, EntryDay, Usd, 1m, null));
        Assert.Throws<ArgumentException>(() =>
            InventoryUnlinkedReceiptFxRateResolverV1.Resolve(CompanyA, EntryDay, Guid.Empty, 1m, null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InventoryUnlinkedReceiptFxRateResolverV1.Resolve(CompanyA, EntryDay, Usd, -1m, null));
    }

    private static ExchangeRateResolutionV1 Resolved(Guid source, string? sellingRate) =>
        new(EntryDay, source, Lira, "USD", ExchangeRateResolutionStatesV1.Resolved, ExchangeRateSourcesV1.Tcmb, false, null,
            EntryDay, sellingRate, ExchangeRateProvenancesV1.Official, null, null, null, null);

    private static ExchangeRateResolutionV1 Unresolved(Guid source) =>
        new(EntryDay, source, Lira, "USD", ExchangeRateResolutionStatesV1.Unavailable, ExchangeRateSourcesV1.Tcmb, false, null,
            null, null, null, null, null, "exchange_rate_not_found", EntryDay);
}
