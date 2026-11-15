using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// API-85b (FNS-D31, FNS-D58): the one collateral calculator. The weighted collateral is amount x the Settings type
/// rate / 100, exact and per currency; anything Finance cannot read exactly makes the whole figure unavailable. The
/// documentary risk counts every customer instrument that is neither collected nor returned. F018 §L3: the held
/// collateral at its recorded amount and the utilization (the open receivables taken from the weighted collateral, per
/// currency, failing closed across currencies). SENTETIK / NOT_ENV.
/// </summary>
public sealed class FinancePartyCollateralFiguresTests
{
    private static readonly Guid Lira = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Dollar = Guid.Parse("23232323-2323-2323-2323-232323232323");
    private static readonly Guid Guarantee = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Mortgage = Guid.Parse("56565656-5656-5656-5656-565656565656");
    private static readonly DateOnly Today = new(2026, 11, 15);

    [Theory]
    [InlineData("1000.50", "75.5", "755.3775")]
    [InlineData("1000", "100", "1000")]
    [InlineData("1000", "0", "0")]
    [InlineData("0.01", "33.33", "0.003333")]
    [InlineData("250000.00", "50.00", "125000")]
    public void A_weighted_collateral_is_the_exact_amount_times_the_rate_over_a_hundred(string amount, string rate,
        string weighted)
    {
        var figure = FinancePartyCollateralFigures.Weigh([Held(Guarantee, Lira, amount)], Catalog(Item(Guarantee, rate)),
            Today);

        Assert.True(figure.IsAvailable);
        var single = Assert.Single(figure.Amounts);
        Assert.Equal((Lira, weighted), (single.CurrencyId, single.Amount));
    }

    [Fact]
    public void Weighted_collaterals_are_summed_per_currency_and_never_converted()
    {
        var figure = FinancePartyCollateralFigures.Weigh(
            [Held(Guarantee, Lira, "1000"), Held(Mortgage, Lira, "200.10"), Held(Guarantee, Dollar, "40")],
            Catalog(Item(Guarantee, "80"), Item(Mortgage, "50")), Today);

        Assert.True(figure.IsAvailable);
        Assert.Equal([(Lira, "900.05"), (Dollar, "32")],
            figure.Amounts.Select(amount => (amount.CurrencyId, amount.Amount)));
    }

    [Fact]
    public void Only_a_collateral_in_force_on_the_read_date_counts_and_none_in_force_is_a_recorded_zero()
    {
        HeldInput[] held =
        [
            new(Guarantee, Lira, "100", Today.AddDays(1), null),
            new(Guarantee, Lira, "100", Today.AddDays(-30), Today.AddDays(-1)),
        ];

        // Nothing in force: the catalog is not even needed.
        var figure = FinancePartyCollateralFigures.Weigh([.. held.Select(ToHeld)], catalog: null, Today);
        Assert.Equal((true, 0, (string?)null), (figure.IsAvailable, figure.Amounts.Count, figure.ReasonCode));

        // The first and the last day both count.
        Assert.True(FinancePartyCollateralFigures.IsInForce(Today, Today, Today));
        Assert.True(FinancePartyCollateralFigures.IsInForce(Today.AddDays(-1), null, Today));
        Assert.False(FinancePartyCollateralFigures.IsInForce(Today.AddDays(1), null, Today));
        Assert.False(FinancePartyCollateralFigures.IsInForce(Today.AddDays(-2), Today.AddDays(-1), Today));
    }

    [Theory]
    [InlineData("catalog-unavailable")]
    [InlineData("type-not-listed")]
    [InlineData("type-listed-twice")]
    [InlineData("type-is-instrument")]
    [InlineData("rate-above-hundred")]
    [InlineData("rate-negative")]
    [InlineData("rate-exponent")]
    [InlineData("rate-padded")]
    [InlineData("rate-comma")]
    [InlineData("rate-null")]
    public void A_rate_Finance_cannot_read_exactly_makes_the_whole_figure_unavailable(string fault)
    {
        FinanceCollateralTypeCatalogAnswer catalog = fault switch
        {
            "catalog-unavailable" => FinanceCollateralTypeCatalogAnswer.NotAvailable,
            "type-not-listed" => Catalog(Item(Mortgage, "50")),
            "type-listed-twice" => Catalog(Item(Guarantee, "50"), Item(Guarantee, "60")),
            "type-is-instrument" => Catalog(Item(Guarantee, "50") with { IsNegotiableInstrument = true }),
            "rate-above-hundred" => Catalog(Item(Guarantee, "100.01")),
            "rate-negative" => Catalog(Item(Guarantee, "-1")),
            "rate-exponent" => Catalog(Item(Guarantee, "1e2")),
            "rate-padded" => Catalog(Item(Guarantee, " 50")),
            "rate-comma" => Catalog(Item(Guarantee, "50,5")),
            _ => Catalog(Item(Guarantee, null!)),
        };

        // A second collateral that could be weighed does not turn the answer into a partial sum.
        var figure = FinancePartyCollateralFigures.Weigh(
            [Held(Mortgage, Dollar, "100"), Held(Guarantee, Lira, "1000")],
            fault == "type-not-listed" ? catalog : Add(catalog, Item(Mortgage, "50")), Today);

        Assert.Equal((false, 0, FinancePartyCollateralFigures.TypeRateUnavailable),
            (figure.IsAvailable, figure.Amounts.Count, figure.ReasonCode));
    }

