using System.Globalization;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

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
//
// URT-SC33 follow-up (W5):
// - KIM-SC33-b: the capacity and the safety buffer come from the Settings work center capacity record
//   (ISettingsWorkCenterCapacityProviderV1: the latest stored version, for a draft and an approved plan alike — the
//   record is not captured at approval; reversible, reported). No record is capacity_not_configured; a Settings failure
//   is capacity_source_unavailable. The parameter-profile keys below are no longer read for the suggestion.
// - URT-D96: a "day" buffer counts WORKING days of the work center calendar. The work center's own buffer applies when
//   it has one; otherwise the real Settings company default buffer — the two are never added. A missing record is the
//   decided zero/version-0 answer; a missing or unreadable provider is a retryable 503, never silently zero.
// - URT-D97: the queue order is termin, then priority score, then creation (ProductionWorkCenterQueueOrderV1); a draft
//   is placed where it would stand by the same key. The priority score is zero until it is stored.
// - URT-D99: each plan ahead counts its REMAINING kg (ProductionQueueRemainingKgCalculatorV1).

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
    /// <summary>Version 2 (URT-SC33): the queue and safety margin fields were added. The W5 follow-up keeps the shape;
    /// <c>QueuedKg</c> now carries the remaining kg of the plans ahead (URT-D99).</summary>
    public const int ContractVersion = 2;

    /// <summary>The REMAINING input kg of the plans ordered ahead (termin, priority, creation) and then this plan's input
    /// kg over the work center's hourly INPUT capacity, plus the safety buffer, on the work center calendar, starting now
    /// (URT-D86..D89, URT-D96, URT-D97, URT-D99).</summary>
    public const string MethodCode = "calendar_capacity_remaining_queue_from_now";

    /// <summary>The <see cref="PlanDueDateSuggestionViewV1.CapacitySource"/> of a capacity read from the Settings work
    /// center capacity record (KIM-SC33-b).</summary>
    public const string WorkCenterCapacitySource = "WorkCenterCapacity";

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

    /// <summary>Settings holds no capacity record for the plan's work center.</summary>
    public const string CapacityNotConfigured = "capacity_not_configured";

    /// <summary>The capacity answer is not positive or names another work center or company.</summary>
    public const string CapacityInvalid = "capacity_invalid";

    /// <summary>The safety buffer has an unknown unit, is negative, or (days) is not a whole number of working days.</summary>
    public const string SafetyMarginInvalid = "safety_margin_invalid";

    /// <summary>No work center queue store is composed, or it could not answer (URT-D86 needs the queue).</summary>
    public const string QueueUnavailable = "queue_unavailable";

    /// <summary>No capacity provider is composed, or Settings could not answer.</summary>
    public const string CapacitySourceUnavailable = "capacity_source_unavailable";

    public const string CompanyBufferSourceUnavailable = "company_buffer_source_unavailable";

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
                if (BusinessToday() is not { } businessDate)
                    answer = new(ManufacturingApiStatus.Unavailable, null, false, null);
                else
                {
                    var suggestion = await SuggestDueDateAsync(plan, companyId, now, businessDate, cancellationToken);
                    answer = suggestion.ReasonCode == ManufacturingDueDateSuggestionV1.CompanyBufferSourceUnavailable
                        ? new(ManufacturingApiStatus.Unavailable, null, false, suggestion.ReasonCode)
                        : new(ManufacturingApiStatus.Ok, suggestion, false, null);
                }
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

        var (capacityRecord, capacityRefusal) = await ReadDueDateCapacityAsync(plan, companyId, cancellationToken);
        view = view with
        {
            CapacityKgPerHour = capacityRecord is null ? null : Text(capacityRecord.InputCapacityKgPerHour),
            CapacityProfileVersion = capacityRecord?.Profile.Version.ToString(CultureInfo.InvariantCulture),
            CapacitySource = capacityRecord is null ? null : ManufacturingDueDateSuggestionV1.WorkCenterCapacitySource,
        };
        if (capacityRefusal is not null || capacityRecord is null)
        {
            return view with
            {
                ReasonCode = capacityRefusal ?? ManufacturingDueDateSuggestionV1.CapacityNotConfigured,
            };
        }

        var companyBuffer = (Value: 0m, Unit: (string?)null);
        if (capacityRecord.SafetyBufferUnit is null && capacityRecord.SafetyBufferValue == 0m)
        {
            var recorded = await ReadCompanyDueDateBufferAsync(companyId, cancellationToken);
            if (recorded is null)
                return view with { ReasonCode = ManufacturingDueDateSuggestionV1.CompanyBufferSourceUnavailable };
            companyBuffer = (recorded.WorkingDays, WorkCenterSafetyBufferUnitsV1.Days);
        }
        var (margin, marginRefusal) = SafetyBufferOf(capacityRecord.SafetyBufferValue, capacityRecord.SafetyBufferUnit,
            companyBuffer);
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

        view = view with { QueuedPlanCount = queue.PlanCount, QueuedKg = Text(queue.RemainingKg.Value) };

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
            queue.RemainingKg,
            required,
            capacityRecord.InputCapacityKgPerHour,
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
    /// KIM-SC33-b: the work center's capacity record in force (the latest Settings version). No provider, or a Settings
    /// failure, is <see cref="ManufacturingDueDateSuggestionV1.CapacitySourceUnavailable"/>; no record is
    /// <see cref="ManufacturingDueDateSuggestionV1.CapacityNotConfigured"/>; an answer for another company or work center,
    /// or a non-positive capacity, is <see cref="ManufacturingDueDateSuggestionV1.CapacityInvalid"/>. Cancellation is not
    /// swallowed. Nothing is defaulted.
    /// </summary>
    private async Task<(SettingsWorkCenterCapacityV1? Capacity, string? Refusal)> ReadDueDateCapacityAsync(
        ProductionPlan plan, Guid companyId, CancellationToken cancellationToken)
    {
        if (workCenterCapacities is null)
        {
            return (null, ManufacturingDueDateSuggestionV1.CapacitySourceUnavailable);
        }

        SettingsWorkCenterCapacityV1? capacity;
        try
        {
            capacity = await workCenterCapacities.GetWorkCenterCapacityAsync(companyId, plan.WorkCenterId,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (null, ManufacturingDueDateSuggestionV1.CapacitySourceUnavailable);
        }

        if (capacity is null)
        {
            return (null, ManufacturingDueDateSuggestionV1.CapacityNotConfigured);
        }

        return capacity.CompanyId != companyId || capacity.WorkCenterId != plan.WorkCenterId
               || capacity.InputCapacityKgPerHour <= 0m || capacity.Profile is null
               || capacity.Profile.Version <= 0
            ? (null, ManufacturingDueDateSuggestionV1.CapacityInvalid)
            : (capacity, null);
    }

    /// <summary>
    /// URT-D96/D100: the latest company default, used only when the work center records none. Settings owns the
    /// decided zero/version-0 answer when no record exists. No provider, a failed read or a foreign/malformed answer
    /// is unavailable; it is never interpreted as that zero. The caller revalidates PlansRead after all owner awaits.
    /// </summary>
    private async Task<SettingsManufacturingDueDateBufferV1?> ReadCompanyDueDateBufferAsync(Guid companyId,
        CancellationToken cancellationToken)
    {
        if (companyDueDateBuffer is null) return null;
        try
        {
            var answer = await companyDueDateBuffer.GetDueDateBufferAsync(companyId, cancellationToken);
            return answer.CompanyId == companyId && answer.WorkingDays >= 0 && answer.Version >= 0
                && (answer.Version > 0 || answer.WorkingDays == 0) ? answer : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return null; }
    }

    /// <summary>
    /// URT-D89/URT-D96: the safety buffer from the work center record, or — only when the work center records none — the
    /// company default. Never both. A zero value with no unit is no buffer. <see cref="WorkCenterSafetyBufferUnitsV1.Hours"/>
    /// is working time; <see cref="WorkCenterSafetyBufferUnitsV1.Days"/> is whole WORKING days of the work center calendar
    /// (URT-D96). A negative value, an unknown unit, a unit without a value, or a fractional day count is
    /// <see cref="ManufacturingDueDateSuggestionV1.SafetyMarginInvalid"/> (no rounding is chosen here). Too many hours or
    /// days for the calculator's search horizon is left to the calculator (no working time).
    /// </summary>
    internal static (DueDateSafetyMarginV1? Margin, string? Refusal) SafetyBufferOf(decimal workCenterValue,
        string? workCenterUnit, (decimal Value, string? Unit) companyDefault)
    {
        var workCenterHasBuffer = workCenterUnit is not null || workCenterValue != 0m;
        var (value, unit) = workCenterHasBuffer ? (workCenterValue, workCenterUnit) : companyDefault;
        if (unit is null)
        {
            return value == 0m
                ? (DueDateSafetyMarginV1.None, null)
                : (null, ManufacturingDueDateSuggestionV1.SafetyMarginInvalid);
        }

        if (value < 0m)
        {
            return (null, ManufacturingDueDateSuggestionV1.SafetyMarginInvalid);
        }

        if (string.Equals(unit, WorkCenterSafetyBufferUnitsV1.Hours, StringComparison.Ordinal))
        {
            var maximumHours = (decimal)TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerHour;
            return value > maximumHours
                ? (null, ManufacturingDueDateSuggestionV1.SafetyMarginInvalid)
                : (new DueDateSafetyMarginV1(TimeSpan.FromTicks((long)decimal.Ceiling(value * TimeSpan.TicksPerHour)), 0),
                    null);
        }

        if (string.Equals(unit, WorkCenterSafetyBufferUnitsV1.Days, StringComparison.Ordinal))
        {
            return value != decimal.Truncate(value) || value > int.MaxValue
                ? (null, ManufacturingDueDateSuggestionV1.SafetyMarginInvalid)
                : (new DueDateSafetyMarginV1(TimeSpan.Zero, (int)value), null);
        }

        return (null, ManufacturingDueDateSuggestionV1.SafetyMarginInvalid);
    }

    /// <summary>
    /// URT-D86/URT-D97/URT-D99: the work ordered ahead of the plan on its work center — the queued plans whose
    /// (termin, priority score, creation) key comes before the plan's own, and their remaining kg. A draft is placed by
    /// the same key (where it would stand once approved). No store, or a store failure other than a concurrent change
    /// (which stays the retryable 503), is null — the suggestion is then unavailable, never "free line".
    /// </summary>
    private async Task<ProductionQueueAheadV1?> ReadDueDateQueueAsync(ProductionPlan plan,
        CancellationToken cancellationToken)
    {
        if (workCenterQueues is null)
        {
            return null;
        }

        try
        {
            var queue = await workCenterQueues.ReadWorkCenterQueueAsync(plan.WorkCenterId, plan.Id,
                cancellationToken);
            return queue.Target.PlanId == plan.Id
                ? ProductionWorkCenterQueueOrderV1.AheadRealized(queue.Queued, queue.Target, queue.TargetIsQueued, queue.StoredOrder)
                : null;
        }
        catch (ManufacturingRuleException)
        {
            return null;
        }
        catch (ManufacturingStoreException exception) when (exception.Code != ManufacturingStoreCodes.ConcurrentChange)
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
