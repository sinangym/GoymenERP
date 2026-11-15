using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-S13 (URT-D47, URT-D65): the monthly scrap price as a value. A month is a calendar year and month; the price is a
/// positive TL per kg figure with at most four decimals (a fifth is refused, never rounded); a stored price that the rules
/// cannot have produced is refused on rebuild, never repaired; a month's price answers the cost calculator's input as a
/// value. URT-D72: a wrong price is corrected by a new version with a reason and the old version stays as it is; URT-D73:
/// only the current and past months (the Europe/Istanbul business day of the entry) take a price. Every figure here is
/// synthetic; no price policy is chosen.
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
            year, month, value, MonthlyScrapPrice.FirstVersion, null, "synthetic-actor", At));

        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, refused.Code);
    }

    [Fact]
    public void A_stored_price_rebuilds_as_it_was_written()
    {
        var id = Guid.NewGuid();

        var price = MonthlyScrapPrice.Restore(id, Company, 2026, 11, 7.2500m, MonthlyScrapPrice.FirstVersion, null,
            "synthetic-actor", At);

        Assert.Equal((id, new ScrapPricePeriod(2026, 11), 7.25m, 1, (string?)null),
            (price.Id, price.Period, price.Price.Value, price.Version, price.Reason));
    }

    [Fact]
    public void A_first_entry_is_version_one_without_a_reason()
    {
        var price = Recorded(7.25m);

        Assert.Equal((MonthlyScrapPrice.FirstVersion, (string?)null), (price.Version, price.Reason));
    }

    [Fact]
    public void A_correction_is_the_next_version_of_the_same_month_with_its_reason_and_the_old_version_stays()
    {
        var first = Recorded(7.25m);
        var id = Guid.NewGuid();
        var later = At.AddDays(1);

        var corrected = first.Correct(id, MonthlyScrapPrice.FirstVersion, new ScrapPriceTlPerKg(6.5m),
            "  SENTETIK yanlis girildi  ", " synthetic-checker ", later);

        Assert.Equal((id, Company, first.Period, 6.5m, 2, "SENTETIK yanlis girildi", "synthetic-checker", later),
            (corrected.Id, corrected.CompanyId, corrected.Period, corrected.Price.Value, corrected.Version,
                corrected.Reason, corrected.RecordedBy, corrected.RecordedAtUtc));
        Assert.Equal((1, 7.25m, (string?)null), (first.Version, first.Price.Value, first.Reason));
        Assert.Equal(new ScrapPriceInput(ScrapPriceReadStatus.Value, 6.5m), corrected.ToInput());

        var again = corrected.Correct(Guid.NewGuid(), 2, new ScrapPriceTlPerKg(6.75m), "SENTETIK ikinci duzeltme",
            "synthetic-checker", later);
        Assert.Equal(3, again.Version);
    }

    [Fact]
    public void A_correction_from_a_stale_version_is_a_version_conflict_before_any_other_rule()
    {
        var corrected = Recorded(7.25m).Correct(Guid.NewGuid(), 1, new ScrapPriceTlPerKg(6.5m), "SENTETIK ilk",
            "synthetic-actor", At);

        var conflict = Assert.Throws<ManufacturingVersionConflictException>(() => corrected.Correct(Guid.NewGuid(), 1,
            new ScrapPriceTlPerKg(6.5m), " ", "synthetic-actor", At));

        Assert.Equal((1L, 2L), (conflict.ExpectedVersion, conflict.CurrentVersion));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-long")]
    public void A_correction_without_a_reason_or_with_a_reason_longer_than_the_column_is_refused(string reason)
    {
        var text = reason == "too-long" ? new string('a', MonthlyScrapPrice.ReasonMaximumLength + 1) : reason;

        var refused = Assert.Throws<ArgumentException>(() => Recorded(7.25m).Correct(Guid.NewGuid(), 1,
            new ScrapPriceTlPerKg(6.5m), text, "synthetic-actor", At));

        Assert.Equal("reason", refused.ParamName);
    }

    [Fact]
    public void A_correction_with_the_effective_price_again_is_refused_as_unchanged_also_with_trailing_zeros()
    {
        var refused = Assert.Throws<ManufacturingRuleException>(() => Recorded(7.25m).Correct(Guid.NewGuid(), 1,
            new ScrapPriceTlPerKg(7.2500m), "SENTETIK ayni fiyat", "synthetic-actor", At));

        Assert.Equal(ManufacturingRuleCodes.ScrapPriceUnchanged, refused.Code);
    }

    [Fact]
    public void A_future_month_is_refused_on_entry_and_the_current_and_a_past_month_are_taken()
    {
        var refused = Assert.Throws<ManufacturingRuleException>(() => MonthlyScrapPrice.Record(Guid.NewGuid(), Company,
            new ScrapPricePeriod(2026, 12), new ScrapPriceTlPerKg(7.25m), "synthetic-actor", At));

        Assert.Equal(ManufacturingRuleCodes.ScrapPriceFutureMonth, refused.Code);
        Assert.Equal(11, MonthlyScrapPrice.Record(Guid.NewGuid(), Company, new ScrapPricePeriod(2026, 11),
            new ScrapPriceTlPerKg(7.25m), "synthetic-actor", At).Period.Month);
        Assert.Equal(2025, MonthlyScrapPrice.Record(Guid.NewGuid(), Company, new ScrapPricePeriod(2025, 12),
            new ScrapPriceTlPerKg(7.25m), "synthetic-actor", At).Period.Year);
    }

    [Fact]
    public void The_Istanbul_business_day_decides_the_current_month_not_the_utc_date()
    {
        // 2026-11-30 21:00Z is already 1 December in Istanbul (UTC+3); 20:59Z is still 30 November.
        var stillNovember = new DateTimeOffset(2026, 11, 30, 20, 59, 0, TimeSpan.Zero);
        var alreadyDecember = new DateTimeOffset(2026, 11, 30, 21, 0, 0, TimeSpan.Zero);
        var december = new ScrapPricePeriod(2026, 12);

        var refused = Assert.Throws<ManufacturingRuleException>(() => MonthlyScrapPrice.Record(Guid.NewGuid(), Company,
            december, new ScrapPriceTlPerKg(7.25m), "synthetic-actor", stillNovember));

        Assert.Equal(ManufacturingRuleCodes.ScrapPriceFutureMonth, refused.Code);
        Assert.Equal(december, MonthlyScrapPrice.Record(Guid.NewGuid(), Company, december, new ScrapPriceTlPerKg(7.25m),
            "synthetic-actor", alreadyDecember).Period);
    }

    [Fact]
    public void A_correction_of_a_stored_future_month_is_refused_by_the_entry_rule_and_its_rebuild_is_not()
    {
        // A version stored under a later clock rebuilds (the month rule is an entry rule), but a correction entered before
        // that month began is refused.
        var stored = MonthlyScrapPrice.Restore(Guid.NewGuid(), Company, 2027, 1, 7.25m, MonthlyScrapPrice.FirstVersion,
            null, "synthetic-actor", At);

        var refused = Assert.Throws<ManufacturingRuleException>(() => stored.Correct(Guid.NewGuid(), 1,
            new ScrapPriceTlPerKg(6.5m), "SENTETIK", "synthetic-actor", At));

        Assert.Equal(ManufacturingRuleCodes.ScrapPriceFutureMonth, refused.Code);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "SENTETIK")]
    [InlineData(2, null)]
    [InlineData(2, " ")]
    public void A_stored_version_whose_reason_does_not_fit_its_version_is_refused_on_rebuild(int version, string? reason)
    {
        var refused = Assert.Throws<ManufacturingRuleException>(() => MonthlyScrapPrice.Restore(Guid.NewGuid(), Company,
            2026, 11, 7.25m, version, reason, "synthetic-actor", At));

        Assert.Equal(ManufacturingRuleCodes.StoredRecordInvalid, refused.Code);
    }

    [Fact]
    public void A_stored_correction_rebuilds_with_its_version_and_reason()
    {
        var price = MonthlyScrapPrice.Restore(Guid.NewGuid(), Company, 2026, 11, 6.5m, 3, "SENTETIK", "synthetic-actor",
            At);

        Assert.Equal((3, "SENTETIK"), (price.Version, price.Reason));
    }

    private static MonthlyScrapPrice Recorded(decimal tlPerKg) =>
        MonthlyScrapPrice.Record(Guid.NewGuid(), Company, new ScrapPricePeriod(2026, 11), new ScrapPriceTlPerKg(tlPerKg),
            "synthetic-actor", At);
}
