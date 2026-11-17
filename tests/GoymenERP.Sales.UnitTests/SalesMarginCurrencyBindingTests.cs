using GoymenERP.BuildingBlocks.Monetary;
using System.Globalization;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

public sealed class SalesMarginCurrencyBindingTests
{
    private static readonly Guid Unit = Guid.Parse("66666666-6666-4666-8666-666666666666");

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SAT_S12_restored_sale_and_cost_currencies_cannot_redefine_the_TL_margin(
        bool saleIsTl, bool costIsTl)
    {
        var tl = SettingsCurrencyIdentitiesV1.TurkishLira;
        var usd = SettingsCurrencyIdentitiesV1.UnitedStatesDollar;
        var snapshot = Snapshot(costIsTl ? tl : usd);
        snapshot.Validate(); // Legacy snapshots may be valid data even when the margin currency is unsupported.

        var result = SalesDraftService.CalculateMarginInTurkishLira(
            Money.Of(100m, saleIsTl ? tl : usd, 2), Unit, 1m, snapshot);

        Assert.Equal(SalesLineMarginState.Unavailable, result.State);
        Assert.Equal(SalesLineMarginUnavailableReason.CurrencyConversionUndecided, result.UnavailableReason);
        Assert.Null(result.Amount);
        Assert.Null(result.Percent);
    }

    [Fact]
    public void SAT_S12_actual_TL_binding_preserves_the_reviewed_margin()
    {
        var tl = SettingsCurrencyIdentitiesV1.TurkishLira;
        var result = SalesDraftService.CalculateMarginInTurkishLira(Money.Of(100m, tl, 2), Unit, 1m, Snapshot(tl));

        Assert.Equal(SalesLineMarginState.Computed, result.State);
        Assert.Equal(Money.Of(80m, tl, 2), result.Amount);
        Assert.Equal(80m, result.Percent);
    }

    // Independently reviewed PR1200 G01-G15 values; G11/G12 start with their once-rounded net sale.
    [Theory]
    [InlineData("1000.00", "7.1234", "100", "287.66", "28.77")]
    [InlineData("100.00", "120.5", "1", "-20.50", "-20.50")]
    [InlineData("0.00", "5", "2", "-10.00", null)]
    [InlineData("-50.00", "1", "1", "-51.00", null)]
    [InlineData("10.00", "4.995", "1", "5.01", "50.05")]
    [InlineData("10.00", "10.005", "1", "-0.01", "-0.05")]
    [InlineData("1000.00", "999.95", "1", "0.05", "0.01")]
    [InlineData("1000.00", "1000.05", "1", "-0.05", "-0.01")]
    [InlineData("10.00", "3.3335", "1", "6.67", "66.67")]
    [InlineData("10.00", "3.3325", "1", "6.67", "66.68")]
    [InlineData("900.00", "60", "10", "300.00", "33.33")]
    [InlineData("30.02", "10", "3", "0.02", "0.07")]
    [InlineData("1500.00", "12.3456", "2.5", "1469.14", "97.94")]
    [InlineData("300.00", "0.3333", "3", "299.00", "99.67")]
    [InlineData("100.00", "5.00010", "1", "95.00", "95.00")]
    public void SAT_S12_independent_reviewed_golden_reaches_the_same_application_currency_binding(
        string net, string cost, string quantity, string amount, string? percent)
    {
        var tl = SettingsCurrencyIdentitiesV1.TurkishLira;
        var q = decimal.Parse(quantity, CultureInfo.InvariantCulture);
        var snapshot = Snapshot(tl) with { Quantity = q, CostPerUnit = decimal.Parse(cost, CultureInfo.InvariantCulture) };
        var result = SalesDraftService.CalculateMarginInTurkishLira(
            Money.Of(decimal.Parse(net, CultureInfo.InvariantCulture), tl, 2), Unit, q, snapshot);

        Assert.Equal(SalesLineMarginState.Computed, result.State);
        Assert.Null(result.UnavailableReason);
        Assert.Equal(Money.Of(decimal.Parse(amount, CultureInfo.InvariantCulture), tl, 2), result.Amount);
        if (percent is null)
        {
            Assert.Null(result.Percent);
            Assert.Equal(SalesLineMarginUnavailableReason.NetSaleNotPositive, result.PercentUnavailableReason);
        }
        else Assert.Equal(decimal.Parse(percent, CultureInfo.InvariantCulture), result.Percent);
    }

    [Fact]
    public void SAT_S12_zero_cost_is_unavailable_in_the_actual_application_binding_even_after_golden_acceptance()
    {
        var tl = SettingsCurrencyIdentitiesV1.TurkishLira;
        var result = SalesDraftService.CalculateMarginInTurkishLira(Money.Of(100m, tl, 2), Unit, 1m,
            Snapshot(tl) with { CostPerUnit = 0m });

        Assert.Equal(SalesLineMarginState.Unavailable, result.State);
        Assert.Equal(SalesLineMarginUnavailableReason.CostUnreadable, result.UnavailableReason);
        Assert.Null(result.Amount);
        Assert.Null(result.Percent);
    }

    private static SalesMarginCostSnapshot Snapshot(Guid currencyId) => new(
        Guid.Parse("11111111-1111-4111-8111-111111111111"), 0,
        Guid.Parse("22222222-2222-4222-8222-222222222222"), Unit, 1m, currencyId, 20m, null,
        "{\"syntheticRestoredSnapshot\":true}", new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero));
}
