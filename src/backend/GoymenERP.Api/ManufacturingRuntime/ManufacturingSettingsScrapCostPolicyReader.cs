using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.ManufacturingRuntime;

/// <summary>SAT-D305: the real Settings value and immutable version, with no consumer default or policy choice.</summary>
internal sealed class ManufacturingSettingsScrapCostPolicyReader(ICompanyContext company, ICurrentUser user,
    ISettingsManufacturingScrapCostPolicyProviderV1 owner) : IManufacturingScrapCostPolicyReadPortV1
{
    public async Task<ManufacturingScrapCostPolicySnapshotV1?> ReadAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var actor = user.UserId;
        if (!InScope()) return null;
        try
        {
            var value = await owner.GetScrapCostPolicyAsync(companyId, cancellationToken);
            if (!InScope() || value is null || value.CompanyId != companyId || value.Version < 0) return null;
            return new(companyId, value.IncludeScrapCost, value.VersionText);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }

        bool InScope() => companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId
            && user.IsAuthenticated && !string.IsNullOrWhiteSpace(actor) && user.UserId == actor && user.CanAccessCompany(companyId);
    }
}
