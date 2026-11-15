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

/// <summary>The plan's termin suggestion, version 1.</summary>
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
    public const int ContractVersion = 1;

    /// <summary>Planned input kg over the profile's hourly capacity, on the work center calendar, starting now; the
    /// line is assumed free (no queue of other work is counted).</summary>
    public const string MethodCode = "calendar_capacity_from_now";

    /// <summary>
    /// The Settings parameter key of the work center's hourly input capacity. Manufacturing mirrors the Settings key
    /// name here as it does for the line speed; the value itself only ever comes from the profile.
    /// </summary>
    public const string CapacityParameterKey = "inputCapacityKgPerHour";

    /// <summary>The unit the capacity value must carry.</summary>
    public const string CapacityUnit = "kg/h";

    public const string Suggested = "suggested";
    public const string Unavailable = "unavailable";

    /// <summary>The plan is cancelled; no termin is suggested for it.</summary>
    public const string PlanCancelled = "plan_cancelled";

    /// <summary>The profile (approved snapshot or today's) carries no capacity value, or there is no profile.</summary>
    public const string CapacityNotConfigured = "capacity_not_configured";

    /// <summary>The capacity value has another unit, is a choice, or is not positive.</summary>
    public const string CapacityInvalid = "capacity_invalid";

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
            required,
            capacity!.Value);
        view = view with { RequiredWorkingMinutes = result.RequiredWorkingMinutes };
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
