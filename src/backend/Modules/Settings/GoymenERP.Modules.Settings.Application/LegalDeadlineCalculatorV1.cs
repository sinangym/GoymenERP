using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>Whether an obligation has a recorded calendar entry (and so a running counter).</summary>
public enum LegalDeadlineCounterStateV1
{
    /// <summary>No recorded entry: the counter does not run and the screen shows "tanımsız" (fail closed).</summary>
    Undefined = 1,

    /// <summary>A recorded entry: the next due date and the days left are computed.</summary>
    Defined = 2,
}

/// <summary>How the shown due date relates to the legal due date (YOL-D32).</summary>
public enum LegalDeadlineDueAdjustmentV1
{
    /// <summary>The legal due date is a business day; it is shown as is.</summary>
    None = 1,

    /// <summary>The legal due date falls on a weekend or a listed holiday/eve; the first business day after it is shown.</summary>
    MovedToBusinessDay = 2,

    /// <summary>A year the shift needs has no recorded holiday calendar (YOL-D22): no shift is applied and the screen says so.</summary>
    HolidayCalendarUndefined = 3,
}

/// <summary>
/// One obligation's counter. Dates are business dates (Turkey); null for an undefined obligation. <see cref="DueDate"/> is the
/// date shown and counted (YOL-D32: moved to the first business day when needed); <see cref="LegalDueDate"/> is the date the
/// calendar entry gives, shown as a note when they differ.
/// </summary>
public sealed record LegalDeadlineCounterV1(LegalDeadlineObligationV1 Obligation, LegalDeadlineCounterStateV1 State,
    DateOnly? PeriodStart, DateOnly? PeriodEnd, DateOnly? DueDate, int? DaysRemaining, DateOnly? LegalDueDate = null,
    LegalDeadlineDueAdjustmentV1? Adjustment = null);

/// <summary>
/// WP15 (1.16): the one deterministic calculator of legal deadline counters; the Work view and the screens only show its
/// answers. It knows no due day of its own: every number comes from the recorded calendar entry. For an entry it finds the
/// earliest period whose due date is on or after <c>today</c>. A period of <c>n</c> months ends at the end of a month whose
/// number is a multiple of <c>n</c>; the due date is <c>MonthsAfterPeriodEnd</c> months later, on <c>DueDayOfMonth</c>
/// clamped to that month's last day. YOL-D32: a legal due date on a Saturday, Sunday or a holiday/eve of the company holiday
/// calendar (YOL-D22) is moved to the first following business day; every year the move looks at must have a recorded holiday
/// calendar, otherwise nothing is moved and the counter says the calendar is undefined. The upcoming period is chosen by the
/// moved date. SAT-D125 (sales due days are not moved) is a different rule and is not touched here.
/// </summary>
public static class LegalDeadlineCalculatorV1
{
    /// <summary>Every catalogue obligation in catalogue order; those without a recorded entry are undefined.</summary>
    public static IReadOnlyList<LegalDeadlineCounterV1> Evaluate(SettingsLegalDeadlineCalendarV1 calendar, DateOnly today,
        IReadOnlyDictionary<int, IReadOnlySet<DateOnly>>? holidayYears = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        var entries = calendar.IsDefined
            ? calendar.Entries.GroupBy(entry => entry.Obligation).ToDictionary(group => group.Key, group => group.Single())
            : new Dictionary<LegalDeadlineObligationV1, SettingsLegalDeadlineEntryV1>();
        return Enum.GetValues<LegalDeadlineObligationV1>().Select(obligation => entries.TryGetValue(obligation, out var entry)
            ? Next(entry, today, holidayYears)
            : new LegalDeadlineCounterV1(obligation, LegalDeadlineCounterStateV1.Undefined, null, null, null, null)).ToArray();
    }

    /// <summary>The counter of one recorded entry.</summary>
    public static LegalDeadlineCounterV1 Next(SettingsLegalDeadlineEntryV1 entry, DateOnly today,
        IReadOnlyDictionary<int, IReadOnlySet<DateOnly>>? holidayYears = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!IsValid(entry)) throw new ArgumentOutOfRangeException(nameof(entry), "Yasal süre girdisi geçerli aralıkta değil.");
        // Start from a period that certainly falls due before today: the period ending one full cycle plus the offset earlier.
        var cursor = new DateOnly(today.Year, today.Month, 1).AddMonths(-(entry.PeriodMonths + entry.MonthsAfterPeriodEnd + 1));
        while (cursor.Month % entry.PeriodMonths != 0) cursor = cursor.AddMonths(1);
        while (true)
        {
            var periodEnd = new DateOnly(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
            var dueMonth = cursor.AddMonths(entry.MonthsAfterPeriodEnd);
            var due = new DateOnly(dueMonth.Year, dueMonth.Month,
                Math.Min(entry.DueDayOfMonth, DateTime.DaysInMonth(dueMonth.Year, dueMonth.Month)));
            var (shown, adjustment) = Shift(due, holidayYears);
            if (shown >= today)
            {
                var periodStart = new DateOnly(cursor.Year, cursor.Month, 1).AddMonths(1 - entry.PeriodMonths);
                return new(entry.Obligation, LegalDeadlineCounterStateV1.Defined, periodStart, periodEnd, shown,
                    shown.DayNumber - today.DayNumber, due, adjustment);
            }

            cursor = cursor.AddMonths(entry.PeriodMonths);
        }
    }

    /// <summary>YOL-D32: the first business day on or after <paramref name="due"/>, or the date itself when it cannot be decided.</summary>
    public static (DateOnly Shown, LegalDeadlineDueAdjustmentV1 Adjustment) Shift(DateOnly due,
        IReadOnlyDictionary<int, IReadOnlySet<DateOnly>>? holidayYears)
    {
        var day = due;
        while (true)
        {
            if (holidayYears is null || !holidayYears.TryGetValue(day.Year, out var holidays))
                return (due, LegalDeadlineDueAdjustmentV1.HolidayCalendarUndefined);
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidays.Contains(day))
                return (day, day == due ? LegalDeadlineDueAdjustmentV1.None : LegalDeadlineDueAdjustmentV1.MovedToBusinessDay);
            day = day.AddDays(1);
        }
    }

    public static bool IsValid(SettingsLegalDeadlineEntryV1 entry) =>
        Enum.IsDefined(entry.Obligation)
        && SettingsLegalDeadlineCalendarLimitsV1.PeriodMonths.Contains(entry.PeriodMonths)
        && entry.MonthsAfterPeriodEnd is >= 0 and <= SettingsLegalDeadlineCalendarLimitsV1.MaximumMonthsAfterPeriodEnd
        && entry.DueDayOfMonth is >= 1 and <= SettingsLegalDeadlineCalendarLimitsV1.MaximumDueDayOfMonth;
}
