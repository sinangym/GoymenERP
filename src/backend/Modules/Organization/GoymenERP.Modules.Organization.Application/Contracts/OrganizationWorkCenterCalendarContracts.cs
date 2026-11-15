using System.Globalization;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Organization.Domain.WorkCenters;

namespace GoymenERP.Modules.Organization.Application.Contracts;

/// <summary>
/// One weekly working window on the wire. <paramref name="Day"/> is the English weekday token (<c>Monday</c> …
/// <c>Sunday</c>), the day the window starts on; <paramref name="Start"/> and <paramref name="End"/> are <c>HH:mm</c> in the
/// business time zone. KIM-D33: an <paramref name="End"/> earlier than <paramref name="Start"/> ends on the next day
/// (<c>22:00</c>–<c>06:00</c>); an equal pair is refused.
/// </summary>
public sealed record WorkCenterCalendarShiftV1(
    string Day,
    string Start,
    string End);

/// <summary>
/// GET /api/v1/organization/work-centers/{id}/calendar. <paramref name="Source"/> is <c>Default</c> when the work centre
/// has no saved calendar and therefore works on the URT-D11 default (then <paramref name="Version"/> is 0, the
/// expected version of the first save), or <c>WorkCenter</c> when a calendar was saved (its current revision).
/// <paramref name="ClosedDates"/> are <c>yyyy-MM-dd</c> business dates, ascending.
/// </summary>
public sealed record WorkCenterCalendarDetail(
    Guid WorkCenterId,
    string Source,
    long Version,
    string TimeZoneId,
    IReadOnlyList<WorkCenterCalendarShiftV1> Shifts,
    IReadOnlyList<string> ClosedDates);

/// <summary>
/// PUT /api/v1/organization/work-centers/{id}/calendar: replaces the work centre's whole calendar with a new revision.
/// <paramref name="ExpectedVersion"/> is the version the caller read (0 while the work centre is on the default).
/// Saving content equal to the current one is a stored, audited no-op that does not move the version.
/// </summary>
public sealed record SetWorkCenterCalendar(
    IReadOnlyList<WorkCenterCalendarShiftV1>? Shifts,
    IReadOnlyList<string>? ClosedDates,
    [property: JsonRequired] long ExpectedVersion,
    string Reason);

/// <summary>The work-centre calendar master (B-15). Reads and writes are fenced by the resolved company context.</summary>
public interface IOrganizationWorkCenterCalendarService
{
    /// <summary>
    /// The current calendar of a work centre inside the caller's <c>organization.work-centers.read</c> scope, or the
    /// URT-D11 default when none was saved. Null when the work centre is unknown, foreign or out of scope.
    /// </summary>
    Task<WorkCenterCalendarDetail?> GetAsync(Guid workCenterId, CancellationToken cancellationToken);

    /// <summary>
    /// Idempotent on its key; scope-checked on <see cref="OrganizationPermissionManifestV1.WorkCenterCalendarsWrite"/>;
    /// one audit event per applied command. Terminal answers: 204, code-less 404, 409 with the current version.
    /// </summary>
    Task<CommandExecutionResult> SetAsync(
        Guid workCenterId,
        SetWorkCenterCalendar command,
        CommandMetadata metadata,
        CancellationToken cancellationToken);
}

/// <summary>
/// The request-shape rules shared by the endpoint (400 before anything runs) and the service (which refuses the same
/// shapes as a programming error), and the one mapping from a definition to the wire shape.
/// </summary>
public static class OrganizationWorkCenterCalendarRules
{
    public const string DefaultSource = "Default";
    public const string WorkCenterSource = "WorkCenter";

    /// <summary>The expected version of the first save, while the work centre is on the default.</summary>
    public const long DefaultVersion = 0;

    public const string TimeFormat = "HH:mm";
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>The weekday tokens exactly as the calendar read publishes them (<c>DayOfWeek.ToString()</c>).</summary>
    public static readonly IReadOnlyList<string> DayTokens = Enum.GetNames<DayOfWeek>();

    /// <summary>
    /// Parses and validates a request. False when a field is missing or malformed, or when the content breaks a
    /// definition rule (<paramref name="error"/> then names it); the endpoint answers 400 either way.
    /// </summary>
    public static bool TryParse(
        SetWorkCenterCalendar? command,
        out WorkCenterCalendarDefinition? definition,
        out WorkCenterCalendarDefinitionError error)
    {
        definition = null;
        error = WorkCenterCalendarDefinitionError.None;
        if (command?.Shifts is null || command.ClosedDates is null
            || command.Shifts.Count > WorkCenterCalendarDefinition.MaximumShiftCount
            || command.ClosedDates.Count > WorkCenterCalendarDefinition.MaximumClosedDateCount)
        {
            return false;
        }

        var shifts = new List<WorkCenterShift>(command.Shifts.Count);
        foreach (var shift in command.Shifts)
        {
            if (shift is null
                || !TryParseDay(shift.Day, out var day)
                || !TryParseTime(shift.Start, out var start)
                || !TryParseTime(shift.End, out var end))
            {
                return false;
            }

            shifts.Add(new WorkCenterShift(day, start, end));
        }

        var dates = new List<DateOnly>(command.ClosedDates.Count);
        foreach (var text in command.ClosedDates)
        {
            if (!TryParseDate(text, out var date))
            {
                return false;
            }

            dates.Add(date);
        }

        error = WorkCenterCalendarDefinition.TryCreate(shifts, dates, out definition);
        return error == WorkCenterCalendarDefinitionError.None;
    }

    public static WorkCenterCalendarDetail ToDetail(
        Guid workCenterId,
        string source,
        long version,
        WorkCenterCalendarDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new WorkCenterCalendarDetail(
            workCenterId,
            source,
            version,
            WorkCenterCalendarDefinition.BusinessTimeZoneId,
            definition.Shifts.Select(ToWire).ToArray(),
            definition.ClosedDates.Select(FormatDate).ToArray());
    }

    public static WorkCenterCalendarShiftV1 ToWire(WorkCenterShift shift) =>
        new(shift.Day.ToString(), FormatTime(shift.Start), FormatTime(shift.End));

    public static string FormatTime(TimeOnly value) => value.ToString(TimeFormat, CultureInfo.InvariantCulture);

    public static string FormatDate(DateOnly value) => value.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static bool TryParseDay(string? text, out DayOfWeek day)
    {
        day = default;
        return text is not null
            && DayTokens.Contains(text, StringComparer.Ordinal)
            && Enum.TryParse(text, ignoreCase: false, out day);
    }

    private static bool TryParseTime(string? text, out TimeOnly value)
    {
        value = default;
        return text is not null
            && TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static bool TryParseDate(string? text, out DateOnly value)
    {
        value = default;
        return text is not null
            && DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }
}
