using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// WP15 (DALGA_PLANI_v2 1.16): the legal deadline calculator. Every number below is SENTETIK test input, not a confirmed legal
/// deadline: the real values wait for the financial advisor (MALI_MUSAVIR_SORULARI_v1) and are never in code.
/// </summary>
public sealed class LegalDeadlineCalculatorV1Tests
{
    private static readonly Guid Company = Guid.Parse("15000000-0000-0000-0000-0000000000c1");

    [Fact]
    public void An_unrecorded_calendar_leaves_every_catalogue_obligation_undefined_without_any_counter()
    {
        var counters = LegalDeadlineCalculatorV1.Evaluate(new SettingsLegalDeadlineCalendarV1(Company, 0, []), new DateOnly(2026, 11, 22));
        Assert.Equal(Enum.GetValues<LegalDeadlineObligationV1>(), counters.Select(counter => counter.Obligation));
        Assert.All(counters, counter =>
        {
            Assert.Equal(LegalDeadlineCounterStateV1.Undefined, counter.State);
            Assert.Null(counter.DueDate);
            Assert.Null(counter.DaysRemaining);
            Assert.Null(counter.PeriodStart);
            Assert.Null(counter.PeriodEnd);
        });
    }

    [Fact]
    public void Entries_of_an_undefined_version_are_ignored_fail_closed()
    {
        var calendar = new SettingsLegalDeadlineCalendarV1(Company, 0, [new(LegalDeadlineObligationV1.VatReturn, 1, 1, 28)]);
        Assert.All(LegalDeadlineCalculatorV1.Evaluate(calendar, new DateOnly(2026, 11, 22)),
            counter => Assert.Equal(LegalDeadlineCounterStateV1.Undefined, counter.State));
    }

    [Fact]
    public void A_recorded_version_counts_only_its_own_obligations_the_rest_stay_undefined()
    {
        var calendar = new SettingsLegalDeadlineCalendarV1(Company, 3, [new(LegalDeadlineObligationV1.BaBsForms, 1, 1, 31)]);
        var counters = LegalDeadlineCalculatorV1.Evaluate(calendar, new DateOnly(2026, 11, 22));
        var defined = Assert.Single(counters, counter => counter.State == LegalDeadlineCounterStateV1.Defined);
        Assert.Equal(LegalDeadlineObligationV1.BaBsForms, defined.Obligation);
        Assert.Equal(Enum.GetValues<LegalDeadlineObligationV1>().Length - 1,
            counters.Count(counter => counter.State == LegalDeadlineCounterStateV1.Undefined));
    }

