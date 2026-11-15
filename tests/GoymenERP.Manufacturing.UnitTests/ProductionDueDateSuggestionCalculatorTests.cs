using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// URT-S10 (F-010b, B-15) and URT-SC33 (URT-D86..D89): the one termin suggestion calculator. The clock, zone, calendar and capacity are inputs; every
/// figure here (shift hours, holidays, kg, kg/h) is a synthetic test value, never a business default.
/// 2026-09-18 is a Friday.
/// </summary>
public sealed class ProductionDueDateSuggestionCalculatorTests
{
    private static readonly DateTimeOffset FridayTen = new(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Friday = new(2026, 9, 18);
    private static readonly DateOnly Saturday = new(2026, 9, 19);
    private static readonly DateOnly Monday = new(2026, 9, 21);
    private static readonly DateOnly Tuesday = new(2026, 9, 22);

    private static DueDateCalendarShiftV1[] Weekdays(int startHour, int endHour) =>
    [
        .. new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
            .Select(day => new DueDateCalendarShiftV1(day, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0))),
    ];

    private static ProductionDueDateSuggestionV1 Calculate(IReadOnlyList<DueDateCalendarShiftV1> shifts,
        decimal kg, decimal kgPerHour, DateOnly[]? holidays = null, DateTimeOffset? now = null, TimeZoneInfo? zone = null) =>
        ProductionDueDateSuggestionCalculatorV1.Calculate(now ?? FridayTen, zone ?? TimeZoneInfo.Utc, shifts,
            holidays ?? [], new Kilograms(kg), kgPerHour);

    private static void AssertSuggested(ProductionDueDateSuggestionV1 result, DateOnly day, DateTime completes,
        long minutes)
    {
        Assert.Equal((DueDateSuggestionStatusV1.Suggested, (DateOnly?)day, (DateTime?)completes, (long?)minutes,
                (string?)null),
            (result.Status, result.SuggestedDueDate, result.CompletesAtLocal, result.RequiredWorkingMinutes,
                result.ReasonCode));
    }

    [Fact]
    public void The_rest_of_the_running_shift_counts_from_now()
    {
        var result = Calculate(Weekdays(8, 16), kg: 300m, kgPerHour: 100m);

        AssertSuggested(result, Friday, new DateTime(2026, 9, 18, 13, 0, 0), 180);
        Assert.Equal(3 * TimeSpan.TicksPerHour, result.RequiredWorkingTicks);
    }

    [Fact]
    public void Work_longer_than_the_rest_of_the_week_continues_on_the_next_working_day()
    {
        var result = Calculate(Weekdays(8, 16), kg: 1000m, kgPerHour: 100m);

        AssertSuggested(result, Monday, new DateTime(2026, 9, 21, 12, 0, 0), 600);
    }

    [Fact]
    public void A_holiday_is_skipped()
    {
        var result = Calculate(Weekdays(8, 16), kg: 1000m, kgPerHour: 100m, holidays: [Monday]);

        AssertSuggested(result, Tuesday, new DateTime(2026, 9, 22, 12, 0, 0), 600);
    }

    [Fact]
    public void Now_after_the_shift_has_ended_starts_on_the_next_working_day()
    {
        var evening = new DateTimeOffset(2026, 9, 18, 17, 0, 0, TimeSpan.Zero);

        var result = Calculate(Weekdays(8, 16), kg: 200m, kgPerHour: 100m, now: evening);

        AssertSuggested(result, Monday, new DateTime(2026, 9, 21, 10, 0, 0), 120);
    }

    [Fact]
    public void An_overnight_shift_is_split_at_midnight_and_the_spill_over_part_counts()
    {
        var everyNight = Enum.GetValues<DayOfWeek>()
            .Select(day => new DueDateCalendarShiftV1(day, new TimeOnly(22, 0), new TimeOnly(6, 0)))
            .ToArray();

        var result = Calculate(everyNight, kg: 600m, kgPerHour: 100m);

        AssertSuggested(result, Saturday, new DateTime(2026, 9, 19, 4, 0, 0), 360);
    }

    [Fact]
    public void An_overnight_shift_that_began_yesterday_still_counts_now()
    {
        var thursdayNight = new[] { new DueDateCalendarShiftV1(DayOfWeek.Thursday, new TimeOnly(22, 0), new TimeOnly(6, 0)) };
        var fridayTwo = new DateTimeOffset(2026, 9, 18, 2, 0, 0, TimeSpan.Zero);

        var result = Calculate(thursdayNight, kg: 200m, kgPerHour: 100m, now: fridayTwo);

        AssertSuggested(result, Friday, new DateTime(2026, 9, 18, 4, 0, 0), 120);
    }

    [Fact]
    public void The_spill_over_part_on_a_holiday_counts_when_the_shift_started_on_a_working_day()
    {
        // URT-D88: the overnight shift belongs to its start day; a working start day counts the whole shift.
        var fridayNight = new[] { new DueDateCalendarShiftV1(DayOfWeek.Friday, new TimeOnly(22, 0), new TimeOnly(6, 0)) };

        var result = Calculate(fridayNight, kg: 300m, kgPerHour: 100m, holidays: [Saturday]);

        AssertSuggested(result, Saturday, new DateTime(2026, 9, 19, 1, 0, 0), 180);
    }

