namespace GoymenERP.Modules.Organization.Domain.WorkCenters;

/// <summary>
/// One working window of a work center that starts on a weekday, in the business time zone
/// (<see cref="WorkCenterCalendarDefinition.BusinessTimeZoneId"/>). Both ends are whole minutes.
/// <para>
/// KIM-D33 (SINAN_KARARLARI_2026-11-15_DALGA1_EK): an <see cref="End"/> earlier than <see cref="Start"/> spills into
/// the next day (22:00–06:00 runs from 22:00 on <see cref="Day"/> to 06:00 on the following day; 16:00–00:00 ends at
/// midnight). <see cref="Start"/> equal to <see cref="End"/> is not a window. The shift belongs to the day it starts on;
/// splitting its capacity across both days is the capacity calculation's job, not this master's.
/// </para>
/// </summary>
public readonly record struct WorkCenterShift(DayOfWeek Day, TimeOnly Start, TimeOnly End)
{
    /// <summary>Minutes in one calendar day.</summary>
    public static readonly int MinutesPerDay = (int)TimeSpan.FromDays(1).TotalMinutes;

    /// <summary>Minutes in one week, the period the weekly windows repeat with.</summary>
    public static readonly int MinutesPerWeek = MinutesPerDay * Enum.GetValues<DayOfWeek>().Length;

    /// <summary>ISO weekday order (Monday first) used for the canonical sort of a definition.</summary>
    public int IsoDayOrder => Day == DayOfWeek.Sunday ? 7 : (int)Day;

    /// <summary>KIM-D33: the window ends on the next day (its end is earlier than its start).</summary>
    public bool EndsNextDay => End < Start;

    /// <summary>Minute offset of the start inside the ISO week (Monday 00:00 is 0).</summary>
    public int WeekStartMinute => ((IsoDayOrder - 1) * MinutesPerDay) + MinuteOfDay(Start);

    /// <summary>
    /// Length of the window in minutes; positive for every valid window (0 when <see cref="Start"/> equals
    /// <see cref="End"/>, which is refused).
    /// </summary>
    public int LengthMinutes => EndsNextDay
        ? MinutesPerDay - MinuteOfDay(Start) + MinuteOfDay(End)
        : MinuteOfDay(End) - MinuteOfDay(Start);

    private static int MinuteOfDay(TimeOnly value) => (int)value.ToTimeSpan().TotalMinutes;
}
