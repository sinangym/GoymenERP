using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed class SettingsManufacturingToleranceProviderV1(
    SettingsDbContext database,
    ICompanyContext companyContext,
    ICurrentUser currentUser) : ISettingsManufacturingToleranceProviderV1
{
    public async Task<ManufacturingToleranceProfileV1> GetManufacturingToleranceAsync(
        string process, DateOnly effectiveOn, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId)
            || !companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new SettingsPrerequisiteException("settings_access_denied");
        }

        try
        {
            if (!await database.ManufacturingTolerancePolicies.AsNoTracking().AnyAsync(cancellationToken))
            {
                throw new SettingsPrerequisiteException("settings_provider_unavailable");
            }

            var versions = await database.ManufacturingTolerancePolicies.AsNoTracking()
                .Where(x => x.Process == process && x.WorkCenterId == null)
                .OrderBy(x => x.EffectiveFrom)
                .ToArrayAsync(cancellationToken);
            if (versions.Length == 0)
            {
                throw new SettingsPrerequisiteException(
                    SettingsPrerequisiteCodesV1.ManufacturingToleranceProfileNotFound);
            }

            var selected = versions.LastOrDefault(x => x.EffectiveFrom <= effectiveOn) ?? versions[0];
            return new ManufacturingToleranceProfileV1(
                selected.CompanyId,
                selected.Process,
                new ProfileVersionV1(selected.ProfileId, selected.Version),
                selected.MaximumFirePercent,
                selected.IsActive,
                selected.EffectiveFrom,
                selected.EffectiveThrough)
            {
                MassBalanceDifferenceTolerancePercent = selected.MassBalanceDifferenceTolerancePercent,
                WorkCenterId = selected.WorkCenterId,
            };
        }
        catch (DbException)
        {
            throw new SettingsPrerequisiteException("settings_provider_unavailable");
        }
    }

    /// <summary>
    /// U1-b (URT-D6, URT-D23): line → process → version-0 catalog, only windows that cover the date (see the port).
    /// </summary>
    public async Task<ManufacturingToleranceProfileV1> GetScopedManufacturingToleranceAsync(
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
        ManufacturingTolerancePolicyRow[] versions;
        bool anyPublished;
        try
        {
            versions = await database.ManufacturingTolerancePolicies.AsNoTracking()
                .Where(x => x.Process == process)
                .ToArrayAsync(cancellationToken);
            anyPublished = versions.Length > 0
                || await database.ManufacturingTolerancePolicies.AsNoTracking().AnyAsync(cancellationToken);
        }
        catch (DbException)
        {
            throw new SettingsPrerequisiteException("settings_provider_unavailable");
        }

        if (!anyPublished && !ApprovedManufacturingProfileCatalog.Serves(companyId))
        {
            throw new SettingsPrerequisiteException("settings_provider_unavailable");
        }

        var selected = (workCenterId is { } line ? Covering(versions, line, effectiveOn) : null)
            ?? Covering(versions, null, effectiveOn);
        if (selected is not null)
        {
            return new ManufacturingToleranceProfileV1(
                selected.CompanyId,
                selected.Process,
                new ProfileVersionV1(selected.ProfileId, selected.Version),
                selected.MaximumFirePercent,
                selected.IsActive,
                selected.EffectiveFrom,
                selected.EffectiveThrough)
            {
                MassBalanceDifferenceTolerancePercent = selected.MassBalanceDifferenceTolerancePercent,
                WorkCenterId = selected.WorkCenterId,
                Source = ManufacturingProfileSourcesV1.Settings,
            };
        }

        // URT-D23: the reviewed version-0 starting value is live when no published version of the scope covers the date.
        var catalog = ApprovedManufacturingProfileCatalog.ToleranceFor(companyId)
            .Where(entry => entry.Process == process && entry.EffectiveFrom <= effectiveOn)
            .OrderByDescending(entry => entry.EffectiveFrom)
            .FirstOrDefault()
            ?? throw new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.ManufacturingToleranceProfileNotFound);
        return new ManufacturingToleranceProfileV1(
            catalog.CompanyId,
            catalog.Process,
            new ProfileVersionV1(catalog.ProfileId, ApprovedManufacturingProfileCatalog.CatalogProfileVersion),
            catalog.MaximumFirePercent,
            true,
            catalog.EffectiveFrom,
            null)
        {
            MassBalanceDifferenceTolerancePercent = catalog.MassBalanceDifferenceTolerancePercent,
            Source = ManufacturingProfileSourcesV1.Catalog,
        };
    }

    /// <summary>The covering published version of one scope: the latest <c>EffectiveFrom</c>, then the highest version.</summary>
    private static ManufacturingTolerancePolicyRow? Covering(
        IEnumerable<ManufacturingTolerancePolicyRow> versions, Guid? workCenterId, DateOnly on) =>
        versions
            .Where(row => row.WorkCenterId == workCenterId && row.EffectiveFrom <= on
                          && (row.EffectiveThrough is not { } through || through >= on))
            .OrderByDescending(row => row.EffectiveFrom)
            .ThenByDescending(row => row.Version)
            .FirstOrDefault();
}
