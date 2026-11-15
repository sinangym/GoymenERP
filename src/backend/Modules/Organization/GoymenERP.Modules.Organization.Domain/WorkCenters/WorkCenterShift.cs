namespace GoymenERP.Modules.Organization.Domain.WorkCenters;

/// <summary>
/// One working window of a work center on a weekday, in the business time zone
/// (<see cref="WorkCenterCalendarDefinition.BusinessTimeZoneId"/>). The window starts and ends on the same calendar day
/// and both ends are whole minutes; an overnight window is not modelled in v1.
/// </summary>
public readonly record struct WorkCenterShift(DayOfWeek Day, TimeOnly Start, TimeOnly End)
{
    /// <summary>ISO weekday order (Monday first) used for the canonical sort of a definition.</summary>
    public int IsoDayOrder => Day == DayOfWeek.Sunday ? 7 : (int)Day;
}