    [Theory]
    // Monthly, due on day 28 of the following month: on the 22nd the October period is due on 28 November.
    [InlineData(1, 1, 28, "2026-11-22", "2026-10-01", "2026-10-31", "2026-11-28", 6)]
    // The due day itself still counts as upcoming with zero days left.
    [InlineData(1, 1, 28, "2026-11-28", "2026-10-01", "2026-10-31", "2026-11-28", 0)]
    // The day after, the next period takes over.
    [InlineData(1, 1, 28, "2026-11-29", "2026-11-01", "2026-11-30", "2026-12-28", 29)]
    // Day 31 in a 30-day month is the month's last day; February clamps to 28 (non-leap 2027).
    [InlineData(1, 1, 31, "2026-11-01", "2026-10-01", "2026-10-31", "2026-11-30", 29)]
    [InlineData(1, 1, 31, "2027-02-01", "2027-01-01", "2027-01-31", "2027-02-28", 27)]
    // Leap year February keeps day 29.
    [InlineData(1, 1, 31, "2028-02-10", "2028-01-01", "2028-01-31", "2028-02-29", 19)]
    // Quarterly, due on day 17 of the second month after the quarter end.
    [InlineData(3, 2, 17, "2026-11-10", "2026-07-01", "2026-09-30", "2026-11-17", 7)]
    [InlineData(3, 2, 17, "2026-11-22", "2026-10-01", "2026-12-31", "2027-02-17", 87)]
    [InlineData(3, 2, 17, "2026-12-01", "2026-10-01", "2026-12-31", "2027-02-17", 78)]
    // Yearly, due in the fourth month after the year end; crossing the year boundary.
    [InlineData(12, 4, 30, "2026-11-22", "2026-01-01", "2026-12-31", "2027-04-30", 159)]
    // Zero months after the period end: due inside the period's last month.
    [InlineData(1, 0, 10, "2026-11-11", "2026-12-01", "2026-12-31", "2026-12-10", 29)]
    public void The_next_due_date_is_the_earliest_on_or_after_today(int periodMonths, int monthsAfter, int dueDay, string today,
        string periodStart, string periodEnd, string due, int daysRemaining)
    {
        var counter = LegalDeadlineCalculatorV1.Next(new(LegalDeadlineObligationV1.VatReturn, periodMonths, monthsAfter, dueDay),
            DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(LegalDeadlineCounterStateV1.Defined, counter.State);
        Assert.Equal((DateOnly.Parse(periodStart, System.Globalization.CultureInfo.InvariantCulture),
                DateOnly.Parse(periodEnd, System.Globalization.CultureInfo.InvariantCulture),
                DateOnly.Parse(due, System.Globalization.CultureInfo.InvariantCulture), daysRemaining),
            (counter.PeriodStart!.Value, counter.PeriodEnd!.Value, counter.DueDate!.Value, counter.DaysRemaining!.Value));
    }

    [Theory]
    [InlineData(2, 1, 28)]
    [InlineData(0, 1, 28)]
    [InlineData(1, -1, 28)]
    [InlineData(1, 13, 28)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, 32)]
    public void An_entry_outside_the_input_range_is_refused_not_computed(int periodMonths, int monthsAfter, int dueDay)
    {
        var entry = new SettingsLegalDeadlineEntryV1(LegalDeadlineObligationV1.VatReturn, periodMonths, monthsAfter, dueDay);
        Assert.False(LegalDeadlineCalculatorV1.IsValid(entry));
        Assert.Throws<ArgumentOutOfRangeException>(() => LegalDeadlineCalculatorV1.Next(entry, new DateOnly(2026, 11, 22)));
    }

    private static readonly Dictionary<int, IReadOnlySet<DateOnly>> Holidays2026 = new()
    {
        // SENTETIK holiday list: 29 October (Thursday) and its eve 28 October; 2027 defined empty.
        [2026] = new HashSet<DateOnly> { new(2026, 10, 28), new(2026, 10, 29) },
        [2027] = new HashSet<DateOnly>(),
    };

