using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>The state of one item's duration in an inbox answer.</summary>
internal static class WorkInboxDurationStatesV1
{
    /// <summary>The class length is recorded in Settings; the due time is computed.</summary>
    public const string Defined = "Defined";

    /// <summary>Settings holds no length for the class (YOL-D18 fail closed: "süre tanımlı değil").</summary>
    public const string Undefined = "Undefined";

    /// <summary>Settings could not be read now; nothing is assumed.</summary>
    public const string Unavailable = "Unavailable";
}

/// <summary>
/// The duration of one inbox item (YOL-D17/YOL-D18): its class, the class length in force and its Settings version,
/// the due time and whether it has passed. Only <see cref="WorkInboxDurationStatesV1.Defined"/> carries numbers.
/// </summary>
internal sealed record WorkInboxDurationV1(
    string Class,
    string State,
    int? Limit,
    DateTimeOffset? DueAtUtc,
    bool? IsOverdue,
    string? PolicyVersion);

/// <summary>
/// Computes an item's due time from the owner's "waiting since" time and the Settings class length. The hour class adds
/// whole hours. The business-day class counts Monday–Friday in Turkey local time (UTC+3, no daylight saving since 2016):
/// a wait that starts on a weekend counts from the next Monday 00:00; the due time keeps the local clock time.
/// ASSUMPTION-W1A-1: no public-holiday calendar exists in the system yet, so holidays count as business days until one
/// is added; this is a display rule only (no automatic approval, rejection or escalation is driven by it in W1-A).
/// </summary>
internal static class WorkInboxDurationCalculatorV1
{
    private static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);

    public static WorkInboxDurationV1 Compute(
        WorkApprovalDurationClassV1 durationClass,
        SettingsWorkApprovalDurationPolicyV1? policy,
        DateTimeOffset waitingSinceUtc,
        DateTimeOffset nowUtc)
    {
        var className = durationClass.ToString();
        if (policy is null)
        {
            return new(className, WorkInboxDurationStatesV1.Unavailable, null, null, null, null);
        }

        var (limit, version) = durationClass switch
        {
            WorkApprovalDurationClassV1.Hours => (policy.Hours, policy.HoursVersionText),
            WorkApprovalDurationClassV1.BusinessDays => (policy.BusinessDays, policy.BusinessDaysVersionText),
            _ => (null, null),
        };
        if (limit is not > 0 || version is null)
        {
            return new(className, WorkInboxDurationStatesV1.Undefined, null, null, null, null);
        }

        var due = durationClass == WorkApprovalDurationClassV1.Hours
            ? waitingSinceUtc.AddHours(limit.Value)
            : AddBusinessDays(waitingSinceUtc, limit.Value);
        return new(className, WorkInboxDurationStatesV1.Defined, limit, due, nowUtc > due, version);
    }

    internal static DateTimeOffset AddBusinessDays(DateTimeOffset startUtc, int days)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        var local = startUtc.ToOffset(TurkeyOffset);
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            var toMonday = local.DayOfWeek == DayOfWeek.Saturday ? 2 : 1;
            local = new DateTimeOffset(local.Date.AddDays(toMonday), TurkeyOffset);
        }

        var remaining = days;
        while (remaining > 0)
        {
            local = local.AddDays(1);
            if (local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                remaining--;
            }
        }
        return local.ToUniversalTime();
    }
}