    [Fact]
    public void An_overnight_shift_whose_start_day_is_a_holiday_is_skipped_entirely()
    {
        var fridayNight = new[] { new DueDateCalendarShiftV1(DayOfWeek.Friday, new TimeOnly(22, 0), new TimeOnly(6, 0)) };

        var result = Calculate(fridayNight, kg: 100m, kgPerHour: 100m, holidays: [Friday]);

        AssertSuggested(result, new DateOnly(2026, 9, 25), new DateTime(2026, 9, 25, 23, 0, 0), 60);
    }

    [Fact]
    public void Work_ending_exactly_at_midnight_belongs_to_the_day_before_and_needs_no_holiday_spill_over()
    {
        var fridayNight = new[] { new DueDateCalendarShiftV1(DayOfWeek.Friday, new TimeOnly(22, 0), new TimeOnly(6, 0)) };

        var result = Calculate(fridayNight, kg: 200m, kgPerHour: 100m, holidays: [Saturday]);

        AssertSuggested(result, Friday, new DateTime(2026, 9, 19, 0, 0, 0), 120);
    }

    [Fact]
    public void Overlapping_shifts_are_counted_once()
    {
        var overlapping = new[]
        {
            new DueDateCalendarShiftV1(DayOfWeek.Friday, new TimeOnly(12, 0), new TimeOnly(18, 0)),
            new DueDateCalendarShiftV1(DayOfWeek.Friday, new TimeOnly(8, 0), new TimeOnly(16, 0)),
        };

        var result = Calculate(overlapping, kg: 700m, kgPerHour: 100m);

        AssertSuggested(result, Friday, new DateTime(2026, 9, 18, 17, 0, 0), 420);
    }

    [Fact]
    public void The_required_time_is_exact_in_ticks_and_rounded_up_to_whole_minutes_for_display()
    {
        var result = Calculate(Weekdays(8, 16), kg: 1m, kgPerHour: 7m);

        // 1 kg at 7 kg/h is 8.57... minutes: the ticks are the ceiling of the exact value, the minutes round up.
        Assert.Equal(decimal.ToInt64(decimal.Ceiling(TimeSpan.TicksPerHour / 7m)), result.RequiredWorkingTicks);
        Assert.Equal((long?)9, result.RequiredWorkingMinutes);
        Assert.Equal(Friday, result.SuggestedDueDate);
    }

    [Fact]
    public void The_calendar_zone_decides_the_local_day_and_hours()
    {
        var plusThree = TimeZoneInfo.CreateCustomTimeZone("SENTETIK+03", TimeSpan.FromHours(3), "SENTETIK+03",
            "SENTETIK+03");

        // 10:00 UTC is 13:00 local: three hours remain on Friday, the fourth is Monday's first.
        var sameDay = Calculate(Weekdays(8, 16), kg: 300m, kgPerHour: 100m, zone: plusThree);
        var nextWeek = Calculate(Weekdays(8, 16), kg: 400m, kgPerHour: 100m, zone: plusThree);

        AssertSuggested(sameDay, Friday, new DateTime(2026, 9, 18, 16, 0, 0), 180);
        AssertSuggested(nextWeek, Monday, new DateTime(2026, 9, 21, 9, 0, 0), 240);
    }

    [Fact]
    public void Nothing_to_produce_is_suggested_for_today()
    {
        var result = Calculate(Weekdays(8, 16), kg: 0m, kgPerHour: 100m);

        AssertSuggested(result, Friday, new DateTime(2026, 9, 18, 10, 0, 0), 0);
    }

    [Fact]
    public void A_calendar_without_shifts_has_no_working_time()
    {
        var result = Calculate([], kg: 100m, kgPerHour: 100m);

        Assert.Equal((DueDateSuggestionStatusV1.Unavailable, DueDateSuggestionCodesV1.NoWorkingTime, (long?)60),
            (result.Status, result.ReasonCode, result.RequiredWorkingMinutes));
    }

    [Fact]
    public void A_calendar_whose_holidays_cover_every_working_day_has_no_working_time()
    {
        var holidays = Enumerable.Range(-1, ProductionDueDateSuggestionCalculatorV1.SearchHorizonDays + 2)
            .Select(offset => Friday.AddDays(offset))
            .ToArray();

        var result = Calculate(Weekdays(8, 16), kg: 100m, kgPerHour: 100m, holidays: holidays);

        Assert.Equal((DueDateSuggestionStatusV1.Unavailable, DueDateSuggestionCodesV1.NoWorkingTime),
            (result.Status, result.ReasonCode));
    }

