using System.Globalization;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Modules.Manufacturing.Application;

// URT-S10 (F-010b, B-15; SINAN-CEVAP 14 NS-10 "Takvim + öneri, onaylı"): the plan's termin SUGGESTION, read only. The
// work center's shift calendar comes from Organization (IOrganizationWorkCenterPlanningReferenceV2: weekly shifts,
// holidays, time zone, calendar revision); the hourly capacity comes from the plan's Settings parameter profile
// (key ManufacturingDueDateSuggestionV1.CapacityParameterKey, unit kg/h): the approved plan's captured snapshot, or the
// profile in force today for a draft — the same rule as every other plan calculation. No value is a number in code.
// The one calculator is ProductionDueDateSuggestionCalculatorV1. Nothing is stored and no termin is set: the user takes
// (or changes) the suggested day through the existing termin command (F-010a), which keeps every check of URT-D66.
// A missing capacity value, calendar or working time is a 200 answer "unavailable" with its reason, never a guessed day.
// OPEN-007 stays open.
//
// URT-SC33 (SINAN-CEVAP 33): the queued planned work on the work center is counted first (URT-D86; the queue rule is
// IManufacturingWorkCenterQueueStore's, a reversible implementation detail), the capacity is INPUT kg per hour and the
// plan's recipe input kg is divided by it (URT-D87), an overnight shift belongs to its start day and counts whole on a
// working start day (URT-D88), and the work center's optional safety margin (hours and/or days, same profile as the
// capacity) is added; no capacity is "unavailable", no margin is zero (URT-D89). The margin keys mirror the Settings
// keys the KIM-SC33-b slice adds; until they exist no profile carries them and the margin is zero.

/// <summary>The plan's termin suggestion (contract version <see cref="ManufacturingDueDateSuggestionV1.ContractVersion"/>).
/// <c>RequiredKg</c>/<c>RequiredWorkingMinutes</c> are this plan's own input kg and working time; the queue fields are the
/// work queued ahead of it; <c>TotalWorkingMinutes</c> is queue + plan + margin hours.</summary>
public sealed record PlanDueDateSuggestionViewV1(
    int ContractVersion,
    string MethodCode,
    Guid PlanId,
    long PlanVersion,
    long DueDateRevision,
    Guid WorkCenterId,
    string Status,
    DateOnly? SuggestedDueDate,
    string? CompletesAtLocal,
    string RequiredKg,
    long? RequiredWorkingMinutes,
    int? QueuedPlanCount,
    string? QueuedKg,
    long? QueueWorkingMinutes,
    string? SafetyMarginHours,
    int? SafetyMarginDays,
    long? TotalWorkingMinutes,
    string? CapacityKgPerHour,
    string? CapacityProfileVersion,
    string? CapacitySource,
    long? CalendarVersion,
    string? CalendarSource,
    string? TimeZoneId,
    string? ReasonCode,
    DateOnly BusinessDate,
    DateTimeOffset CalculatedAtUtc);

/// <summary>Wire names and codes of the termin suggestion (URT-S10).</summary>
public static class ManufacturingDueDateSuggestionV1
{
    /// <summary>Version 2 (URT-SC33): the queue and safety margin fields were added.</summary>
    public const int ContractVersion = 2;

    /// <summary>The queued plans' input kg and then this plan's input kg over the profile's hourly INPUT capacity, plus
    /// the safety margin, on the work center calendar, starting now (URT-D86..D89).</summary>
    public const string MethodCode = "calendar_capacity_queue_from_now";

    /// <summary>
    /// The Settings parameter key of the work center's hourly input capacity. Manufacturing mirrors the Settings key
    /// name here as it does for the line speed; the value itself only ever comes from the profile.
    /// </summary>
    public const string CapacityParameterKey = "inputCapacityKgPerHour";

    /// <summary>The unit the capacity value must carry.</summary>
    public const string CapacityUnit = "kg/h";

