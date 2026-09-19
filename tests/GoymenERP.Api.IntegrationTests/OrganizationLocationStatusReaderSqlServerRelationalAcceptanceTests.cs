using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The Organization module read (<see cref="IOrganizationLocationStatusReaderV1"/>) against a REAL SQL
/// Server and the real host's registration. It runs in a scope with no signed-in user: the read is
/// company-scoped by the resolved context only, with no Organization permission or row scope. Both test
/// companies carry the same location codes under different ids, so a cross-company read is a real one.
/// </summary>
public sealed class OrganizationLocationStatusReaderSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_module_reads_its_companys_location_state_without_a_user_and_never_another_companys()
    {
        await WithDatabaseAsync(async factory =>
        {
            var hallOfA = await LocationIdAsync(factory, TestCompanyIds.CompanyA, "OSM-H0");
            var depotOfA = await LocationIdAsync(factory, TestCompanyIds.CompanyA, "EXT-BASTUG");
            var hallOfB = await LocationIdAsync(factory, TestCompanyIds.CompanyB, "OSM-H0");
            Assert.NotEqual(hallOfA, hallOfB);

            Assert.Equal(new OrganizationLocationStatusV1(hallOfA, true, false),
                await ReadAsync(factory, TestCompanyIds.CompanyA, TestCompanyIds.CompanyA, hallOfA));
            // Supplier depots are external (SINAN_KARARLARI_2026-08-31 §3).
            Assert.Equal(new OrganizationLocationStatusV1(depotOfA, true, true),
                await ReadAsync(factory, TestCompanyIds.CompanyA, TestCompanyIds.CompanyA, depotOfA));

            // Another company's location is not found, from either side; nothing of it is returned.
            Assert.Null(await ReadAsync(factory, TestCompanyIds.CompanyB, TestCompanyIds.CompanyB, hallOfA));
            Assert.Null(await ReadAsync(factory, TestCompanyIds.CompanyA, TestCompanyIds.CompanyA, hallOfB));
            Assert.Null(await ReadAsync(factory, TestCompanyIds.CompanyA, TestCompanyIds.CompanyA, Guid.NewGuid()));

            // A company argument that is not the resolved context is refused, not answered.
            await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(
                () => ReadAsync(factory, TestCompanyIds.CompanyB, TestCompanyIds.CompanyA, hallOfA));

            // The location's own flag: deactivated in A, it reads inactive; B's location of the same code is untouched.
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
                (await db.Locations.SingleAsync(location => location.Id == hallOfA)).SetActive(false);
                await db.SaveChangesAsync();
            }

            Assert.Equal(new OrganizationLocationStatusV1(hallOfA, false, false),
                await ReadAsync(factory, TestCompanyIds.CompanyA, TestCompanyIds.CompanyA, hallOfA));
            Assert.Equal(new OrganizationLocationStatusV1(hallOfB, true, false),
                await ReadAsync(factory, TestCompanyIds.CompanyB, TestCompanyIds.CompanyB, hallOfB));
        });
    }

    private static async Task<OrganizationLocationStatusV1?> ReadAsync(
        SqlServerRelationalApiFactory factory, Guid resolvedCompany, Guid askedCompany, Guid locationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(resolvedCompany);
        return await scope.ServiceProvider.GetRequiredService<IOrganizationLocationStatusReaderV1>()
            .ReadAsync(askedCompany, locationId);
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
        var databaseName = $"GoymenERP_Organization_LocationStatus_{Guid.NewGuid():N}";
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
