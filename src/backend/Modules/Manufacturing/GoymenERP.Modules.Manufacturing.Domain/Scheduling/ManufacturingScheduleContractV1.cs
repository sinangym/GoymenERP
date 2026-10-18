namespace GoymenERP.Modules.Manufacturing.Domain.Scheduling;

public enum ManufacturingScheduleOutcomeV1
{
    Ready = 1,
    Unavailable = 2,
    Conflict = 3,
}

public static class ManufacturingScheduleCodesV1
{
    public const string InputUnavailable = "manufacturing_schedule_input_unavailable";
    public const string CapacityUnavailable = "manufacturing_schedule_capacity_unavailable";
    public const string InputConflict = "manufacturing_schedule_input_conflict";
}

/// <summary>
/// One applied queue item. Its grain is an executable ProductionOrder, never a draft or approved
/// plan. All ready instants are UTC facts supplied by their owners; a missing fact stays unknown.
/// </summary>
public sealed record ManufacturingScheduleQueueItemV1(
    Guid CompanyId,
    Guid WorkCenterId,
    Guid ProductionOrderId,
    long ProductionOrderVersion,
    DateTimeOffset? MaterialReadyAtUtc,
    DateTimeOffset? ApprovalReadyAtUtc,
    DateTimeOffset? PredecessorReadyAtUtc,
    DateTimeOffset? WorkCenterReadyAtUtc);

/// <summary>One already-resolved half-open UTC working interval.</summary>
public sealed record ManufacturingScheduleAvailabilityIntervalV1(
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc);

/// <summary>
/// Organization-owned availability copied into the Manufacturing calculation boundary with its
/// calendar and timezone provenance. No recurrence, DST or shift rule is resolved here.
/// </summary>
public sealed class ManufacturingScheduleAvailabilitySnapshotV1
{
    public ManufacturingScheduleAvailabilitySnapshotV1(
        Guid companyId,
        Guid workCenterId,
        Guid calendarId,
        long? calendarVersion,
        string? calendarSourceRevision,
        string? timeZoneId,
        long? timeZoneBindingVersion,
        string? timeZoneSourceRevision,
        DateTimeOffset fromUtc,
        DateTimeOffset throughUtc,
        IEnumerable<ManufacturingScheduleAvailabilityIntervalV1> intervals)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        CompanyId = companyId;
        WorkCenterId = workCenterId;
        CalendarId = calendarId;
        CalendarVersion = calendarVersion;
        CalendarSourceRevision = calendarSourceRevision;
        TimeZoneId = timeZoneId;
        TimeZoneBindingVersion = timeZoneBindingVersion;
        TimeZoneSourceRevision = timeZoneSourceRevision;
        FromUtc = fromUtc;
        ThroughUtc = throughUtc;
        Intervals = Array.AsReadOnly(intervals.ToArray());
    }

    public Guid CompanyId { get; }
    public Guid WorkCenterId { get; }
    public Guid CalendarId { get; }
    public long? CalendarVersion { get; }
    public string? CalendarSourceRevision { get; }
    public string? TimeZoneId { get; }
    public long? TimeZoneBindingVersion { get; }
    public string? TimeZoneSourceRevision { get; }
    public DateTimeOffset FromUtc { get; }
    public DateTimeOffset ThroughUtc { get; }
    public IReadOnlyList<ManufacturingScheduleAvailabilityIntervalV1> Intervals { get; }
}

/// <summary>
/// One explicit duration in minutes with the exact profile/source version that supplied it. A null
/// duration or missing provenance means unavailable; it is never interpreted as zero.
/// </summary>
public sealed record ManufacturingScheduleDurationSnapshotV1(
    long? Minutes,
    Guid? SourceId,
    long? SourceVersion,
    string? SourceRevision);

public sealed record ManufacturingScheduleInputV1(
    ManufacturingScheduleQueueItemV1? QueueItem,
    ManufacturingScheduleAvailabilitySnapshotV1? Availability,
    ManufacturingScheduleDurationSnapshotV1? Setup,
    ManufacturingScheduleDurationSnapshotV1? Changeover,
    ManufacturingScheduleDurationSnapshotV1? Run,
    ManufacturingScheduleDurationSnapshotV1? Cleanup);

public sealed record ManufacturingScheduleResultV1(
    ManufacturingScheduleOutcomeV1 Outcome,
    string? Code,
    Guid ProductionOrderId,
    long ProductionOrderVersion,
    DateTimeOffset? EffectiveReadyAtUtc,
    DateTimeOffset? PlannedStartUtc,
    DateTimeOffset? PlannedFinishUtc,
    long? TotalWorkingMinutes)
{
    public static ManufacturingScheduleResultV1 Ready(
        ManufacturingScheduleQueueItemV1 item,
        DateTimeOffset effectiveReadyAtUtc,
        DateTimeOffset plannedStartUtc,
        DateTimeOffset plannedFinishUtc,
        long totalWorkingMinutes) =>
        new(
            ManufacturingScheduleOutcomeV1.Ready,
            null,
            item.ProductionOrderId,
            item.ProductionOrderVersion,
            effectiveReadyAtUtc,
            plannedStartUtc,
            plannedFinishUtc,
            totalWorkingMinutes);

    public static ManufacturingScheduleResultV1 Unavailable(
        ManufacturingScheduleQueueItemV1? item,
        string code) =>
        Failed(ManufacturingScheduleOutcomeV1.Unavailable, item, code);

    public static ManufacturingScheduleResultV1 Conflict(
        ManufacturingScheduleQueueItemV1? item,
        string code) =>
        Failed(ManufacturingScheduleOutcomeV1.Conflict, item, code);

    private static ManufacturingScheduleResultV1 Failed(
        ManufacturingScheduleOutcomeV1 outcome,
        ManufacturingScheduleQueueItemV1? item,
        string code) =>
        new(
            outcome,
            code,
            item?.ProductionOrderId ?? Guid.Empty,
            item?.ProductionOrderVersion ?? 0,
            null,
            null,
            null,
            null);
}