    /// <summary>URT-D89: the optional safety margin in working hours added after the work (mirrors the Settings key).</summary>
    public const string SafetyMarginHoursParameterKey = "dueDateSafetyMarginHours";

    /// <summary>The unit the margin hours value must carry.</summary>
    public const string SafetyMarginHoursUnit = "h";

    /// <summary>URT-D89: the optional safety margin in whole working days added after the completion day (mirrors the
    /// Settings key).</summary>
    public const string SafetyMarginDaysParameterKey = "dueDateSafetyMarginDays";

    /// <summary>The unit the margin days value must carry.</summary>
    public const string SafetyMarginDaysUnit = "d";

    public const string Suggested = "suggested";
    public const string Unavailable = "unavailable";

    /// <summary>The plan is cancelled; no termin is suggested for it.</summary>
    public const string PlanCancelled = "plan_cancelled";

    /// <summary>The profile (approved snapshot or today's) carries no capacity value, or there is no profile.</summary>
    public const string CapacityNotConfigured = "capacity_not_configured";

    /// <summary>The capacity value has another unit, is a choice, or is not positive.</summary>
    public const string CapacityInvalid = "capacity_invalid";

    /// <summary>A safety margin value has another unit, is a choice, is negative, or (days) is not a whole number.</summary>
    public const string SafetyMarginInvalid = "safety_margin_invalid";

    /// <summary>No work center queue store is composed, or it could not answer (URT-D86 needs the queue).</summary>
    public const string QueueUnavailable = "queue_unavailable";

    /// <summary>Settings could not be read for a draft's profile.</summary>
    public const string CapacitySourceUnavailable = "capacity_source_unavailable";

    /// <summary>No calendar port is composed, or Organization failed to answer.</summary>
    public const string CalendarUnavailable = "calendar_unavailable";

    /// <summary>Organization answered for another work center, or with a time zone the host cannot resolve.</summary>
    public const string CalendarAnswerInvalid = "calendar_answer_invalid";

    /// <summary>Organization does not know the plan's work center in this company.</summary>
    public const string WorkCenterNotFound = "work_center_not_found";

    /// <summary>The local wall-clock form of <see cref="PlanDueDateSuggestionViewV1.CompletesAtLocal"/>.</summary>
    public const string LocalMomentFormat = "yyyy-MM-dd'T'HH:mm";
}

public sealed partial class ManufacturingCommandService
{
    /// <summary>
    /// URT-S10: the plan's termin suggestion under the plan read grant (<see cref="ErpPermissions.ManufacturingPlansRead"/>),
    /// measured again after the reads. The server-resolved company only: another company's plan is not found. A read
    /// meeting concurrent commits, or a host that cannot resolve the business day, is a retryable 503. Cancellation is not
    /// swallowed. The suggested day is never earlier than today's Europe/Istanbul business day (URT-D66).
    /// </summary>
    public async Task<ManufacturingApiResult<PlanDueDateSuggestionViewV1>> GetPlanDueDateSuggestionAsync(Guid planId,
        CancellationToken cancellationToken)
    {
        if (!HasContext() || ActorGuid() is not { } actorId)
        {
            return Denied<PlanDueDateSuggestionViewV1>();
        }

        if (planId == Guid.Empty)
        {
            return NotFoundResult<PlanDueDateSuggestionViewV1>();
        }

        var companyId = companyContext.CompanyId;
        ManufacturingApiResult<PlanDueDateSuggestionViewV1> answer;
        try
        {
            var plan = await plans.FindPlanAsync(planId, cancellationToken);
            if (plan is null || plan.CompanyId != companyId)
            {
                answer = NotFoundResult<PlanDueDateSuggestionViewV1>();
            }
            else
            {
                var now = timeProvider.GetUtcNow();
                answer = BusinessToday() is { } businessDate
                    ? new ManufacturingApiResult<PlanDueDateSuggestionViewV1>(ManufacturingApiStatus.Ok,
                        await SuggestDueDateAsync(plan, companyId, now, businessDate, cancellationToken), false, null)
                    : new ManufacturingApiResult<PlanDueDateSuggestionViewV1>(ManufacturingApiStatus.Unavailable,
                        null, false, null);
            }
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            answer = new ManufacturingApiResult<PlanDueDateSuggestionViewV1>(ManufacturingApiStatus.Unavailable, null,
                false, null);
        }

        // A grant withdrawn while the suggestion was read answers nothing of it.
        var scope = await authority.RevalidatePermissionScopeAsync(
            ErpPermissions.ManufacturingPlansRead, companyId, cancellationToken);
        return scope.IsGranted && HasCapturedContext(companyId, actorId)
            ? answer
            : Denied<PlanDueDateSuggestionViewV1>();
    }

