using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

// URT-S10 (F-010b, B-15, OPEN-007 note): the calendar-based termin SUGGESTION. The work center's shift calendar
// (Organization's planning reference) and the hourly capacity (a Settings parameter value, never a number in code) give
// the day on which the plan's quantity would be finished if work started now. The result is only a proposal: the user
// confirms or changes it through the existing hand-entered termin command (F-010a, URT-D29); nothing is stored here.
//
// Deliberately NOT decided here (open business policy, fail-closed until answered):
// - the queue or load of other plans/orders on the same work center (the suggestion assumes the line is free now);
// - whether the part of an overnight shift that runs into a holiday is working time (a needed spill-over part on a
//   holiday stops the suggestion with OvernightHolidaySpillUndecided instead of choosing either answer);
// - any safety buffer added to the computed day.
// The walk counts local wall-clock time of each shift (a shift is the hours the calendar names, also on a day the zone
// changes its clock offset).

/// <summary>One working shift of the calendar as the domain reads it. <see cref="End"/> earlier than (or equal to midnight
/// before) <see cref="Start"/> is an overnight shift that runs into the next day; the shift belongs to its start day.</summary>
public sealed record DueDateCalendarShiftV1(DayOfWeek Day, TimeOnly Start, TimeOnly End);

/// <summary>The outcome of a suggestion.</summary>
public enum DueDateSuggestionStatusV1
{
    Suggested,
    Unavailable,
}

/// <summary>Stable machine codes of a suggestion that cannot be given.</summary>
public static class DueDateSuggestionCodesV1
{
    /// <summary>The calendar has no working time at all, or not enough within the search horizon.</summary>
    public const string NoWorkingTime = "no_working_time";

    /// <summary>A shift whose start equals its end, or a day value that is not a weekday.</summary>
    public const string CalendarInvalid = "calendar_invalid";

    /// <summary>The work would need the part of an overnight shift that falls on a holiday; that policy is open.</summary>
    public const string OvernightHolidaySpillUndecided = "overnight_holiday_spill_undecided";
}

/// <summary>
/// A suggestion: the local calendar day on which the last working minute falls, the local moment the work would
/// finish, and the working time needed (ticks, exact; minutes rounded up for display only).
/// </summary>
public sealed record ProductionDueDateSuggestionV1(
    DueDateSuggestionStatusV1 Status,
    DateOnly? SuggestedDueDate,
    DateTime? CompletesAtLocal,
    long? RequiredWorkingTicks,
    string? ReasonCode)
{
    /// <summary>The required working time in whole minutes, rounded UP (a display figure; the calculation uses ticks).</summary>
    public long? RequiredWorkingMinutes => RequiredWorkingTicks is { } ticks
        ? ticks / TimeSpan.TicksPerMinute + (ticks % TimeSpan.TicksPerMinute == 0 ? 0 : 1)
        : null;

    public static ProductionDueDateSuggestionV1 Unavailable(string reasonCode, long? requiredTicks = null) =>
        new(DueDateSuggestionStatusV1.Unavailable, null, null, requiredTicks, reasonCode);
}

/// <summary>
/// The one deterministic termin suggestion calculator (URT-S10). It walks the calendar forward from <c>now</c> in the
/// calendar's own time zone: the rest of a running shift counts, holidays are skipped (a shift belongs to its start day),
/// overlapping shifts are counted once, and an overnight shift is split at midnight. Pure: the clock, zone, calendar and
/// capacity are all inputs.
/// </summary>
public static class ProductionDueDateSuggestionCalculatorV1
{
    /// <summary>
    /// A technical search bound, not a business rule: a calendar that cannot finish the work within this many days from
    /// today gives no suggestion (<see cref="DueDateSuggestionCodesV1.NoWorkingTime"/>) instead of searching forever. The
    /// user still enters any termin by hand.
    /// </summary>
    public const int SearchHorizonDays = 366;

