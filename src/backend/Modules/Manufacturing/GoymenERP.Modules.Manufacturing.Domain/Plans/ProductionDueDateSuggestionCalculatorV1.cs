using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

// URT-S10 (F-010b, B-15, OPEN-007 note): the calendar-based termin SUGGESTION. The work center's shift calendar
// (Organization's planning reference) and the hourly capacity (a Settings parameter value, never a number in code) give
// the day on which the plan's quantity would be finished. The result is only a proposal: the user confirms or changes it
// through the existing hand-entered termin command (F-010a, URT-D29); nothing is stored here.
//
// URT-SC33 (SINAN-CEVAP 33):
// - URT-D86: the queued planned work on the work center is counted first, then this plan (the caller passes the queued
//   input kg; which plans form the queue is the application's reversible rule);
// - URT-D87: the capacity is INPUT kg per hour; the plan's recipe input kg is divided by it;
// - URT-D88: an overnight shift belongs to its start day; when the start day is a working day the whole shift counts,
//   also the hours after midnight that fall on a holiday (a holiday START day is still skipped);
// - URT-D89: the optional safety margin of the work center (working hours added after the work, and/or working days
//   added after the completion day); no margin is zero. The capacity has no default (the caller refuses without it).
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

    /// <summary>The queued kg or the safety margin is negative (a caller fault surfaced as a code, never a guessed day).</summary>
    public const string InputInvalid = "due_date_input_invalid";
}

/// <summary>
/// A suggestion: the local calendar day on which the last working minute (plus the safety margin) falls, the local moment
/// the work (queue + plan + margin hours) would finish, the plan's own working time (ticks, exact; minutes rounded up for
/// display only) and the whole walked working time including the queue and the margin hours.
/// </summary>
public sealed record ProductionDueDateSuggestionV1(
    DueDateSuggestionStatusV1 Status,
    DateOnly? SuggestedDueDate,
    DateTime? CompletesAtLocal,
    long? RequiredWorkingTicks,
    string? ReasonCode,
    long? QueueWorkingTicks = null,
    long? TotalWorkingTicks = null)
{
    /// <summary>The required working time in whole minutes, rounded UP (a display figure; the calculation uses ticks).</summary>
    public long? RequiredWorkingMinutes => MinutesUp(RequiredWorkingTicks);

    /// <summary>The queue's working time in whole minutes, rounded UP (display only).</summary>
    public long? QueueWorkingMinutes => MinutesUp(QueueWorkingTicks);

    /// <summary>The whole walked working time (queue + plan + margin hours) in whole minutes, rounded UP (display only).</summary>
    public long? TotalWorkingMinutes => MinutesUp(TotalWorkingTicks);

    public static ProductionDueDateSuggestionV1 Unavailable(string reasonCode, long? requiredTicks = null) =>
        new(DueDateSuggestionStatusV1.Unavailable, null, null, requiredTicks, reasonCode);

    private static long? MinutesUp(long? value) => value is { } ticks
        ? ticks / TimeSpan.TicksPerMinute + (ticks % TimeSpan.TicksPerMinute == 0 ? 0 : 1)
        : null;
}

/// <summary>
/// The work center's safety margin (URT-D89) as the calculator reads it: working time added after the work and whole
/// working days (non-holiday days with at least one shift) added after the completion day. <see cref="None"/> is the
/// "no margin entered" answer, which is zero.
/// </summary>
public sealed record DueDateSafetyMarginV1(TimeSpan WorkingTime, int WorkingDays)
{
    public static DueDateSafetyMarginV1 None { get; } = new(TimeSpan.Zero, 0);
}

/// <summary>
/// The one deterministic termin suggestion calculator (URT-S10, URT-SC33). It walks the calendar forward from <c>now</c>
/// in the calendar's own time zone: the rest of a running shift counts, a holiday start day is skipped (a shift belongs to
/// its start day, URT-D88), overlapping shifts are counted once, and an overnight shift is split at midnight. The queue is
/// walked first, then the plan, then the margin hours; the margin days follow the completion day. Pure: the clock, zone,
/// calendar, queue, capacity and margin are all inputs.
/// </summary>
public static class ProductionDueDateSuggestionCalculatorV1
{
    /// <summary>
    /// A technical search bound, not a business rule: a calendar that cannot finish the work within this many days from
    /// today gives no suggestion (<see cref="DueDateSuggestionCodesV1.NoWorkingTime"/>) instead of searching forever. The
    /// user still enters any termin by hand.
    /// </summary>
    public const int SearchHorizonDays = 366;

    /// <summary>The URT-S10 form: no queue and no margin.</summary>
    public static ProductionDueDateSuggestionV1 Calculate(
        DateTimeOffset now,
        TimeZoneInfo zone,
        IReadOnlyList<DueDateCalendarShiftV1> shifts,
        IReadOnlyCollection<DateOnly> holidays,
        Kilograms required,
        decimal capacityKgPerHour) =>
        Calculate(now, zone, shifts, holidays, Kilograms.Zero, required, capacityKgPerHour, DueDateSafetyMarginV1.None);

