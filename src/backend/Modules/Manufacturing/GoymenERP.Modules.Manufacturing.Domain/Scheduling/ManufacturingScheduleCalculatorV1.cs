namespace GoymenERP.Modules.Manufacturing.Domain.Scheduling;

/// <summary>
/// Pure deterministic scheduling arithmetic. It chooses no priority, speed, shift, due date or
/// duration. It only advances an explicit ProductionOrder through explicit UTC availability.
/// </summary>
public static class ManufacturingScheduleCalculatorV1
{
    public static ManufacturingScheduleResultV1 Calculate(ManufacturingScheduleInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var item = input.QueueItem;
        if (!ValidIdentity(item))
        {
            return ManufacturingScheduleResultV1.Conflict(
                item,
                ManufacturingScheduleCodesV1.InputConflict);
        }

        if (!TryReadyAt(item!, out var readyAt, out var readyConflict))
        {
            return readyConflict
                ? ManufacturingScheduleResultV1.Conflict(item, ManufacturingScheduleCodesV1.InputConflict)
                : ManufacturingScheduleResultV1.Unavailable(item, ManufacturingScheduleCodesV1.InputUnavailable);
        }

        var availability = input.Availability;
        if (availability is null)
        {
            return ManufacturingScheduleResultV1.Unavailable(
                item,
                ManufacturingScheduleCodesV1.InputUnavailable);
        }

        var availabilityProvenance = ClassifyAvailabilityProvenance(availability);
        if (availabilityProvenance == ProvenanceState.Invalid)
        {
            return ManufacturingScheduleResultV1.Conflict(
                item,
                ManufacturingScheduleCodesV1.InputConflict);
        }

        if (availabilityProvenance == ProvenanceState.Missing)
        {
            return ManufacturingScheduleResultV1.Unavailable(
                item,
                ManufacturingScheduleCodesV1.InputUnavailable);
        }

        if (availability.CompanyId != item!.CompanyId
            || availability.WorkCenterId != item.WorkCenterId
            || !Utc(availability.FromUtc)
            || !Utc(availability.ThroughUtc)
            || availability.FromUtc >= availability.ThroughUtc)
        {
            return ManufacturingScheduleResultV1.Conflict(
                item,
                ManufacturingScheduleCodesV1.InputConflict);
        }

        var durations = new[] { input.Setup, input.Changeover, input.Run, input.Cleanup };
        if (durations.Any(duration => duration is null))
        {
            return ManufacturingScheduleResultV1.Unavailable(
                item,
                ManufacturingScheduleCodesV1.InputUnavailable);
        }

        var durationProvenance = durations
            .Select(duration => ClassifyDurationProvenance(duration!))
            .ToArray();
        if (durationProvenance.Contains(ProvenanceState.Invalid))
        {
            return ManufacturingScheduleResultV1.Conflict(
                item,
                ManufacturingScheduleCodesV1.InputConflict);
        }

        if (durationProvenance.Contains(ProvenanceState.Missing))
        {
            return ManufacturingScheduleResultV1.Unavailable(
                item,
                ManufacturingScheduleCodesV1.InputUnavailable);
        }

        long totalMinutes;
        long remainingTicks;
        try
        {
            totalMinutes = durations.Aggregate(
                0L,
                (total, duration) => checked(total + duration!.Minutes!.Value));
            remainingTicks = checked(totalMinutes * TimeSpan.TicksPerMinute);
        }
        catch (OverflowException)
        {
            return ManufacturingScheduleResultV1.Conflict(
                item,
                ManufacturingScheduleCodesV1.InputConflict);
        }

        if (!TryValidateAvailability(availability, out var intervals))
        {
            return ManufacturingScheduleResultV1.Conflict(
                item,
                ManufacturingScheduleCodesV1.InputConflict);
        }

        if (intervals.Count == 0 || readyAt >= availability.ThroughUtc)
        {
            return ManufacturingScheduleResultV1.Unavailable(
                item,
                ManufacturingScheduleCodesV1.CapacityUnavailable);
        }

        DateTimeOffset? plannedStart = null;
        foreach (var interval in intervals)
        {
            if (interval.EndsAtUtc <= readyAt)
            {
                continue;
            }

            var cursor = interval.StartsAtUtc > readyAt ? interval.StartsAtUtc : readyAt;
            plannedStart ??= cursor;
            if (remainingTicks == 0)
            {
                return ManufacturingScheduleResultV1.Ready(
                    item,
                    readyAt,
                    plannedStart.Value,
                    cursor,
                    totalMinutes);
            }

            var availableTicks = (interval.EndsAtUtc - cursor).Ticks;
            if (remainingTicks <= availableTicks)
            {
                return ManufacturingScheduleResultV1.Ready(
                    item,
                    readyAt,
                    plannedStart.Value,
                    cursor.AddTicks(remainingTicks),
                    totalMinutes);
            }

            remainingTicks -= availableTicks;
        }

        return ManufacturingScheduleResultV1.Unavailable(
            item,
            ManufacturingScheduleCodesV1.CapacityUnavailable);
    }