    [Fact]
    public void A_type_deactivated_after_the_record_still_answers_its_rate()
    {
        var figure = FinancePartyCollateralFigures.Weigh([Held(Guarantee, Lira, "1000")],
            Catalog(Item(Guarantee, "40") with { IsActive = false }), Today);

        Assert.Equal((Lira, "400"), (Assert.Single(figure.Amounts).CurrencyId, figure.Amounts[0].Amount));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    public void A_stored_amount_that_does_not_parse_is_unreadable(string amount)
    {
        var figure = FinancePartyCollateralFigures.Weigh([Held(Guarantee, Lira, amount)], Catalog(Item(Guarantee, "50")),
            Today);

        Assert.Equal((false, FinancePartyCollateralFigures.CollateralUnreadable), (figure.IsAvailable, figure.ReasonCode));
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("100", true)]
    [InlineData("100.00", true)]
    [InlineData("12.345678", true)]
    [InlineData("100.000001", false)]
    [InlineData("", false)]
    [InlineData("+5", false)]
    [InlineData(".5", false)]
    public void A_rate_is_exact_decimal_text_within_zero_and_a_hundred(string rate, bool readable) =>
        Assert.Equal(readable, FinancePartyCollateralFigures.TryRate(rate, out _));

    [Fact]
    public void The_documentary_risk_counts_every_instrument_neither_collected_nor_returned_per_currency()
    {
        var figure = FinancePartyCollateralFigures.DocumentaryRisk(
        [
            Instrument(CustomerInstrumentStatus.InPortfolio, Lira, "100"),
            Instrument(CustomerInstrumentStatus.AtBankForCollection, Lira, "50.25"),
            Instrument(CustomerInstrumentStatus.Bounced, Lira, "10"),
            Instrument(CustomerInstrumentStatus.Protested, Dollar, "7"),
            Instrument(CustomerInstrumentStatus.Collected, Lira, "1000"),
            Instrument(CustomerInstrumentStatus.ReturnedToCustomer, Dollar, "1000"),
        ]);

        Assert.True(figure.IsAvailable);
        Assert.Equal([(Lira, "160.25"), (Dollar, "7")], figure.Amounts.Select(amount => (amount.CurrencyId, amount.Amount)));
        Assert.Empty(FinancePartyCollateralFigures.DocumentaryRisk([]).Amounts);
    }

    [Fact]
    public void An_unknown_instrument_status_or_an_unreadable_amount_cannot_be_calculated()
    {
        var unknown = FinancePartyCollateralFigures.DocumentaryRisk(
            [Instrument(CustomerInstrumentStatus.InPortfolio, Lira, "100"), new(99, Lira, "1")]);
        Assert.Equal((false, FinancePartyCollateralFigures.DocumentaryRiskStatusUnknown),
            (unknown.IsAvailable, unknown.ReasonCode));

        var unreadable = FinancePartyCollateralFigures.DocumentaryRisk(
            [Instrument(CustomerInstrumentStatus.InPortfolio, Guid.Empty, "100")]);
        Assert.Equal((false, FinancePartyCollateralFigures.DocumentaryRiskUnreadable),
            (unreadable.IsAvailable, unreadable.ReasonCode));

        // A collected instrument leaves the risk before its amount is read.
        Assert.True(FinancePartyCollateralFigures.DocumentaryRisk(
            [Instrument(CustomerInstrumentStatus.Collected, Lira, "not-an-amount")]).IsAvailable);
    }

    [Fact]
    public void The_held_collateral_is_the_in_force_recorded_amounts_per_currency_and_an_unreadable_one_is_unavailable()
    {
        var figure = FinancePartyCollateralFigures.Gross(
        [
            Held(Guarantee, Lira, "1000.00"),
            Held(Mortgage, Lira, "200.10"),
            Held(Guarantee, Dollar, "40"),
            new(Guarantee, Lira, "999", Today.AddDays(1), null),
            new(Guarantee, Lira, "999", Today.AddDays(-30), Today.AddDays(-1)),
        ], Today);

        Assert.True(figure.IsAvailable);
        Assert.Equal([(Lira, "1200.1"), (Dollar, "40")], figure.Amounts.Select(amount => (amount.CurrencyId, amount.Amount)));
        Assert.Equal((true, 0), (FinancePartyCollateralFigures.Gross([], Today).IsAvailable,
            FinancePartyCollateralFigures.Gross([], Today).Amounts.Count));

        var unreadable = FinancePartyCollateralFigures.Gross([Held(Guarantee, Lira, "1,5")], Today);
        Assert.Equal((false, FinancePartyCollateralFigures.CollateralUnreadable), (unreadable.IsAvailable, unreadable.ReasonCode));
        // A collateral not in force is not read at all.
        Assert.True(FinancePartyCollateralFigures.Gross(
            [new(Guarantee, Lira, "not-an-amount", Today.AddDays(1), null)], Today).IsAvailable);
    }

    [Theory]
    [InlineData("300", "800", "300")]
    [InlineData("800", "300", "300")]
    [InlineData("500.50", "500.5", "500.5")]
    public void The_utilization_is_per_currency_the_smaller_of_the_open_receivables_and_the_weighted_collateral(
        string open, string weighted, string used)
    {
        var figure = FinancePartyCollateralFigures.Utilization(Available((Lira, open)), Available((Lira, weighted)));

        Assert.True(figure.IsAvailable);
        Assert.Equal([(Lira, used)], figure.Amounts.Select(amount => (amount.CurrencyId, amount.Amount)));
    }

    [Fact]
    public void Nothing_owed_or_nothing_held_uses_nothing_and_both_currencies_short_or_both_long_still_answer()
    {
        Assert.Empty(FinancePartyCollateralFigures.Utilization(Available(), Available((Lira, "800"))).Amounts);
        Assert.Empty(FinancePartyCollateralFigures.Utilization(Available((Lira, "800")), Available()).Amounts);

        var bothShort = FinancePartyCollateralFigures.Utilization(Available((Lira, "900"), (Dollar, "50")),
            Available((Lira, "100"), (Dollar, "20")));
        Assert.Equal([(Lira, "100"), (Dollar, "20")], bothShort.Amounts.Select(amount => (amount.CurrencyId, amount.Amount)));

        var bothLong = FinancePartyCollateralFigures.Utilization(Available((Lira, "10"), (Dollar, "5")),
            Available((Lira, "100"), (Dollar, "20")));
        Assert.Equal([(Lira, "10"), (Dollar, "5")], bothLong.Amounts.Select(amount => (amount.CurrencyId, amount.Amount)));
    }

    [Fact]
    public void A_leftover_in_one_currency_beside_an_uncovered_receivable_in_another_has_no_decided_rule_and_is_unavailable()
    {
        var figure = FinancePartyCollateralFigures.Utilization(Available((Lira, "900")), Available((Dollar, "20")));

        Assert.Equal((false, 0, FinancePartyCollateralFigures.UtilizationCurrencyRuleNotSet),
            (figure.IsAvailable, figure.Amounts.Count, figure.ReasonCode));
    }

    [Fact]
    public void An_unavailable_or_unreadable_input_makes_the_utilization_unavailable()
    {
        var unavailable = FinancePartyCollateralFigures.Figure.Unavailable(FinancePartyCollateralFigures.TypeRateUnavailable);
        Assert.Equal(FinancePartyCollateralFigures.UtilizationSourceUnavailable,
            FinancePartyCollateralFigures.Utilization(Available((Lira, "1")), unavailable).ReasonCode);
        Assert.Equal(FinancePartyCollateralFigures.UtilizationSourceUnavailable,
            FinancePartyCollateralFigures.Utilization(unavailable, Available((Lira, "1"))).ReasonCode);
        Assert.False(FinancePartyCollateralFigures.Utilization(Available((Lira, "-1")), Available((Lira, "1"))).IsAvailable);
        Assert.False(FinancePartyCollateralFigures.Utilization(Available((Lira, "1"), (Lira, "2")),
            Available((Lira, "1"))).IsAvailable);
        Assert.False(FinancePartyCollateralFigures.Utilization(Available((Guid.Empty, "1")),
            Available((Lira, "1"))).IsAvailable);
    }

    private static FinancePartyCollateralFigures.Figure Available(params (Guid CurrencyId, string Amount)[] amounts) =>
        new(true, [.. amounts.Select(amount => new FinancePartyCollateralFigures.CurrencyAmount(amount.CurrencyId,
            amount.Amount))], null);

    private sealed record HeldInput(Guid TypeId, Guid CurrencyId, string Amount, DateOnly From, DateOnly? Until);

    private static FinancePartyCollateralFigures.HeldCollateral ToHeld(HeldInput input) =>
        new(input.TypeId, input.CurrencyId, input.Amount, input.From, input.Until);

    private static FinancePartyCollateralFigures.HeldCollateral Held(Guid type, Guid currency, string amount) =>
        new(type, currency, amount, Today.AddDays(-10), null);

    private static FinancePartyCollateralFigures.PortfolioInstrument Instrument(CustomerInstrumentStatus status,
        Guid currency, string amount) => new((int)status, currency, amount);

    private static FinanceCollateralTypeCatalogItem Item(Guid type, string rate) =>
        new(type, "SENTETIK-TUR", "SENTETIK teminat türü", IsActive: true, rate, "1", IsNegotiableInstrument: false);

    private static FinanceCollateralTypeCatalogAnswer Catalog(params FinanceCollateralTypeCatalogItem[] items) =>
        new(FinanceCollateralTypeCatalogState.Available, items);

    private static FinanceCollateralTypeCatalogAnswer Add(FinanceCollateralTypeCatalogAnswer catalog,
        FinanceCollateralTypeCatalogItem item) =>
        catalog with { Items = [.. catalog.Items, item] };
}
