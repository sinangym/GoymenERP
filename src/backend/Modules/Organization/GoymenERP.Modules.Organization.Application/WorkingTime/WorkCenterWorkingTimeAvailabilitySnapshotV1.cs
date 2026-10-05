using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Modules.Organization.Application.WorkingTime;

/// <summary>One already-resolved half-open UTC availability interval.</summary>
public sealed record WorkCenterWorkingTimeIntervalV1(
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc);

/// <summary>
/// A complete, immutable working-time answer for the exact requested UTC window. The source coverage
/// must equal that window. Calendar and timezone provenance are retained losslessly; this type does
/// not choose local-time, DST, weekly-rule or dated-override semantics.
/// </summary>
public sealed class WorkCenterWorkingTimeAvailabilitySnapshotV1
{
    private WorkCenterWorkingTimeAvailabilitySnapshotV1(
        OrganizationWorkCenterWorkingTimeAvailabilityQueryV1 query,
        Guid calendarId,
        long calendarVersion,
        string calendarSourceRevision,
        string timeZoneId,
        long timeZoneBindingVersion,
        string timeZoneSourceRevision,
        IReadOnlyList<WorkCenterWorkingTimeIntervalV1> intervals)
    {
        CompanyId = query.CompanyId;
        WorkCenterId = query.WorkCenterId;
        CalendarId = calendarId;
        CalendarVersion = calendarVersion;
        CalendarSourceRevision = calendarSourceRevision;
        TimeZoneId = timeZoneId;
        TimeZoneBindingVersion = timeZoneBindingVersion;
        TimeZoneSourceRevision = timeZoneSourceRevision;
        FromUtc = query.FromUtc;
        ThroughUtc = query.ThroughUtc;
        Intervals = intervals;
    }

    public Guid CompanyId { get; }

    public Guid WorkCenterId { get; }

    public Guid CalendarId { get; }

    public long CalendarVersion { get; }

    public string CalendarSourceRevision { get; }

    public string TimeZoneId { get; }

    public long TimeZoneBindingVersion { get; }

    public string TimeZoneSourceRevision { get; }

    public DateTimeOffset FromUtc { get; }

    public DateTimeOffset ThroughUtc { get; }

    public IReadOnlyList<WorkCenterWorkingTimeIntervalV1> Intervals { get; }

    /// <summary>
    /// Builds an available answer only from a source row bound to the exact query identity and complete
    /// coverage. The interval sequence is validated as supplied and defensively copied; it is not sorted
    /// or normalized on the caller's behalf.
    /// </summary>
    public static WorkCenterWorkingTimeAvailabilitySnapshotV1 Create(
        OrganizationWorkCenterWorkingTimeAvailabilityQueryV1 query,
        Guid sourceCompanyId,
        Guid sourceWorkCenterId,
        Guid calendarId,
        long calendarVersion,
        string calendarSourceRevision,
        string timeZoneId,
        long timeZoneBindingVersion,
        string timeZoneSourceRevision,
        DateTimeOffset coveredFromUtc,
        DateTimeOffset coveredThroughUtc,
        IEnumerable<WorkCenterWorkingTimeIntervalV1> intervals)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(intervals);

        if (sourceCompanyId != query.CompanyId)
        {
            throw new ArgumentException("Takvim şirketi sorgu şirketiyle eşleşmelidir.", nameof(sourceCompanyId));
        }

        if (sourceWorkCenterId != query.WorkCenterId)
        {
            throw new ArgumentException("Takvim iş merkezi sorgu iş merkeziyle eşleşmelidir.", nameof(sourceWorkCenterId));
        }

        if (calendarId == Guid.Empty)
        {
            throw new ArgumentException("Takvim kimliği zorunludur.", nameof(calendarId));
        }

        if (calendarVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(calendarVersion));
        }

        RequireSource(calendarSourceRevision, nameof(calendarSourceRevision));
        RequireSource(timeZoneId, nameof(timeZoneId));

        if (timeZoneBindingVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeZoneBindingVersion));
        }

        RequireSource(timeZoneSourceRevision, nameof(timeZoneSourceRevision));
        RequireUtc(coveredFromUtc, nameof(coveredFromUtc));
        RequireUtc(coveredThroughUtc, nameof(coveredThroughUtc));
        if (coveredFromUtc != query.FromUtc || coveredThroughUtc != query.ThroughUtc)
        {
            throw new ArgumentException("Kaynak kapsamı sorgu aralığının tamamını karşılamalıdır.", nameof(coveredThroughUtc));
        }

        var owned = intervals.ToArray();
        for (var index = 0; index < owned.Length; index++)
        {
            var interval = owned[index] ?? throw new ArgumentException("Çalışma zamanı aralığı boş olamaz.", nameof(intervals));
            RequireUtc(interval.StartsAtUtc, nameof(intervals));
            RequireUtc(interval.EndsAtUtc, nameof(intervals));
            if (interval.StartsAtUtc >= interval.EndsAtUtc)
            {
                throw new ArgumentException("Çalışma zamanı aralığı pozitif olmalıdır.", nameof(intervals));
            }

            if (interval.StartsAtUtc < query.FromUtc || interval.EndsAtUtc > query.ThroughUtc)
            {
                throw new ArgumentException("Çalışma zamanı aralığı sorgu kapsamı içinde olmalıdır.", nameof(intervals));
            }

            if (index > 0 && interval.StartsAtUtc < owned[index - 1].EndsAtUtc)
            {
                throw new ArgumentException("Çalışma zamanı aralıkları sıralı ve çakışmasız olmalıdır.", nameof(intervals));
            }
        }

        return new WorkCenterWorkingTimeAvailabilitySnapshotV1(
            query,
            calendarId,
            calendarVersion,
            calendarSourceRevision,
            timeZoneId,
            timeZoneBindingVersion,
            timeZoneSourceRevision,
            Array.AsReadOnly(owned));
    }

    private static void RequireSource(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Kaynak kimliği ve sürümü zorunludur.", parameterName);
        }
    }

    private static void RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Çalışma zamanı değerleri UTC olmalıdır.", parameterName);
        }
    }
}