    private static bool ValidIdentity(ManufacturingScheduleQueueItemV1? item) =>
        item is not null
        && item.CompanyId != Guid.Empty
        && item.WorkCenterId != Guid.Empty
        && item.ProductionOrderId != Guid.Empty
        && item.ProductionOrderVersion > 0;

    private static bool TryReadyAt(
        ManufacturingScheduleQueueItemV1 item,
        out DateTimeOffset readyAt,
        out bool conflict)
    {
        var values = new[]
        {
            item.MaterialReadyAtUtc,
            item.ApprovalReadyAtUtc,
            item.PredecessorReadyAtUtc,
            item.WorkCenterReadyAtUtc,
        };
        readyAt = default;
        conflict = false;
        if (values.Any(value => value is null))
        {
            return false;
        }

        if (values.Any(value => !Utc(value!.Value)))
        {
            conflict = true;
            return false;
        }

        readyAt = values.Max(value => value!.Value);
        return true;
    }

    private static ProvenanceState ClassifyAvailabilityProvenance(
        ManufacturingScheduleAvailabilitySnapshotV1 snapshot)
    {
        if (snapshot.CalendarId == Guid.Empty
            || snapshot.CalendarVersion is <= 0
            || snapshot.TimeZoneBindingVersion is <= 0
            || SuppliedBlank(snapshot.CalendarSourceRevision)
            || SuppliedBlank(snapshot.TimeZoneId)
            || SuppliedBlank(snapshot.TimeZoneSourceRevision))
        {
            return ProvenanceState.Invalid;
        }

        return snapshot.CalendarVersion is null
            || snapshot.CalendarSourceRevision is null
            || snapshot.TimeZoneId is null
            || snapshot.TimeZoneBindingVersion is null
            || snapshot.TimeZoneSourceRevision is null
                ? ProvenanceState.Missing
                : ProvenanceState.Complete;
    }

    private static ProvenanceState ClassifyDurationProvenance(
        ManufacturingScheduleDurationSnapshotV1 snapshot)
    {
        if (snapshot.Minutes is < 0
            || snapshot.SourceId == Guid.Empty
            || snapshot.SourceVersion is <= 0
            || SuppliedBlank(snapshot.SourceRevision))
        {
            return ProvenanceState.Invalid;
        }

        return snapshot.Minutes is null
            || snapshot.SourceId is null
            || snapshot.SourceVersion is null
            || snapshot.SourceRevision is null
                ? ProvenanceState.Missing
                : ProvenanceState.Complete;
    }

    private static bool SuppliedBlank(string? value) =>
        value is not null && string.IsNullOrWhiteSpace(value);

    private static bool TryValidateAvailability(
        ManufacturingScheduleAvailabilitySnapshotV1 snapshot,
        out IReadOnlyList<ManufacturingScheduleAvailabilityIntervalV1> intervals)
    {
        intervals = snapshot.Intervals;
        for (var index = 0; index < intervals.Count; index++)
        {
            var interval = intervals[index];
            if (interval is null
                || !Utc(interval.StartsAtUtc)
                || !Utc(interval.EndsAtUtc)
                || interval.StartsAtUtc >= interval.EndsAtUtc
                || interval.StartsAtUtc < snapshot.FromUtc
                || interval.EndsAtUtc > snapshot.ThroughUtc)
            {
                return false;
            }

            if (index > 0 && interval.StartsAtUtc < intervals[index - 1].EndsAtUtc)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Utc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;

    private enum ProvenanceState
    {
        Complete,
        Missing,
        Invalid,
    }
}
