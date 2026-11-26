using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Modules.Work.Infrastructure.ApprovalFollowUp;

/// <summary>
/// One active member of the company who holds a permission now: company-wide, or scoped to the listed sites, locations and
/// work centers (D11). Identity measures it; Work never reads Identity's tables.
/// </summary>
public sealed record WorkPermissionHolderV1(
    Guid UserId,
    bool IsCompanyWide,
    IReadOnlySet<Guid> SiteIds,
    IReadOnlySet<Guid> LocationIds,
    IReadOnlySet<Guid> WorkCenterIds,
    bool IsGeneralManager = false,
    WorkPermissionHolderScopeV1? ModuleManagerScope = null);

public sealed record WorkPermissionHolderScopeV1(bool IsCompanyWide, IReadOnlySet<Guid> SiteIds,
    IReadOnlySet<Guid> LocationIds, IReadOnlySet<Guid> WorkCenterIds);

/// <summary><see cref="Available"/> false: the owner could not answer (nothing is assumed, nobody is told, no step is taken).</summary>
public sealed record WorkPermissionHoldersV1(bool Available, IReadOnlyList<WorkPermissionHolderV1> Holders)
{
    public static WorkPermissionHoldersV1 Unavailable { get; } = new(false, []);
}

/// <summary>
/// WP2: the holders of one registered permission in the server-resolved company now, for the follow-up cycle (system work,
/// no person asks). Implemented by the host over Identity's system port. Bounded: an owner that holds more holders than it
/// returns answers unavailable rather than a partial list.
/// </summary>
public interface IWorkPermissionHolderDirectoryV1
{
    Task<WorkPermissionHoldersV1> ListHoldersAsync(Guid companyId, string permission, CancellationToken cancellationToken = default);
}

/// <summary>
/// The approval timing the follow-up cycle needs, as Settings holds it for the resolved company: the two class lengths with
/// their version texts (YOL-D18; version 0 is the decided default) and the recorded holiday days of the years asked for
/// (YOL-D22; a year without a list is missing from <see cref="HolidayYears"/> and deducts weekends only).
/// </summary>
public sealed record WorkApprovalTimingV1(
    Guid CompanyId,
    int? Hours,
    string? HoursVersion,
    int? BusinessDays,
    string? BusinessDaysVersion,
    IReadOnlyDictionary<int, IReadOnlySet<DateOnly>> HolidayYears);

/// <summary>Settings' automation read for the follow-up cycle (host-implemented); null when Settings could not answer.</summary>
public interface IWorkApprovalTimingReaderV1
{
    Task<WorkApprovalTimingV1?> ReadAsync(Guid companyId, int fromYear, int toYear, CancellationToken cancellationToken = default);
}

/// <summary>
/// The due time of one approval cycle (YOL-D17, YOL-D18, YOL-D22, D6): the hour class adds whole hours; the business-day
/// class counts Monday–Friday in the business time zone and skips the recorded holidays and eves. A wait that starts on a
/// non-business day counts from the next business day 00:00; the due time keeps the local clock time. The same arithmetic
/// as the inbox shows (the inbox delegates here).
/// </summary>
public static class WorkApprovalDueCalculatorV1
{
    public static DateTimeOffset AddHours(DateTimeOffset startUtc, int hours)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hours);
        return startUtc.ToUniversalTime().AddHours(hours);
    }

    public static DateTimeOffset AddBusinessDays(
        DateTimeOffset startUtc,
        int days,
        TimeZoneInfo zone,
        IReadOnlyDictionary<int, IReadOnlySet<DateOnly>> holidays,
        out int firstYear,
        out int lastYear)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(holidays);
        var local = TimeZoneInfo.ConvertTime(startUtc, zone);
        var date = DateOnly.FromDateTime(local.DateTime);
        var clock = TimeOnly.FromDateTime(local.DateTime);
        firstYear = date.Year;
        if (!IsBusinessDay(date, holidays))
        {
            do
            {
                date = date.AddDays(1);
            }
            while (!IsBusinessDay(date, holidays));
            clock = TimeOnly.MinValue;
        }

        var remaining = days;
        while (remaining > 0)
        {
            date = date.AddDays(1);
            if (IsBusinessDay(date, holidays))
            {
                remaining--;
            }
        }

        lastYear = date.Year;
        var dueLocal = date.ToDateTime(clock, DateTimeKind.Unspecified);
        // A local time skipped by a clock change moves forward to the first valid minute (the zone's own rule).
        while (zone.IsInvalidTime(dueLocal))
        {
            dueLocal = dueLocal.AddMinutes(1);
        }

        return new DateTimeOffset(dueLocal, zone.GetUtcOffset(dueLocal)).ToUniversalTime();
    }

    /// <summary>
    /// The class length and its version for one cycle, and its due time; null when the length is not defined, the zone rules
    /// are missing for a business-day class, or Settings could not answer (fail closed: no reminder, no escalation).
    /// </summary>
    public static (DateTimeOffset Due, int Length, string Version)? Due(
        WorkApprovalDurationClassV1 durationClass,
        WorkApprovalTimingV1? timing,
        DateTimeOffset startUtc,
        TimeZoneInfo? zone)
    {
        if (timing is null)
        {
            return null;
        }

        switch (durationClass)
        {
            case WorkApprovalDurationClassV1.Hours when timing.Hours is { } hours && hours > 0
                && timing.HoursVersion is { Length: > 0 } version:
                return (AddHours(startUtc, hours), hours, version);
            case WorkApprovalDurationClassV1.BusinessDays when timing.BusinessDays is { } days && days > 0
                && timing.BusinessDaysVersion is { Length: > 0 } dayVersion && zone is not null:
                return (AddBusinessDays(startUtc, days, zone, timing.HolidayYears, out _, out _), days, dayVersion);
            default:
                return null;
        }
    }

    /// <summary>
    /// YOL-D33 (E-58; YOL-D17/YOL-D28): after the reminder, the escalation falls due one more duration class later (4 hours in
    /// the hour class, 2 business days in the business-day class by default) — the same Settings value, no new number.
    /// </summary>
    public static DateTimeOffset? EscalationDue(
        WorkApprovalDurationClassV1 durationClass,
        int length,
        DateTimeOffset dueUtc,
        TimeZoneInfo? zone,
        IReadOnlyDictionary<int, IReadOnlySet<DateOnly>> holidays) => durationClass switch
        {
            WorkApprovalDurationClassV1.Hours => AddHours(dueUtc, length),
            WorkApprovalDurationClassV1.BusinessDays when zone is not null => AddBusinessDays(dueUtc, length, zone, holidays, out _, out _),
            _ => null,
        };

    private static bool IsBusinessDay(DateOnly date, IReadOnlyDictionary<int, IReadOnlySet<DateOnly>> holidays) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
        && !(holidays.TryGetValue(date.Year, out var days) && days.Contains(date));
}
