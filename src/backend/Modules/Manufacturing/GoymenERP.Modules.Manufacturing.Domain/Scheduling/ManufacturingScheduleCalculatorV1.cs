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
        if (availability is null || !HasAvailabilityProvenance(availability))
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
        if (durations.Any(duration => duration is null || !HasDurationProvenance(duration)))
        {
            return ManufacturingScheduleResultV1.Unavailable(
                item,
                ManufacturingScheduleCodesV1.InputUnavailable);
        }

        if (durations.Any(duration => duration!.Minutes < 0))
        {
            return ManufacturingScheduleResultV1.Conflict(
                item,
                ManufacturingScheduleCodesV1.InputConflict);
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

        if (!TryNormalizeAvailability(availability, out var intervals))
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

    private static bool HasAvailabilityProvenance(ManufacturingScheduleAvailabilitySnapshotV1 snapshot) =>
        snapshot.CalendarId != Guid.Empty
        && snapshot.CalendarVersion > 0
        && Canonical(snapshot.CalendarSourceRevision)
        && Canonical(snapshot.TimeZoneId)
        && snapshot.TimeZoneBindingVersion > 0
        && Canonical(snapshot.TimeZoneSourceRevision);

    private static bool HasDurationProvenance(ManufacturingScheduleDurationSnapshotV1 snapshot) =>
        snapshot.Minutes is not null
        && snapshot.SourceId is not null && snapshot.SourceId != Guid.Empty
        && snapshot.SourceVersion > 0
        && Canonical(snapshot.SourceRevision);

    private static bool TryNormalizeAvailability(
        ManufacturingScheduleAvailabilitySnapshotV1 snapshot,
        out IReadOnlyList<ManufacturingScheduleAvailabilityIntervalV1> normalized)
    {
        normalized = [];
        if (snapshot.Intervals.Any(interval =>
                interval is null
                || !Utc(interval.StartsAtUtc)
                || !Utc(interval.EndsAtUtc)
                || interval.StartsAtUtc >= interval.EndsAtUtc
                || interval.StartsAtUtc < snapshot.FromUtc
                || interval.EndsAtUtc > snapshot.ThroughUtc))
        {
            return false;
        }

        var merged = new List<ManufacturingScheduleAvailabilityIntervalV1>();
        foreach (var interval in snapshot.Intervals.OrderBy(interval => interval.StartsAtUtc))
        {
            if (merged.Count == 0 || interval.StartsAtUtc > merged[^1].EndsAtUtc)
            {
                merged.Add(interval);
                continue;
            }

            var prior = merged[^1];
            if (interval.EndsAtUtc > prior.EndsAtUtc)
            {
                merged[^1] = prior with { EndsAtUtc = interval.EndsAtUtc };
            }
        }

        normalized = merged.AsReadOnly();
        return true;
    }

    private static bool Canonical(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static bool Utc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;
}