    private async Task<PlanDueDateSuggestionViewV1> SuggestDueDateAsync(ProductionPlan plan, Guid companyId,
        DateTimeOffset now, DateOnly businessDate, CancellationToken cancellationToken)
    {
        var required = plan.Inputs.Aggregate(Kilograms.Zero, (sum, input) => sum + input.PlannedMass);
        var view = new PlanDueDateSuggestionViewV1(
            ManufacturingDueDateSuggestionV1.ContractVersion,
            ManufacturingDueDateSuggestionV1.MethodCode,
            plan.Id,
            plan.Version,
            plan.DueDateRevision,
            plan.WorkCenterId,
            ManufacturingDueDateSuggestionV1.Unavailable,
            null,
            null,
            Text(required.Value),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            businessDate,
            now);

        if (plan.Status == PlanStatus.Cancelled)
        {
            return view with { ReasonCode = ManufacturingDueDateSuggestionV1.PlanCancelled };
        }

        var (capacity, capacityProfile, capacityRefusal) = await ReadDueDateCapacityAsync(plan, cancellationToken);
        view = view with
        {
            CapacityKgPerHour = capacity is { } value ? Text(value) : null,
            CapacityProfileVersion = capacityProfile?.ProfileVersion,
            CapacitySource = capacityProfile?.Source,
        };
        if (capacityRefusal is not null)
        {
            return view with { ReasonCode = capacityRefusal };
        }

        var (margin, marginRefusal) = SafetyMarginOf(capacityProfile);
        if (marginRefusal is not null || margin is null)
        {
            return view with { ReasonCode = marginRefusal ?? ManufacturingDueDateSuggestionV1.SafetyMarginInvalid };
        }

        view = view with
        {
            SafetyMarginHours = Text((decimal)margin.WorkingTime.Ticks / TimeSpan.TicksPerHour),
            SafetyMarginDays = margin.WorkingDays,
        };

        var queue = await ReadDueDateQueueAsync(plan, cancellationToken);
        if (queue is null)
        {
            return view with { ReasonCode = ManufacturingDueDateSuggestionV1.QueueUnavailable };
        }

        view = view with { QueuedPlanCount = queue.PlanCount, QueuedKg = Text(queue.PlannedInputKg.Value) };

        var (calendar, zone, calendarRefusal) = await ReadDueDateCalendarAsync(companyId, plan.WorkCenterId,
            cancellationToken);
        if (calendar is null || zone is null)
        {
            return view with { ReasonCode = calendarRefusal ?? ManufacturingDueDateSuggestionV1.CalendarUnavailable };
        }

        view = view with
        {
            CalendarVersion = calendar.Version,
            CalendarSource = calendar.Source,
            TimeZoneId = calendar.TimeZoneId,
        };

        var result = ProductionDueDateSuggestionCalculatorV1.Calculate(
            now,
            zone,
            calendar.Shifts.Select(shift => new DueDateCalendarShiftV1(shift.Day, shift.Start, shift.End)).ToArray(),
            calendar.Holidays,
            queue.PlannedInputKg,
            required,
            capacity!.Value,
            margin);
        view = view with
        {
            RequiredWorkingMinutes = result.RequiredWorkingMinutes,
            QueueWorkingMinutes = result.QueueWorkingMinutes,
            TotalWorkingMinutes = result.TotalWorkingMinutes,
        };
        if (result.Status != DueDateSuggestionStatusV1.Suggested || result.SuggestedDueDate is not { } suggested)
        {
            return view with { ReasonCode = result.ReasonCode ?? DueDateSuggestionCodesV1.NoWorkingTime };
        }

        return view with
        {
            Status = ManufacturingDueDateSuggestionV1.Suggested,
            // URT-D66 (1): never earlier than today's Istanbul business day, whatever the calendar's own zone says.
            SuggestedDueDate = suggested < businessDate ? businessDate : suggested,
            CompletesAtLocal = result.CompletesAtLocal?.ToString(ManufacturingDueDateSuggestionV1.LocalMomentFormat,
                CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// The plan's hourly capacity: the approved plan's captured profile, or for a draft the profile in force today. A
    /// missing value is <see cref="ManufacturingDueDateSuggestionV1.CapacityNotConfigured"/>; another unit, a choice or a
    /// non-positive number is <see cref="ManufacturingDueDateSuggestionV1.CapacityInvalid"/>; a Settings failure on a
    /// draft is <see cref="ManufacturingDueDateSuggestionV1.CapacitySourceUnavailable"/>. Nothing is defaulted.
    /// </summary>
    private async Task<(decimal? Capacity, PlanParameterSnapshotV1? Profile, string? Refusal)>
        ReadDueDateCapacityAsync(ProductionPlan plan, CancellationToken cancellationToken)
    {
        PlanParameterSnapshotV1? profile;
        if (plan.ApprovedAtUtc is not null)
        {
            profile = plan.ParameterSnapshot;
        }
        else
        {
            // The strict read (as for an approval): Settings being down is "source unavailable", not "not configured";
            // a profile that does not exist stays "not configured". Nothing is approved or stored here.
            var read = await ReadParameterProfileAsync(plan.Process, plan.WorkCenterId, approval: true,
                cancellationToken);
            if (read.Refusal is not null)
            {
                return (null, null, ManufacturingDueDateSuggestionV1.CapacitySourceUnavailable);
            }

            profile = read.Snapshot;
        }

        var (capacity, refusal) = CapacityOf(profile);
        return (capacity, profile, refusal);
    }

    internal static (decimal? Capacity, string? Refusal) CapacityOf(PlanParameterSnapshotV1? profile)
    {
        var value = profile?.Values.FirstOrDefault(candidate => string.Equals(candidate.Key,
            ManufacturingDueDateSuggestionV1.CapacityParameterKey, StringComparison.Ordinal));
        if (value is null)
        {
            return (null, ManufacturingDueDateSuggestionV1.CapacityNotConfigured);
        }

        return value.Number is { } number && number > 0m
               && string.Equals(value.Unit, ManufacturingDueDateSuggestionV1.CapacityUnit, StringComparison.Ordinal)
            ? (number, null)
            : (null, ManufacturingDueDateSuggestionV1.CapacityInvalid);
    }

    /// <summary>
    /// URT-D89: the safety margin from the same profile as the capacity. A missing value is zero (the decision: no margin
    /// is 0); another unit, a choice, a negative number, or a fractional day count is
    /// <see cref="ManufacturingDueDateSuggestionV1.SafetyMarginInvalid"/>. Too many hours or days for the calculator's
    /// search horizon is left to the calculator (no working time).
    /// </summary>
    internal static (DueDateSafetyMarginV1? Margin, string? Refusal) SafetyMarginOf(PlanParameterSnapshotV1? profile)
    {
        var hours = profile?.Values.FirstOrDefault(candidate => string.Equals(candidate.Key,
            ManufacturingDueDateSuggestionV1.SafetyMarginHoursParameterKey, StringComparison.Ordinal));
        var days = profile?.Values.FirstOrDefault(candidate => string.Equals(candidate.Key,
            ManufacturingDueDateSuggestionV1.SafetyMarginDaysParameterKey, StringComparison.Ordinal));
        var maximumHours = (decimal)TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerHour;

        var workingTime = TimeSpan.Zero;
        if (hours is not null)
        {
            if (hours.Number is not { } number || number < 0m || number > maximumHours
                || !string.Equals(hours.Unit, ManufacturingDueDateSuggestionV1.SafetyMarginHoursUnit,
                    StringComparison.Ordinal))
            {
                return (null, ManufacturingDueDateSuggestionV1.SafetyMarginInvalid);
            }

            workingTime = TimeSpan.FromTicks((long)decimal.Ceiling(number * TimeSpan.TicksPerHour));
        }

        var workingDays = 0;
        if (days is not null)
        {
            if (days.Number is not { } number || number < 0m || number != decimal.Truncate(number)
                || number > int.MaxValue
                || !string.Equals(days.Unit, ManufacturingDueDateSuggestionV1.SafetyMarginDaysUnit,
                    StringComparison.Ordinal))
            {
                return (null, ManufacturingDueDateSuggestionV1.SafetyMarginInvalid);
            }

            workingDays = (int)number;
        }

        return (new DueDateSafetyMarginV1(workingTime, workingDays), null);
    }

    /// <summary>
    /// URT-D86: the work queued on the plan's work center ahead of it. An approved plan counts only the plans approved
    /// before it (approvedAtUtc, id); a draft counts every queued plan. No store, or a store failure other than a
    /// concurrent change (which stays the retryable 503), is null — the suggestion is then unavailable, never "free line".
    /// </summary>
    private async Task<ManufacturingWorkCenterQueue?> ReadDueDateQueueAsync(ProductionPlan plan,
        CancellationToken cancellationToken)
    {
        if (workCenterQueues is null)
        {
            return null;
        }

        var key = plan.Status == PlanStatus.Approved && plan.ApprovedAtUtc is { } approvedAt
            ? new ManufacturingWorkCenterQueueKey(approvedAt, plan.Id)
            : null;
        try
        {
            return await workCenterQueues.ReadWorkCenterQueueAsync(plan.WorkCenterId, plan.Id, key,
                cancellationToken);
        }
        catch (ManufacturingRuleException)
        {
            return null;
        }
    }

    /// <summary>
    /// The work center calendar from Organization. No port, an Organization failure (including a company-context
    /// mismatch) → <see cref="ManufacturingDueDateSuggestionV1.CalendarUnavailable"/>; unknown in this company →
    /// <see cref="ManufacturingDueDateSuggestionV1.WorkCenterNotFound"/>; an answer for another work center, with missing
    /// lists or a zone the host cannot resolve → <see cref="ManufacturingDueDateSuggestionV1.CalendarAnswerInvalid"/>.
    /// </summary>
    private async Task<(OrganizationWorkCenterPlanningReferenceV2? Calendar, TimeZoneInfo? Zone, string? Refusal)>
        ReadDueDateCalendarAsync(Guid companyId, Guid workCenterId, CancellationToken cancellationToken)
    {
        if (workCenterCalendars is null)
        {
            return (null, null, ManufacturingDueDateSuggestionV1.CalendarUnavailable);
        }

        OrganizationWorkCenterPlanningReferenceV2? calendar;
        try
        {
            calendar = await workCenterCalendars.ReadAsync(companyId, workCenterId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (null, null, ManufacturingDueDateSuggestionV1.CalendarUnavailable);
        }

        if (calendar is null)
        {
            return (null, null, ManufacturingDueDateSuggestionV1.WorkCenterNotFound);
        }

        if (calendar.WorkCenterId != workCenterId || calendar.Shifts is null || calendar.Holidays is null
            || string.IsNullOrWhiteSpace(calendar.TimeZoneId)
            || !TimeZoneInfo.TryFindSystemTimeZoneById(calendar.TimeZoneId, out var zone))
        {
            return (null, null, ManufacturingDueDateSuggestionV1.CalendarAnswerInvalid);
        }

        return (calendar, zone, null);
    }
}
