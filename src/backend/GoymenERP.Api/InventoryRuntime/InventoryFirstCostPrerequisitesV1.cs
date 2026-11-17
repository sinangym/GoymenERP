using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.InventoryRuntime;

public sealed class InventoryFirstCostPrerequisitesSourceV1(ISettingsInventoryCostProfileReaderV1 profiles,
    IOrganizationLocationDisplayReaderV1 locations, ICompanyContext company) : IInventoryFirstCostPrerequisitesV1
{
    public async Task<InventoryFirstCostPrerequisitesV1> ReadAsync(Guid companyId, DateOnly effectiveOn,
        IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default)
    {
        void Fence()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId)
                throw new InvalidOperationException("purchase_first_cost_company_changed");
        }
        Fence();
        var profile = await profiles.ReadAsync(companyId, effectiveOn, cancellationToken);
        Fence();
        var codes = new Dictionary<Guid, string>();
        foreach (var batch in locationIds.Distinct().Order().Chunk(100))
        {
            var found = await locations.ReadAsync(companyId, batch, cancellationToken);
            Fence();
            if (found.Count != batch.Length || found.Any(x => !batch.Contains(x.LocationId)
                || string.IsNullOrWhiteSpace(x.LocationCode))) throw new InvalidOperationException("purchase_first_cost_location_unavailable");
            foreach (var item in found)
                if (!codes.TryAdd(item.LocationId, item.LocationCode)) throw new InvalidOperationException("purchase_first_cost_location_unavailable");
        }
        Fence();
        return new(profile, codes);
    }
}