    [Theory]
    // 28 November 2026 is a Saturday: moved to Monday 30 November.
    [InlineData(28, "2026-11-22", "2026-11-30", "2026-11-28", LegalDeadlineDueAdjustmentV1.MovedToBusinessDay, 8)]
    // 26 November 2026 is a Thursday: unchanged.
    [InlineData(26, "2026-11-22", "2026-11-26", "2026-11-26", LegalDeadlineDueAdjustmentV1.None, 4)]
    public void YOL_D32_a_due_date_on_a_weekend_moves_to_the_first_business_day_and_keeps_the_legal_date(int day, string today,
        string shown, string legal, LegalDeadlineDueAdjustmentV1 adjustment, int daysRemaining)
    {
        var counter = LegalDeadlineCalculatorV1.Next(new(LegalDeadlineObligationV1.VatReturn, 1, 1, day),
            DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture), Holidays2026);
        Assert.Equal((DateOnly.Parse(shown, System.Globalization.CultureInfo.InvariantCulture),
                DateOnly.Parse(legal, System.Globalization.CultureInfo.InvariantCulture), adjustment, daysRemaining),
            (counter.DueDate!.Value, counter.LegalDueDate!.Value, counter.Adjustment!.Value, counter.DaysRemaining!.Value));
    }

    [Fact]
    public void YOL_D32_holidays_and_eves_are_skipped_together_with_the_weekend()
    {
        // Due 28 October (eve), 29 October (holiday) -> Friday 30 October.
        var (shown, adjustment) = LegalDeadlineCalculatorV1.Shift(new DateOnly(2026, 10, 28), Holidays2026);
        Assert.Equal((new DateOnly(2026, 10, 30), LegalDeadlineDueAdjustmentV1.MovedToBusinessDay), (shown, adjustment));
        // Thursday 31 December 2026 stays; Saturday 2 January 2027 moves to Monday 4 January (2027 defined empty).
        Assert.Equal((new DateOnly(2026, 12, 31), LegalDeadlineDueAdjustmentV1.None), LegalDeadlineCalculatorV1.Shift(new DateOnly(2026, 12, 31), Holidays2026));
        Assert.Equal((new DateOnly(2027, 1, 4), LegalDeadlineDueAdjustmentV1.MovedToBusinessDay), LegalDeadlineCalculatorV1.Shift(new DateOnly(2027, 1, 2), Holidays2026));
    }

    [Fact]
    public void YOL_D32_without_a_holiday_calendar_for_a_year_the_move_needs_nothing_is_moved_and_it_says_so()
    {
        Assert.Equal((new DateOnly(2026, 11, 28), LegalDeadlineDueAdjustmentV1.HolidayCalendarUndefined),
            LegalDeadlineCalculatorV1.Shift(new DateOnly(2026, 11, 28), null));
        Assert.Equal((new DateOnly(2028, 1, 1), LegalDeadlineDueAdjustmentV1.HolidayCalendarUndefined),
            LegalDeadlineCalculatorV1.Shift(new DateOnly(2028, 1, 1), Holidays2026));
        // Saturday 30 December 2028... a move crossing into an undefined year is not applied either.
        var crossing = new Dictionary<int, IReadOnlySet<DateOnly>> { [2028] = new HashSet<DateOnly>() };
        Assert.Equal((new DateOnly(2028, 12, 30), LegalDeadlineDueAdjustmentV1.HolidayCalendarUndefined),
            LegalDeadlineCalculatorV1.Shift(new DateOnly(2028, 12, 30), crossing));
    }

    [Fact]
    public void YOL_D32_the_upcoming_period_is_chosen_by_the_moved_date()
    {
        // On Sunday 29 November the October period (legal 28 Nov, Saturday) is still upcoming: shown Monday 30 November.
        var counter = LegalDeadlineCalculatorV1.Next(new(LegalDeadlineObligationV1.VatReturn, 1, 1, 28), new DateOnly(2026, 11, 29), Holidays2026);
        Assert.Equal((new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 30), 1), (counter.PeriodEnd!.Value, counter.DueDate!.Value, counter.DaysRemaining!.Value));
    }

    [Fact]
    public void An_unknown_obligation_is_not_valid()
    {
        Assert.False(LegalDeadlineCalculatorV1.IsValid(new((LegalDeadlineObligationV1)99, 1, 1, 28)));
    }

    [Fact]
    public void A_version_with_the_same_obligation_twice_cannot_be_evaluated()
    {
        var calendar = new SettingsLegalDeadlineCalendarV1(Company, 1,
            [new(LegalDeadlineObligationV1.VatReturn, 1, 1, 28), new(LegalDeadlineObligationV1.VatReturn, 1, 1, 26)]);
        Assert.Throws<InvalidOperationException>(() => LegalDeadlineCalculatorV1.Evaluate(calendar, new DateOnly(2026, 11, 22)));
    }

    [Fact]
    public void The_input_ranges_and_the_catalogue_are_the_documented_ones()
    {
        Assert.Equal([1, 3, 12], SettingsLegalDeadlineCalendarLimitsV1.PeriodMonths);
        Assert.Equal(12, SettingsLegalDeadlineCalendarLimitsV1.MaximumMonthsAfterPeriodEnd);
        Assert.Equal(31, SettingsLegalDeadlineCalendarLimitsV1.MaximumDueDayOfMonth);
        Assert.Equal(["VatReturn", "WithholdingReturn", "ELedgerBerat", "BaBsForms", "ProvisionalTaxReturn", "CorporateTaxReturn",
                "StampTaxReturn", "SocialSecurityReturn"],
            Enum.GetValues<LegalDeadlineObligationV1>().Select(value => value.ToString()));
        Assert.Equal("legal.deadline-calendar:4", new SettingsLegalDeadlineCalendarV1(Company, 4, []).VersionText);
    }
}
