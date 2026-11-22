using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>The state of one item's duration in an inbox answer.</summary>
internal static class WorkInboxDurationStatesV1
{
    /// <summary>The class length is recorded in Settings; the due time is computed.</summary>
    public const string Defined = "Defined";

    /// <summary>Settings holds no length for the class (YOL-D18 fail closed: "süre tanımlı değil").</summary>
    public const string Undefined = "Undefined";

    /// <summary>Settings could not be read now; nothing is assumed.</summary>
    public const string Unavailable = "Unavailable";
}

/// <summary>
/// The holiday calendar behind a business-day due time (YOL-D22). <see cref="Defined"/>: every year the count crosses has
/// a recorded list; <see cref="Undefined"/>: at least one year has none, so only weekends were deducted ("tatil takvimi
/// tanımsız"); <see cref="Unavailable"/>: the calendar could not be read, so only weekends were deducted.
/// </summary>
internal static class WorkInboxHolidayCalendarStatesV1
{
    public const string Defined = "Defined";
    public const string Undefined = "Undefined";
    public const string Unavailable = "Unavailable";
}

/// <summary>
/// The duration of one inbox item (YOL-D17/YOL-D18): its class, the class length in force and its Settings version,
/// the due time and whether it has passed. Only <see cref="WorkInboxDurationStatesV1.Defined"/> carries numbers.
/// <see cref="HolidayCalendar"/> is set only for a defined business-day duration (YOL-D22).
/// </summary>
internal sealed record WorkInboxDurationV1(
    string Class,
    string State,
    int? Limit,
    DateTimeOffset? DueAtUtc,
    bool? IsOverdue,
    string? PolicyVersion,
    string? HolidayCalendar);

/// <summary>The company holiday lists Work read for one inbox answer (YOL-D22); null when Settings could not answer.</summary>
internal sealed record WorkInboxHolidaysV1(IReadOnlyDictionary<int, IReadOnlySet<DateOnly>> DefinedYears);

/// <summary>
/// Computes an item's due time from the owner's "waiting since" time and the Settings class length. The hour class adds
/// whole hours. The business-day class counts Monday–Friday in Turkey local time (UTC+3, no daylight saving since 2016)
/// and, by YOL-D22, also skips the company's recorded official holidays and eves of each year; a year without a recorded
/// list deducts weekends only and the item says so ("tatil takvimi tanımsız"). A wait that starts on a non-business day
/// counts from the next business day 00:00; the due time keeps the local clock time. This is a display rule only (no
/// automatic approval, rejection or escalation is driven by it in W1-A).
/// </summary>
internal static class WorkInboxDurationCalculatorV1
{
    private static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);

    public static WorkInboxDurationV1 Compute(
        WorkApprovalDurationClassV1 durationClass,
        SettingsWorkApprovalDurationPolicyV1? policy,
        DateTimeOffset waitingSinceUtc,
        DateTimeOffset nowUtc,
        WorkInboxHolidaysV1? holidays = null)
    {
        var className = durationClass.ToString();
        if (policy is null)
        {
            return new(className, WorkInboxDurationStatesV1.Unavailable, null, null, null, null, null);
        }

        var (limit, version) = durationClass switch
        {
            WorkApprovalDurationClassV1.Hours => (policy.Hours, policy.HoursVersionText),
            WorkApprovalDurationClassV1.BusinessDays => (policy.BusinessDays, policy.BusinessDaysVersionText),
            _ => (null, null),
        };
        if (limit is not > 0 || version is null)
        {
            return new(className, WorkInboxDurationStatesV1.Undefined, null, null, null, null, null);
        }

        if (durationClass == WorkApprovalDurationClassV1.Hours)
        {
            var hoursDue = waitingSinceUtc.AddHours(limit.Value);
            return new(className, WorkInboxDurationStatesV1.Defined, limit, hoursDue, nowUtc > hoursDue, version, null);
        }

        var defined = holidays?.DefinedYears ?? new Dictionary<int, IReadOnlySet<DateOnly>>();
        var due = AddBusinessDays(waitingSinceUtc, limit.Value, defined, out var firstYear, out var lastYear);
        var calendar = holidays is null
            ? WorkInboxHolidayCalendarStatesV1.Unavailable
            : Enumerable.Range(firstYear, lastYear - firstYear + 1).All(defined.ContainsKey)
                ? WorkInboxHolidayCalendarStatesV1.Defined
                : WorkInboxHolidayCalendarStatesV1.Undefined;
        return new(className, WorkInboxDurationStatesV1.Defined, limit, due, nowUtc > due, version, calendar);
    }

    /// <summary>The local calendar years a business-day count starting at <paramref name="startUtc"/> may touch.</summary>
    internal static (int FirstYear, int LastYear) CandidateYears(DateTimeOffset startUtc, int days)
    {
        var year = startUtc.ToOffset(TurkeyOffset).Year;
        // Weekends plus at most the bounded holiday list never stretch a ≤ 30 business-day count beyond the next year.
        return (year, year + 1);
    }

    internal static DateTimeOffset AddBusinessDays(DateTimeOffset startUtc, int days) =>
        AddBusinessDays(startUtc, days, new Dictionary<int, IReadOnlySet<DateOnly>>(), out _, out _);

    internal static DateTimeOffset AddBusinessDays(
        DateTimeOffset startUtc,
        int days,
        IReadOnlyDictionary<int, IReadOnlySet<DateOnly>> holidays,
        out int firstYear,
        out int lastYear)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        var local = startUtc.ToOffset(TurkeyOffset);
        firstYear = local.Year;
        if (!IsBusinessDay(local, holidays))
        {
            do
            {
                local = new DateTimeOffset(local.Date.AddDays(1), TurkeyOffset);
            }
            while (!IsBusinessDay(local, holidays));
        }

        var remaining = days;
        while (remaining > 0)
        {
            local = local.AddDays(1);
            if (IsBusinessDay(local, holidays))
            {
                remaining--;
            }
        }
        lastYear = local.Year;
        return local.ToUniversalTime();
    }

    private static bool IsBusinessDay(DateTimeOffset local, IReadOnlyDictionary<int, IReadOnlySet<DateOnly>> holidays) =>
        local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
        && !(holidays.TryGetValue(local.Year, out var days) && days.Contains(DateOnly.FromDateTime(local.DateTime)));
}
