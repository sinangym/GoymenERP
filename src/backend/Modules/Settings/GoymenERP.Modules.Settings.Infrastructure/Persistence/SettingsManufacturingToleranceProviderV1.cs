using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
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
                .Where(x => x.Process == process)
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
                selected.EffectiveThrough);
        }
        catch (DbException)
        {
            throw new SettingsPrerequisiteException("settings_provider_unavailable");
        }
    }
}
