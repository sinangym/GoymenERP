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

/// <summary>One obligation's counter. Dates are business dates (Turkey); null for an undefined obligation.</summary>
public sealed record LegalDeadlineCounterV1(LegalDeadlineObligationV1 Obligation, LegalDeadlineCounterStateV1 State,
    DateOnly? PeriodStart, DateOnly? PeriodEnd, DateOnly? DueDate, int? DaysRemaining);

/// <summary>
/// WP15 (1.16): the one deterministic calculator of legal deadline counters; the Work view and the screens only show its
/// answers. It knows no due day of its own: every number comes from the recorded calendar entry. For an entry it finds the
/// earliest period whose due date is on or after <c>today</c>. A period of <c>n</c> months ends at the end of a month whose
/// number is a multiple of <c>n</c>; the due date is <c>MonthsAfterPeriodEnd</c> months later, on <c>DueDayOfMonth</c>
/// clamped to that month's last day. No holiday/weekend shift (NEEDS-SINAN WP15-2).
/// </summary>
public static class LegalDeadlineCalculatorV1
{
    /// <summary>Every catalogue obligation in catalogue order; those without a recorded entry are undefined.</summary>
    public static IReadOnlyList<LegalDeadlineCounterV1> Evaluate(SettingsLegalDeadlineCalendarV1 calendar, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        var entries = calendar.IsDefined
            ? calendar.Entries.GroupBy(entry => entry.Obligation).ToDictionary(group => group.Key, group => group.Single())
            : new Dictionary<LegalDeadlineObligationV1, SettingsLegalDeadlineEntryV1>();
        return Enum.GetValues<LegalDeadlineObligationV1>().Select(obligation => entries.TryGetValue(obligation, out var entry)
            ? Next(entry, today)
            : new LegalDeadlineCounterV1(obligation, LegalDeadlineCounterStateV1.Undefined, null, null, null, null)).ToArray();
    }

    /// <summary>The counter of one recorded entry.</summary>
    public static LegalDeadlineCounterV1 Next(SettingsLegalDeadlineEntryV1 entry, DateOnly today)
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
            if (due >= today)
            {
                var periodStart = new DateOnly(cursor.Year, cursor.Month, 1).AddMonths(1 - entry.PeriodMonths);
                return new(entry.Obligation, LegalDeadlineCounterStateV1.Defined, periodStart, periodEnd, due,
                    due.DayNumber - today.DayNumber);
            }

            cursor = cursor.AddMonths(entry.PeriodMonths);
        }
    }

    public static bool IsValid(SettingsLegalDeadlineEntryV1 entry) =>
        Enum.IsDefined(entry.Obligation)
        && SettingsLegalDeadlineCalendarLimitsV1.PeriodMonths.Contains(entry.PeriodMonths)
        && entry.MonthsAfterPeriodEnd is >= 0 and <= SettingsLegalDeadlineCalendarLimitsV1.MaximumMonthsAfterPeriodEnd
        && entry.DueDayOfMonth is >= 1 and <= SettingsLegalDeadlineCalendarLimitsV1.MaximumDueDayOfMonth;
}
