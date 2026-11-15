using GoymenERP.Modules.Organization.Domain.Common;

namespace GoymenERP.Modules.Organization.Domain.WorkCenters;

/// <summary>
/// The saved calendar of one work center (B-15, URT-D11 "hat bazında değiştirilebilir"). A work center has at most
/// one calendar; without one it works on <see cref="WorkCenterCalendarDefinition.Urtd11Default"/>.
/// <para>
/// The calendar head carries only identity and the revision counter. Each revision's shifts and closed dates are
/// append-only rows tagged with that revision; a change writes a new revision and never edits or deletes an earlier
/// row, so the version a consumer read always remains explainable.
/// </para>
/// </summary>
public sealed class WorkCenterCalendar : ICompanyOwnedRecord
{
    public const long FirstVersion = 1;

    private WorkCenterCalendar(Guid id, Guid companyId, Guid workCenterId, DateTimeOffset utcNow)
    {
        Id = RequireId(id, nameof(id));
        CompanyId = RequireId(companyId, nameof(companyId));
        WorkCenterId = RequireId(workCenterId, nameof(workCenterId));
        Version = FirstVersion;
        CreatedAtUtc = utcNow;
        UpdatedAtUtc = utcNow;
    }

    private WorkCenterCalendar()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid WorkCenterId { get; private set; }

    /// <summary>The current revision. Rows of the current definition carry this value as their revision.</summary>
    public long Version { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static WorkCenterCalendar Create(Guid id, Guid companyId, Guid workCenterId, DateTimeOffset utcNow)
    {
        return new WorkCenterCalendar(id, companyId, workCenterId, utcNow);
    }

    /// <summary>Opens the next revision; the caller then stores <see cref="RowsFor"/> for it.</summary>
    public void Revise(DateTimeOffset utcNow)
    {
        Version++;
        UpdatedAtUtc = utcNow;
    }

    /// <summary>The append-only rows that record <paramref name="definition"/> as the current revision.</summary>
    public WorkCenterCalendarRevisionRows RowsFor(WorkCenterCalendarDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new WorkCenterCalendarRevisionRows(
            definition.Shifts
                .Select(shift => new WorkCenterCalendarShiftRecord(
                    Guid.NewGuid(),
                    CompanyId,
                    Id,
                    Version,
                    shift))
                .ToArray(),
            definition.ClosedDates
                .Select(date => new WorkCenterCalendarClosedDateRecord(
                    Guid.NewGuid(),
                    CompanyId,
                    Id,
                    Version,
                    date))
                .ToArray());
    }

    private static Guid RequireId(Guid value, string parameterName)
    {
        return value == Guid.Empty
            ? throw new ArgumentException("Kimlik boş olamaz.", parameterName)
            : value;
    }
}

public sealed record WorkCenterCalendarRevisionRows(
    IReadOnlyList<WorkCenterCalendarShiftRecord> Shifts,
    IReadOnlyList<WorkCenterCalendarClosedDateRecord> ClosedDates);

/// <summary>One shift of one calendar revision. Append-only: never updated or deleted.</summary>
public sealed class WorkCenterCalendarShiftRecord : ICompanyOwnedRecord
{
    internal WorkCenterCalendarShiftRecord(
        Guid id,
        Guid companyId,
        Guid calendarId,
        long revision,
        WorkCenterShift shift)
    {
        Id = id;
        CompanyId = companyId;
        CalendarId = calendarId;
        Revision = revision;
        Day = shift.Day;
        StartTime = shift.Start;
        EndTime = shift.End;
    }

    private WorkCenterCalendarShiftRecord()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid CalendarId { get; private set; }

    public long Revision { get; private set; }

    public DayOfWeek Day { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public WorkCenterShift ToShift()
    {
        return new WorkCenterShift(Day, StartTime, EndTime);
    }
}

/// <summary>One closed date (holiday or shutdown) of one calendar revision. Append-only: never updated or deleted.</summary>
public sealed class WorkCenterCalendarClosedDateRecord : ICompanyOwnedRecord
{
    internal WorkCenterCalendarClosedDateRecord(
        Guid id,
        Guid companyId,
        Guid calendarId,
        long revision,
        DateOnly date)
    {
        Id = id;
        CompanyId = companyId;
        CalendarId = calendarId;
        Revision = revision;
        Date = date;
    }

    private WorkCenterCalendarClosedDateRecord()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid CalendarId { get; private set; }

    public long Revision { get; private set; }

    public DateOnly Date { get; private set; }
}
