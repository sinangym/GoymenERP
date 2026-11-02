using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// MOD-01: the Manufacturing parameter profile port over the company's published Settings versions and the reviewed version-0
/// catalog (<see cref="ApprovedManufacturingProfileCatalog"/>), in the order <see cref="ISettingsManufacturingParameterProviderV1"/>
/// states. Drafts and approved-but-unpublished versions are never served. Reads go through the company query filter.
/// </summary>
public sealed class SettingsManufacturingParameterProviderV1(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser) : ISettingsManufacturingParameterProviderV1
{
    public async Task<ManufacturingParameterProfileSnapshotV1> GetManufacturingParametersAsync(
        string process, Guid? workCenterId, DateOnly effectiveOn, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new SettingsPrerequisiteException("settings_access_denied");
        }

        if (!SettingsManufacturingProcessesV1.IsCanonical(process) || workCenterId == Guid.Empty)
        {
            throw new SettingsPrerequisiteException(ManufacturingParameterProfileCodesV1.RequestInvalid);
        }

        var companyId = companyContext.CompanyId;
        IReadOnlyList<ManufacturingParameterProfileCandidate> published;
        bool anyPublished;
        try
        {
            published = await PublishedAsync(process, cancellationToken);
            anyPublished = published.Count > 0 || await database.ManufacturingParameterProfiles.AsNoTracking()
                .AnyAsync(row => row.Status == ManufacturingProfileStatusesV1.Published, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException("settings_provider_unavailable");
        }

        if (!anyPublished && !ApprovedManufacturingProfileCatalog.Serves(companyId))
        {
            throw new SettingsPrerequisiteException("settings_provider_unavailable");
        }

        var candidates = ApprovedManufacturingProfileCatalog.ParametersFor(companyId)
            .Where(entry => entry.Process == process)
            .Select(ManufacturingParameterProfileCandidate.FromCatalog)
            .Concat(published)
            .ToArray();
        var selected = (workCenterId is { } line ? Covering(candidates, line, effectiveOn) : null)
            ?? Covering(candidates, null, effectiveOn)
            ?? throw new SettingsPrerequisiteException(ManufacturingParameterProfileCodesV1.ProfileNotFound);
        if (selected.Values.Count == 0 || selected.Values.Any(value => !ManufacturingProfileValueRulesV1.IsWellFormed(process, value)))
        {
            throw new SettingsPrerequisiteException("settings_profile_invalid");
        }

        return new ManufacturingParameterProfileSnapshotV1(
            companyId,
            process,
            selected.WorkCenterId,
            selected.ProfileId,
            selected.ProfileVersion.ToString(CultureInfo.InvariantCulture),
            selected.Source,
            selected.EffectiveFrom,
            selected.EffectiveThrough,
            selected.Values
                .Select(value => new ManufacturingParameterValueV1(value.Key, value.Unit, value.Number, value.Choice))
                .ToArray());
    }

    /// <summary>The company's published versions of the process with their values (company query filter).</summary>
    private async Task<IReadOnlyList<ManufacturingParameterProfileCandidate>> PublishedAsync(string process,
        CancellationToken cancellationToken)
    {
        var headers = await database.ManufacturingParameterProfiles.AsNoTracking()
            .Where(row => row.Process == process && row.Status == ManufacturingProfileStatusesV1.Published)
            .ToListAsync(cancellationToken);
        if (headers.Count == 0)
        {
            return [];
        }

        var ids = headers.Select(row => row.ProfileId).ToArray();
        var values = await database.ManufacturingParameterProfileValues.AsNoTracking()
            .Where(row => ids.Contains(row.ProfileId))
            .ToListAsync(cancellationToken);
        var byProfile = values.ToLookup(row => row.ProfileId);
        return headers.Select(row => new ManufacturingParameterProfileCandidate(row.ProfileId,
                ManufacturingProfileSourcesV1.Settings, row.Process, row.WorkCenterId, row.ProfileVersion, row.EffectiveFrom,
                row.EffectiveThrough, ManufacturingParameterProfileReads.Values(byProfile[row.ProfileId])))
            .ToArray();
    }

    /// <summary>The covering version of one scope: the latest <c>EffectiveFrom</c>, then the highest version.</summary>
    internal static ManufacturingParameterProfileCandidate? Covering(
        IEnumerable<ManufacturingParameterProfileCandidate> candidates, Guid? workCenterId, DateOnly on) =>
        candidates
            .Where(candidate => candidate.WorkCenterId == workCenterId && candidate.EffectiveFrom <= on
                                && (candidate.EffectiveThrough is not { } through || through >= on))
            .OrderByDescending(candidate => candidate.EffectiveFrom)
            .ThenByDescending(candidate => candidate.ProfileVersion)
            .FirstOrDefault();
}

/// <summary>A servable (published or catalog) parameter profile version.</summary>
internal sealed record ManufacturingParameterProfileCandidate(
    Guid ProfileId,
    string Source,
    string Process,
    Guid? WorkCenterId,
    long ProfileVersion,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveThrough,
    IReadOnlyList<ManufacturingParameterValueDataV1> Values)
{
    public static ManufacturingParameterProfileCandidate FromCatalog(ApprovedManufacturingParameterProfileEntry entry) =>
        new(entry.ProfileId, ManufacturingProfileSourcesV1.Catalog, entry.Process, null,
            ApprovedManufacturingProfileCatalog.CatalogProfileVersion, entry.EffectiveFrom, null, entry.Values);
}

internal static class ManufacturingParameterProfileReads
{
    public static IReadOnlyList<ManufacturingParameterValueDataV1> Values(IEnumerable<ManufacturingParameterProfileValueRow> rows) =>
        rows.OrderBy(row => row.ParameterKey, StringComparer.Ordinal)
            .Select(row => new ManufacturingParameterValueDataV1(row.ParameterKey, row.Unit, row.NumericValue, row.ChoiceValue))
            .ToArray();
}