    [Fact]
    public void Work_that_cannot_finish_within_the_search_horizon_gives_no_suggestion()
    {
        var oneHourAWeek = new[] { new DueDateCalendarShiftV1(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(9, 0)) };

        // Sixty working hours at one hour a week is more than a year; more hours than the horizon has is refused early.
        var sparse = Calculate(oneHourAWeek, kg: 6000m, kgPerHour: 100m);
        var tooLong = Calculate(Weekdays(8, 16), kg: 10000m, kgPerHour: 1m);

        Assert.Equal((DueDateSuggestionStatusV1.Unavailable, DueDateSuggestionCodesV1.NoWorkingTime, (long?)3600),
            (sparse.Status, sparse.ReasonCode, sparse.RequiredWorkingMinutes));
        Assert.Equal((DueDateSuggestionStatusV1.Unavailable, DueDateSuggestionCodesV1.NoWorkingTime, (long?)null),
            (tooLong.Status, tooLong.ReasonCode, tooLong.RequiredWorkingMinutes));
    }

    [Theory]
    [InlineData(8, 8, (int)DayOfWeek.Friday)]
    [InlineData(8, 16, 7)]
    public void A_shift_without_length_or_with_an_unknown_day_is_an_invalid_calendar(int start, int end, int day)
    {
        var shifts = new[] { new DueDateCalendarShiftV1((DayOfWeek)day, new TimeOnly(start, 0), new TimeOnly(end, 0)) };

        var result = Calculate(shifts, kg: 100m, kgPerHour: 100m);

        Assert.Equal((DueDateSuggestionStatusV1.Unavailable, DueDateSuggestionCodesV1.CalendarInvalid, (long?)null),
            (result.Status, result.ReasonCode, result.RequiredWorkingTicks));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void A_capacity_that_is_not_positive_is_refused_by_the_calculator(string capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Calculate(Weekdays(8, 16), kg: 100m, kgPerHour: decimal.Parse(capacity,
                System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static ProductionDueDateSuggestionV1 CalculateQueued(decimal queuedKg, decimal kg, decimal kgPerHour,
        DueDateSafetyMarginV1 margin, DateOnly[]? holidays = null) =>
        ProductionDueDateSuggestionCalculatorV1.Calculate(FridayTen, TimeZoneInfo.Utc, Weekdays(8, 16), holidays ?? [],
            new Kilograms(queuedKg), new Kilograms(kg), kgPerHour, margin);

    [Fact]
    public void The_queued_work_is_counted_first_and_then_the_plan()
    {
        // URT-D86/URT-D87: (300 queued + 200 plan) input kg at 100 kg/h is five working hours from Friday 10:00.
        var result = CalculateQueued(300m, 200m, 100m, DueDateSafetyMarginV1.None);

        AssertSuggested(result, Friday, new DateTime(2026, 9, 18, 15, 0, 0), 120);
        Assert.Equal(((long?)180, (long?)300), (result.QueueWorkingMinutes, result.TotalWorkingMinutes));
    }

    [Fact]
    public void The_safety_margin_hours_are_working_time_added_after_the_work()
    {
        var result = CalculateQueued(0m, 300m, 100m, new DueDateSafetyMarginV1(TimeSpan.FromHours(6), 0));

        // 3 h of work and 6 h of margin: Friday 10:00-16:00 then Monday 08:00-11:00.
        AssertSuggested(result, Monday, new DateTime(2026, 9, 21, 11, 0, 0), 180);
        Assert.Equal((long?)540, result.TotalWorkingMinutes);
    }

    [Fact]
    public void The_safety_margin_days_are_working_days_after_the_completion_day()
    {
        var result = CalculateQueued(0m, 300m, 100m, new DueDateSafetyMarginV1(TimeSpan.Zero, 1), holidays: [Monday]);

        // Finished Friday 13:00; the weekend has no shift and Monday is a holiday, so one working day later is Tuesday.
        AssertSuggested(result, Tuesday, new DateTime(2026, 9, 18, 13, 0, 0), 180);
    }

    [Fact]
    public void No_queue_and_no_margin_gives_the_same_answer_as_the_plain_form()
    {
        var plain = Calculate(Weekdays(8, 16), kg: 1000m, kgPerHour: 100m);
        var queued = CalculateQueued(0m, 1000m, 100m, DueDateSafetyMarginV1.None);

        Assert.Equal(plain, queued);
    }

    [Fact]
    public void A_negative_safety_margin_is_refused_with_a_code()
    {
        var hours = CalculateQueued(0m, 300m, 100m, new DueDateSafetyMarginV1(TimeSpan.FromHours(-1), 0));
        var days = CalculateQueued(0m, 300m, 100m, new DueDateSafetyMarginV1(TimeSpan.Zero, -1));

        Assert.Equal((DueDateSuggestionStatusV1.Unavailable, DueDateSuggestionCodesV1.InputInvalid),
            (hours.Status, hours.ReasonCode));
        Assert.Equal((DueDateSuggestionStatusV1.Unavailable, DueDateSuggestionCodesV1.InputInvalid),
            (days.Status, days.ReasonCode));
    }
}
