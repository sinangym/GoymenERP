using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Manufacturing.Application;

// Üretim U1-b (URT-D9, URT-D10, DEC-PRDCT-008): Manufacturing reads the Settings parameter profile of the plan's line
// (the work center's own published version wins over the process version; the reviewed version-0 catalog is the base)
// through ISettingsManufacturingParameterProviderV1. The values feed the one plan calculator by the engines' own names;
// a value the profile does not carry stays unknown and is named. The approval captures the profile as an immutable
// snapshot that every later calculation of the plan uses.

public sealed partial class ManufacturingCommandService
{
    private const string SettingsAccessDenied = "settings_access_denied";
    private const string SettingsProviderUnavailable = "settings_provider_unavailable";
    private const string SettingsProfileInvalid = "settings_profile_invalid";

    /// <summary>
    /// The profile in force today for the process and line, as a plan snapshot. Outcomes:
    /// <list type="bullet">
    /// <item>no port composed, or Settings has no profile of the process
    /// (<see cref="ManufacturingParameterProfileCodesV1.ProfileNotFound"/> — e.g. Tube / Profile, which has no catalog
    /// profile) → no snapshot: every profile value stays unknown, nothing is defaulted;</item>
    /// <item><c>settings_provider_unavailable</c> → no snapshot for a draft or a preview (values unknown), but a refusal
    /// (503) for an <paramref name="approval"/>, so no plan is approved without the profile while Settings is down;</item>
    /// <item><c>settings_access_denied</c> → 403; any other Settings code or an answer for another company, process or
    /// line, or values the snapshot refuses → 503 <c>settings_profile_invalid</c>.</item>
    /// </list>
    /// </summary>
    private async Task<(PlanParameterSnapshotV1? Snapshot, ManufacturingApiResult<object>? Refusal)>
        ReadParameterProfileAsync(ManufacturingProcess process, Guid workCenterId, bool approval,
            CancellationToken cancellationToken)
    {
        if (manufacturingParameters is null)
        {
            return (null, null);
        }

        var selectedFor = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        ManufacturingParameterProfileSnapshotV1 profile;
        try
        {
            profile = await manufacturingParameters.GetManufacturingParametersAsync(
                process.ToString(), workCenterId, selectedFor, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SettingsPrerequisiteException exception)
        {
            return exception.Code switch
            {
                ManufacturingParameterProfileCodesV1.ProfileNotFound => (null, null),
                SettingsProviderUnavailable when !approval => (null, null),
                SettingsProviderUnavailable => (null, new ManufacturingApiResult<object>(
                    ManufacturingApiStatus.Unavailable, null, false, SettingsProviderUnavailable)),
                SettingsAccessDenied => (null, Denied<object>()),
                _ => (null, new ManufacturingApiResult<object>(
                    ManufacturingApiStatus.Unavailable, null, false, SettingsProfileInvalid)),
            };
        }

        var snapshot = ToSnapshot(profile, process, workCenterId, selectedFor);
        return snapshot is null
            ? (null, new ManufacturingApiResult<object>(ManufacturingApiStatus.Unavailable, null, false,
                SettingsProfileInvalid))
            : (snapshot, null);
    }

    /// <summary>A draft's read: the profile in force today, or none on any Settings answer (a read never fails on it).</summary>
    private async Task<PlanParameterSnapshotV1?> ReadLiveParameterProfileAsync(ProductionPlan plan,
        CancellationToken cancellationToken)
    {
        var read = await ReadParameterProfileAsync(plan.Process, plan.WorkCenterId, approval: false, cancellationToken);
        return read.Refusal is null ? read.Snapshot : null;
    }

    /// <summary>
    /// Copies Settings' answer into the immutable plan snapshot after checking that it is this company's, this process's
    /// and (when scoped) this line's, and that it covers the date. Null when any of that fails.
    /// </summary>
    private PlanParameterSnapshotV1? ToSnapshot(ManufacturingParameterProfileSnapshotV1 profile,
        ManufacturingProcess process, Guid workCenterId, DateOnly selectedFor)
    {
        if (profile is null || profile.CompanyId != companyContext.CompanyId
            || !string.Equals(profile.Process, process.ToString(), StringComparison.Ordinal)
            || profile.WorkCenterId is { } line && line != workCenterId
            || profile.Values is null)
        {
            return null;
        }

        try
        {
            return new PlanParameterSnapshotV1(
                profile.ProfileId,
                profile.ProfileVersion,
                profile.Source,
                process,
                profile.WorkCenterId,
                profile.EffectiveFrom,
                profile.EffectiveThrough,
                selectedFor,
                profile.Values.Select(value => new PlanParameterValueV1(value.Key, value.Unit, value.Number, value.Choice)));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
