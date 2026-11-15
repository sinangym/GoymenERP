namespace GoymenERP.Modules.Organization.Domain.WorkCenters;

/// <summary>Why a proposed work-center calendar was refused.</summary>
public enum WorkCenterCalendarDefinitionError
{
    None = 0,
    NoShift = 1,
    TooManyShifts = 2,
    InvalidDay = 3,
    ShiftNotOnWholeMinute = 4,
    /// <summary>The window has no length: its start equals its end (KIM-D33). An earlier end spills into the next day.</summary>
    ShiftNotPositive = 5,

    /// <summary>Two windows share a minute, including the next-day part of a window that spills past midnight.</summary>
    ShiftsOverlap = 6,
    TooManyClosedDates = 7,
    DuplicateClosedDate = 8,
}

/// <summary>
/// The validated, canonical content of a work-center calendar (B-15): the weekly working windows and the dates on
/// which the work center is closed. Shifts are sorted by ISO weekday then start; closed dates ascending. Two
/// definitions with the same content are equal, so an unchanged save is recognized as a no-op.
/// <para>
/// This is master data only. URT-D29 keeps capacity and calendar computation (termin) out of v1: no module derives a
/// date from this calendar yet. Holidays are entered per work center as closed dates; no automatic holiday source is
/// assumed.
/// </para>
/// </summary>
public sealed class WorkCenterCalendarDefinition : IEquatable<WorkCenterCalendarDefinition>
{
    /// <summary>P7 (SINAN_KARARLARI_2026-10-21): a business day is the Europe/Istanbul calendar day.</summary>
    public const string BusinessTimeZoneId = "Europe/Istanbul";

    /// <summary>Technical payload bound per weekday; not a business rule.</summary>
    public const int MaximumShiftsPerDay = 8;

    /// <summary>Technical payload bound for one calendar (every weekday at its per-day bound).</summary>
    public const int MaximumShiftCount = MaximumShiftsPerDay * 7;

    /// <summary>Technical payload bound for the closed-date list; not a business rule.</summary>
    public const int MaximumClosedDateCount = 1000;

    /// <summary>URT-D11: the default working day starts at 08:00.</summary>
    public static readonly TimeOnly Urtd11DefaultShiftStart = new(8, 0);

    /// <summary>URT-D11: the default working day is 8 hours long (one shift).</summary>
    public static readonly TimeSpan Urtd11DefaultShiftLength = TimeSpan.FromHours(8);

