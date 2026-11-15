using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-S13 (URT-D47, URT-D65): the monthly scrap price as a value. A month is a calendar year and month; the price is a
/// positive TL per kg figure with at most four decimals (a fifth is refused, never rounded); a stored price that the rules
/// cannot have produced is refused on rebuild, never repaired; a month's price answers the cost calculator's input as a
/// value. Every figure here is synthetic; no price policy is chosen.
/// </summary>
public sealed class MonthlyScrapPriceTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset At = new(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void A_month_is_its_year_and_month_and_a_business_day_falls_in_its_own_month()
    {
        var period = new ScrapPricePeriod(2026, 3);

        Assert.Equal((2026, 3), (period.Year, period.Month));
        Assert.Equal("2026-03", period.ToString());
        Assert.Equal(period, ScrapPricePeriod.Of(new DateOnly(2026, 3, 31)));
        Assert.NotEqual(period, ScrapPricePeriod.Of(new DateOnly(2026, 4, 1)));
    }

    [Theory]
    [InlineData(2026, 0, "month")]
    [InlineData(2026, 13, "month")]
    [InlineData(0, 1, "year")]
    [InlineData(10000, 1, "year")]
    public void A_month_outside_the_calendar_is_refused(int year, int month, string parameter)
    {
        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => new ScrapPricePeriod(year, month));

        Assert.Equal(parameter, refused.ParamName);
    }

    [Theory]
    [InlineData("0.0001")]
    [InlineData("7.25")]
    [InlineData("99999999999999.9999")]
    public void A_positive_price_with_at_most_four_decimals_is_kept_as_entered(string text)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(value, new ScrapPriceTlPerKg(value).Value);
    }

    [Fact]
    public void Trailing_zeros_beyond_four_decimals_are_not_a_fifth_decimal()
    {
        Assert.Equal(7.25m, new ScrapPriceTlPerKg(7.250000m).Value);
    }

    [Fact]
    public void A_fifth_decimal_is_refused_by_the_precision_rule_never_rounded()
    {
        var refused = Assert.Throws<ManufacturingRuleException>(() => new ScrapPriceTlPerKg(7.12345m));

        Assert.Equal(ManufacturingRuleCodes.ScrapPricePrecisionExceeded, refused.Code);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.0001")]
    [InlineData("100000000000000")]
    public void A_zero_negative_or_oversized_price_is_refused(string text)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Throws<ArgumentOutOfRangeException>(() => new ScrapPriceTlPerKg(value));
    }

    [Fact]
    public void A_recorded_price_keeps_its_company_month_and_actor_in_utc_and_answers_the_calculator_as_a_value()
    {
        var id = Guid.NewGuid();

        var price = MonthlyScrapPrice.Record(id, Company, new ScrapPricePeriod(2026, 11), new ScrapPriceTlPerKg(7.25m),
            "  synthetic-actor  ", At);

        Assert.Equal((id, Company, new ScrapPricePeriod(2026, 11), 7.25m, "synthetic-actor"),
            (price.Id, price.CompanyId, price.Period, price.Price.Value, price.RecordedBy));
        Assert.Equal(TimeSpan.Zero, price.RecordedAtUtc.Offset);
        Assert.Equal(At, price.RecordedAtUtc);
        Assert.Equal(new ScrapPriceInput(ScrapPriceReadStatus.Value, 7.25m), price.ToInput());
    }

    [Theory]
    [InlineData("id")]
    [InlineData("companyId")]
    [InlineData("period")]
    [InlineData("price")]
    [InlineData("recordedBy")]
    [InlineData("recordedBy-long")]
    public void A_record_without_its_identity_month_price_or_actor_is_refused(string missing)
    {
        var refused = Assert.Throws<ArgumentException>(() => MonthlyScrapPrice.Record(
            missing == "id" ? Guid.Empty : Guid.NewGuid(),
            missing == "companyId" ? Guid.Empty : Company,
            missing == "period" ? default : new ScrapPricePeriod(2026, 11),
            missing == "price" ? default : new ScrapPriceTlPerKg(7.25m),
            missing switch
            {
                "recordedBy" => " ",
                "recordedBy-long" => new string('a', MonthlyScrapPrice.RecordedByMaximumLength + 1),
                _ => "synthetic-actor",
            },
            At));

        Assert.Equal(missing.Split('-')[0], refused.ParamName);
    }

    [Theory]
    [InlineData(2026, 13, "7.25")]
    [InlineData(2026, 11, "0")]
    [InlineData(2026, 11, "7.12345")]
    public void A_stored_price_the_rules_cannot_have_produced_is_refused_on_rebuild(int year, int month, string text)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        var refused = Assert.Throws<ManufacturingRuleException>(() => MonthlyScrapPrice.Restore(Guid.NewGuid(), Company,
            year, month, value, "synthetic-actor", At));

        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, refused.Code);
    }

    [Fact]
    public void A_stored_price_rebuilds_as_it_was_written()
    {
        var id = Guid.NewGuid();

        var price = MonthlyScrapPrice.Restore(id, Company, 2026, 11, 7.2500m, "synthetic-actor", At);

        Assert.Equal((id, new ScrapPricePeriod(2026, 11), 7.25m), (price.Id, price.Period, price.Price.Value));
    }
}
