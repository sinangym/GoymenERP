using GoymenERP.BuildingBlocks.Monetary;
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
    public void SAT_S12_actual_TL_binding_preserves_the_reviewed_margin_without_enabling_the_golden_gate()
    {
        var tl = SettingsCurrencyIdentitiesV1.TurkishLira;
        var result = SalesDraftService.CalculateMarginInTurkishLira(Money.Of(100m, tl, 2), Unit, 1m, Snapshot(tl));

        Assert.Equal(SalesLineMarginState.Computed, result.State);
        Assert.Equal(Money.Of(80m, tl, 2), result.Amount);
        Assert.Equal(80m, result.Percent);
    }

    private static SalesMarginCostSnapshot Snapshot(Guid currencyId) => new(
        Guid.Parse("11111111-1111-4111-8111-111111111111"), 0,
        Guid.Parse("22222222-2222-4222-8222-222222222222"), Unit, 1m, currencyId, 20m, null,
        "{\"syntheticRestoredSnapshot\":true}", new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero));
}