    /// <summary>URT-D11: weekends are closed, so the default works Monday to Friday.</summary>
    public static readonly IReadOnlyList<DayOfWeek> Urtd11DefaultWorkingDays =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
    ];

    private WorkCenterCalendarDefinition(IReadOnlyList<WorkCenterShift> shifts, IReadOnlyList<DateOnly> closedDates)
    {
        Shifts = shifts;
        ClosedDates = closedDates;
    }

    /// <summary>
    /// URT-D11 (SINAN_KARARLARI_2026-11-01_URETIM): 08:00 start, 8 hours, single shift; weekends and holidays closed;
    /// changeable per line. A work center with no saved calendar uses this definition. It carries no closed dates
    /// because no holiday list has been approved as a default.
    /// </summary>
    public static WorkCenterCalendarDefinition Urtd11Default { get; } = new(
        Urtd11DefaultWorkingDays
            .Select(day => new WorkCenterShift(
                day,
                Urtd11DefaultShiftStart,
                Urtd11DefaultShiftStart.Add(Urtd11DefaultShiftLength)))
            .ToArray(),
        []);

    public IReadOnlyList<WorkCenterShift> Shifts { get; }

    public IReadOnlyList<DateOnly> ClosedDates { get; }

    public static WorkCenterCalendarDefinitionError TryCreate(
        IEnumerable<WorkCenterShift> shifts,
        IEnumerable<DateOnly> closedDates,
        out WorkCenterCalendarDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(shifts);
        ArgumentNullException.ThrowIfNull(closedDates);
        definition = null;

        var shiftList = shifts.ToList();
        if (shiftList.Count == 0)
        {
            return WorkCenterCalendarDefinitionError.NoShift;
        }

        if (shiftList.Count > MaximumShiftCount)
        {
            return WorkCenterCalendarDefinitionError.TooManyShifts;
        }

        foreach (var shift in shiftList)
        {
            if (!Enum.IsDefined(shift.Day))
            {
                return WorkCenterCalendarDefinitionError.InvalidDay;
            }

            if (!IsWholeMinute(shift.Start) || !IsWholeMinute(shift.End))
            {
                return WorkCenterCalendarDefinitionError.ShiftNotOnWholeMinute;
            }

            // KIM-D33: an end earlier than the start spills into the next day; only an equal pair is no window.
            if (shift.Start == shift.End)
            {
                return WorkCenterCalendarDefinitionError.ShiftNotPositive;
            }
        }

        var ordered = shiftList
            .OrderBy(shift => shift.IsoDayOrder)
            .ThenBy(shift => shift.Start)
            .ThenBy(shift => shift.End)
            .ToArray();
        if (AnyOverlap(ordered))
        {
            return WorkCenterCalendarDefinitionError.ShiftsOverlap;
        }

        if (ordered.GroupBy(shift => shift.Day).Any(day => day.Count() > MaximumShiftsPerDay))
        {
            return WorkCenterCalendarDefinitionError.TooManyShifts;
        }

        var dateList = closedDates.ToList();
        if (dateList.Count > MaximumClosedDateCount)
        {
            return WorkCenterCalendarDefinitionError.TooManyClosedDates;
        }

        var orderedDates = dateList.Order().ToArray();
        for (var index = 1; index < orderedDates.Length; index++)
        {
            if (orderedDates[index] == orderedDates[index - 1])
            {
                return WorkCenterCalendarDefinitionError.DuplicateClosedDate;
            }
        }

        definition = new WorkCenterCalendarDefinition(ordered, orderedDates);
        return WorkCenterCalendarDefinitionError.None;
    }

    public static WorkCenterCalendarDefinition Create(
        IEnumerable<WorkCenterShift> shifts,
        IEnumerable<DateOnly> closedDates)
    {
        var error = TryCreate(shifts, closedDates, out var definition);
        return error == WorkCenterCalendarDefinitionError.None
            ? definition!
            : throw new ArgumentException($"Çalışma takvimi geçersiz: {error}.", nameof(shifts));
    }

    public bool Equals(WorkCenterCalendarDefinition? other)
    {
        return other is not null
            && Shifts.SequenceEqual(other.Shifts)
            && ClosedDates.SequenceEqual(other.ClosedDates);
    }

    public override bool Equals(object? obj)
    {
        return obj is WorkCenterCalendarDefinition other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var shift in Shifts)
        {
            hash.Add(shift);
        }

        foreach (var date in ClosedDates)
        {
            hash.Add(date);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// True when two windows share a minute on the weekly clock. <paramref name="ordered"/> is sorted by week start, so
    /// each window only needs comparing with the next one, and the last window with the first across the week's end
    /// (a Sunday window that spills into Monday). Windows may touch (one ends at the minute the next starts).
    /// </summary>
    private static bool AnyOverlap(IReadOnlyList<WorkCenterShift> ordered)
    {
        for (var index = 1; index < ordered.Count; index++)
        {
            var previous = ordered[index - 1];
            if (previous.WeekStartMinute + previous.LengthMinutes > ordered[index].WeekStartMinute)
            {
                return true;
            }
        }

        var last = ordered[ordered.Count - 1];
        return last.WeekStartMinute + last.LengthMinutes - WorkCenterShift.MinutesPerWeek > ordered[0].WeekStartMinute;
    }

    private static bool IsWholeMinute(TimeOnly value)
    {
        return value.Second == 0 && value.Millisecond == 0 && value.Microsecond == 0 && value.Nanosecond == 0;
    }
}
