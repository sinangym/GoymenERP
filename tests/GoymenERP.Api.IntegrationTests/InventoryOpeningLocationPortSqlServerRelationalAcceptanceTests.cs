using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// INV-OPEN-2, ORG #345 P3-1: the opening command's location port AS THE HOST COMPOSES IT — the host-registered
/// <see cref="IInventoryLocationStatusPort"/> over the real Organization reader (#339) and its tenant query filter —
/// against a real SQL Server. The command tests use a port double and the adapter unit test a fake reader; this is
/// the composition of the two, end to end. Both test companies carry the same seeded location codes under
/// different ids, so the cross-company read is a real one.
/// </summary>
public sealed class InventoryOpeningLocationPortSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_host_port_reads_GDCs_own_hall_active_the_supplier_depot_external_and_company_Bs_location_not_found()
    {
        await WithDatabaseAsync(async factory =>
        {
            var hallOfA = await LocationIdAsync(factory, TestCompanyIds.CompanyA, "OSM-H0");
            var depotOfA = await LocationIdAsync(factory, TestCompanyIds.CompanyA, "EXT-BASTUG");
            var hallOfB = await LocationIdAsync(factory, TestCompanyIds.CompanyB, "OSM-H0");
            Assert.NotEqual(hallOfA, hallOfB);

            Assert.Equal(InventoryLocationStatus.Active, await ReadInAAsync(factory, hallOfA));
            // Supplier depots are external (SINAN_KARARLARI_2026-08-31 §3).
            Assert.Equal(InventoryLocationStatus.External, await ReadInAAsync(factory, depotOfA));
            // Company B's location, asked in company A: the tenant filter hides it, so it is not found.
            Assert.Equal(InventoryLocationStatus.NotFound, await ReadInAAsync(factory, hallOfB));
        });
    }

    private static async Task<InventoryLocationStatus> ReadInAAsync(SqlServerRelationalApiFactory factory, Guid locationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
        var port = scope.ServiceProvider.GetRequiredService<IInventoryLocationStatusPort>();
        Assert.IsType<OrganizationInventoryLocationStatusPort>(port);
        return await port.ReadAsync(TestCompanyIds.CompanyA, locationId, CancellationToken.None);
    }

    private static async Task<Guid> LocationIdAsync(SqlServerRelationalApiFactory factory, Guid companyId, string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
        return await scope.ServiceProvider.GetRequiredService<OrganizationDbContext>().Locations.AsNoTracking()
            .Where(location => location.Code == code).Select(location => location.Id).SingleAsync();
    }

    private static async Task WithDatabaseAsync(Func<SqlServerRelationalApiFactory, Task> test)
    {
        var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"GoymenERP_Inventory_OpeningLocationPort_{Guid.NewGuid():N}";
        await using var factory = new SqlServerRelationalApiFactory(connection, databaseName: databaseName);
        await using var cleanupScope = factory.Services.CreateAsyncScope();
        var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        // Validate the immutable cleanup target before running assertions, not in their finally path.
        Assert.Equal(databaseName, cleanupDb.Database.GetDbConnection().Database);
        try
        {
            await test(factory);
        }
        finally
        {
            await cleanupDb.Database.EnsureDeletedAsync();
        }
    }
}
