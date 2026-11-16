using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Future_allocation_user_read_keeps_other_company_rows_out_and_rechecks_permission_after_sql()
    {
        await using var run = await Run.StartAsync();
        var supply = FutureSupplyIds.New();
        var ownId = Guid.NewGuid();
        foreach (var companyId in new[] { CompanyA, CompanyB })
        {
            await run.ExecuteAsync(InsertFutureSupplyLine, supply.Line(companyId, 20m, 1m));
            await run.ExecuteAsync(InsertFutureSupplyAllocation, supply.Allocation(companyId, companyId == CompanyA ? ownId : Guid.NewGuid(), 1m, 1m, 1));
        }
        await using var database = run.Context(CompanyA);
        var authority = new FutureReadAuthority();
        var query = new InventoryFutureAllocationQueryService(database, Company(CompanyA), new FutureReadUser(), authority, new FutureReadUnits());
        var result = await query.ListByDemandAsync(supply.DemandOrder, supply.DemandLine);
        Assert.Equal((CompanyA, ownId), (result.CompanyId, Assert.Single(result.Items).AllocationId));
        Assert.Equal(2, authority.Calls);
        authority.DenyAfterFirst = true;
        authority.Calls = 0;
        var denied = await Assert.ThrowsAsync<InventoryAllocationException>(() => query.ListByDemandAsync(supply.DemandOrder, supply.DemandLine));
        Assert.Equal(403, denied.Status);
    }

    private sealed class FutureReadUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "SENTETIK-future-read";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { CompanyA };
        public bool CanAccessCompany(Guid companyId) => companyId == CompanyA;
    }
    private sealed class FutureReadAuthority : IErpAuthorityRevalidationService
    {
        public bool DenyAfterFirst { get; set; }
        public int Calls { get; set; }
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new PermissionScope(companyId == CompanyA && permission == ErpPermissions.InventoryFutureAllocationRead
                && (!DenyAfterFirst || Calls == 1), true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }
    private sealed class FutureReadUnits : ISettingsUnitIdentityCatalogV1
    {
        public Task<UnitIdentityV1?> ReadAsync(Guid unitId, CancellationToken cancellationToken = default) =>
            Task.FromResult<UnitIdentityV1?>(new(unitId, "SENTETIK-birim", "synthetic", false));
        public Task<IReadOnlyList<UnitIdentityV1>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Only stored units may be requested.");
    }
}