    public static ProductionDueDateSuggestionV1 Calculate(
        DateTimeOffset now,
        TimeZoneInfo zone,
        IReadOnlyList<DueDateCalendarShiftV1> shifts,
        IReadOnlyCollection<DateOnly> holidays,
        Kilograms queued,
        Kilograms required,
        decimal capacityKgPerHour,
        DueDateSafetyMarginV1 margin)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(shifts);
        ArgumentNullException.ThrowIfNull(holidays);
        ArgumentNullException.ThrowIfNull(margin);
        if (capacityKgPerHour <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(capacityKgPerHour), capacityKgPerHour,
                "Saatlik kapasite pozitif olmalıdır.");
        }

        if (queued.Value < 0m || required.Value < 0m || margin.WorkingTime < TimeSpan.Zero || margin.WorkingDays < 0)
        {
            return ProductionDueDateSuggestionV1.Unavailable(DueDateSuggestionCodesV1.InputInvalid);
        }

        if (shifts.Any(shift => shift is null || !Enum.IsDefined(shift.Day) || shift.Start == shift.End))
        {
            return ProductionDueDateSuggestionV1.Unavailable(DueDateSuggestionCodesV1.CalendarInvalid);
        }

        var localNow = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var horizonTicks = (decimal)TimeSpan.TicksPerDay * (SearchHorizonDays + 1);
        var exactPlanTicks = required.Value * TimeSpan.TicksPerHour / capacityKgPerHour;
        var exactQueueTicks = queued.Value * TimeSpan.TicksPerHour / capacityKgPerHour;
        var exactTotalTicks = exactQueueTicks + exactPlanTicks + margin.WorkingTime.Ticks;
        if (exactTotalTicks > horizonTicks || margin.WorkingDays > SearchHorizonDays)
        {
            return ProductionDueDateSuggestionV1.Unavailable(DueDateSuggestionCodesV1.NoWorkingTime);
        }

        var requiredTicks = (long)decimal.Ceiling(exactPlanTicks);
        var queueTicks = (long)decimal.Ceiling(exactQueueTicks);
        var totalTicks = (long)decimal.Ceiling(exactTotalTicks);
        var holidaySet = holidays as IReadOnlySet<DateOnly> ?? holidays.ToHashSet();
        var workingWeekdays = shifts.Select(shift => shift.Day).ToHashSet();

        DateTime finished;
        if (totalTicks == 0)
        {
            finished = localNow;
        }
        else if (shifts.Count == 0 || Walk(localNow, shifts, holidaySet, totalTicks) is not { } walked)
        {
            return ProductionDueDateSuggestionV1.Unavailable(DueDateSuggestionCodesV1.NoWorkingTime, requiredTicks)
                with { QueueWorkingTicks = queueTicks, TotalWorkingTicks = totalTicks };
        }
        else
        {
            finished = walked;
        }

        // The day of the last working tick: work ending exactly at midnight belongs to the day before.
        var completionDay = totalTicks == 0 ? DateOnly.FromDateTime(finished) : DateOnly.FromDateTime(finished.AddTicks(-1));
        if (AddWorkingDays(completionDay, margin.WorkingDays, workingWeekdays, holidaySet,
                DateOnly.FromDateTime(localNow).AddDays(SearchHorizonDays * 2)) is not { } dueDay)
        {
            return ProductionDueDateSuggestionV1.Unavailable(DueDateSuggestionCodesV1.NoWorkingTime, requiredTicks)
                with { QueueWorkingTicks = queueTicks, TotalWorkingTicks = totalTicks };
        }

        return new ProductionDueDateSuggestionV1(DueDateSuggestionStatusV1.Suggested, dueDay, finished, requiredTicks,
            null, queueTicks, totalTicks);
    }

    /// <summary>
    /// Walks <paramref name="ticks"/> of working time forward from <paramref name="localNow"/>. A holiday START day is
    /// skipped; the after-midnight part of an overnight shift that started on a working day counts even on a holiday
    /// (URT-D88). Returns the local finishing moment, or null when the horizon is not enough.
    /// </summary>
    private static DateTime? Walk(DateTime localNow, IReadOnlyList<DueDateCalendarShiftV1> shifts,
        IReadOnlySet<DateOnly> holidays, long ticks)
    {
        var ordered = shifts.OrderBy(shift => shift.Start).ToArray();
        var today = DateOnly.FromDateTime(localNow);
        var remaining = ticks;
        var cursor = localNow;

        // Start one day back: an overnight shift that began yesterday may still be running now.
        for (var offset = -1; offset <= SearchHorizonDays; offset++)
        {
            var day = today.AddDays(offset);
            if (holidays.Contains(day))
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

                // The part on the start day.
                var ownEnd = overnight ? midnight : end;
                if (Consume(start, ownEnd, ref cursor, ref remaining) is { } finished)
                {
                    return finished;
                }

                // The part after midnight of an overnight shift (counted whatever the next day is, URT-D88).
                if (overnight && end > midnight && Consume(midnight, end, ref cursor, ref remaining) is { } spilled)
                {
                    return spilled;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The day <paramref name="count"/> working days after <paramref name="from"/> (a working day is a non-holiday day whose
    /// weekday has a shift); zero returns <paramref name="from"/>. Null when <paramref name="limit"/> is passed first.
    /// </summary>
    private static DateOnly? AddWorkingDays(DateOnly from, int count, IReadOnlySet<DayOfWeek> workingWeekdays,
        IReadOnlySet<DateOnly> holidays, DateOnly limit)
    {
        var day = from;
        for (var counted = 0; counted < count;)
        {
            day = day.AddDays(1);
            if (day > limit)
            {
                return null;
            }

            if (workingWeekdays.Contains(day.DayOfWeek) && !holidays.Contains(day))
            {
                counted++;
            }
        }

        return day;
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
}