    public static ProductionDueDateSuggestionV1 Calculate(
        DateTimeOffset now,
        TimeZoneInfo zone,
        IReadOnlyList<DueDateCalendarShiftV1> shifts,
        IReadOnlyCollection<DateOnly> holidays,
        Kilograms required,
        decimal capacityKgPerHour)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(shifts);
        ArgumentNullException.ThrowIfNull(holidays);
        if (capacityKgPerHour <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(capacityKgPerHour), capacityKgPerHour,
                "Saatlik kapasite pozitif olmalıdır.");
        }

        if (shifts.Any(shift => shift is null || !Enum.IsDefined(shift.Day) || shift.Start == shift.End))
        {
            return ProductionDueDateSuggestionV1.Unavailable(DueDateSuggestionCodesV1.CalendarInvalid);
        }

        var localNow = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var exactTicks = required.Value * TimeSpan.TicksPerHour / capacityKgPerHour;
        var horizonTicks = (decimal)TimeSpan.TicksPerDay * (SearchHorizonDays + 1);
        if (exactTicks > horizonTicks)
        {
            return ProductionDueDateSuggestionV1.Unavailable(DueDateSuggestionCodesV1.NoWorkingTime);
        }

        var requiredTicks = (long)decimal.Ceiling(exactTicks);
        if (requiredTicks == 0)
        {
            return new ProductionDueDateSuggestionV1(DueDateSuggestionStatusV1.Suggested,
                DateOnly.FromDateTime(localNow), localNow, 0, null);
        }

        if (shifts.Count == 0)
        {
            return ProductionDueDateSuggestionV1.Unavailable(DueDateSuggestionCodesV1.NoWorkingTime, requiredTicks);
        }

        var holidaySet = holidays as IReadOnlySet<DateOnly> ?? holidays.ToHashSet();
        var ordered = shifts.OrderBy(shift => shift.Start).ToArray();
        var today = DateOnly.FromDateTime(localNow);
        var remaining = requiredTicks;
        var cursor = localNow;

        // Start one day back: an overnight shift that began yesterday may still be running now.
        for (var offset = -1; offset <= SearchHorizonDays; offset++)
        {
            var day = today.AddDays(offset);
            if (holidaySet.Contains(day))
            {
                continue;
            }

            foreach (var shift in ordered)
            {
                if (shift.Day != day.DayOfWeek)
                {
                    continue;
                }

                var start = day.ToDateTime(shift.Start);
                var overnight = shift.End < shift.Start;
                var midnight = day.AddDays(1).ToDateTime(TimeOnly.MinValue);
                var end = overnight ? day.AddDays(1).ToDateTime(shift.End) : day.ToDateTime(shift.End);
                var spillOnHoliday = overnight && holidaySet.Contains(day.AddDays(1));

                // The part on the start day.
                var ownEnd = overnight ? midnight : end;
                if (Consume(start, ownEnd, ref cursor, ref remaining) is { } finished)
                {
                    return Suggested(finished, requiredTicks);
                }

                if (!overnight || end <= midnight)
                {
                    continue;
                }

                // The part after midnight of an overnight shift.
                if (spillOnHoliday)
                {
                    if (end > cursor)
                    {
                        return ProductionDueDateSuggestionV1.Unavailable(
                            DueDateSuggestionCodesV1.OvernightHolidaySpillUndecided, requiredTicks);
                    }

                    continue;
                }

                if (Consume(midnight, end, ref cursor, ref remaining) is { } spilled)
                {
                    return Suggested(spilled, requiredTicks);
                }
            }
        }

        return ProductionDueDateSuggestionV1.Unavailable(DueDateSuggestionCodesV1.NoWorkingTime, requiredTicks);
    }

    /// <summary>
    /// Consumes working time of [<paramref name="start"/>, <paramref name="end"/>) not yet counted (after
    /// <paramref name="cursor"/>). Returns the local finishing moment once nothing remains.
    /// </summary>
    private static DateTime? Consume(DateTime start, DateTime end, ref DateTime cursor, ref long remaining)
    {
        var from = start > cursor ? start : cursor;
        if (end <= from)
        {
            return null;
        }

        var available = (end - from).Ticks;
        if (remaining <= available)
        {
            var finished = from.AddTicks(remaining);
            remaining = 0;
            cursor = finished;
            return finished;
        }

        remaining -= available;
        cursor = end;
        return null;
    }

    /// <summary>The day of the last working tick: work ending exactly at midnight belongs to the day before.</summary>
    private static ProductionDueDateSuggestionV1 Suggested(DateTime finished, long requiredTicks) =>
        new(DueDateSuggestionStatusV1.Suggested, DateOnly.FromDateTime(finished.AddTicks(-1)), finished, requiredTicks,
            null);
}
