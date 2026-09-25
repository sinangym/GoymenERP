using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Organization's module-owned candidate source through the real host registration and SQL Server.
/// It is a read-only company boundary; no signed-in user or Organization permission is required.
/// </summary>
public sealed class OrganizationLocationCandidateSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Active_internal_locations_are_bounded_joined_to_their_site_and_company_isolated()
    {
        await WithDatabaseAsync(async factory =>
        {
            var first = await SearchAsync(factory, TestCompanyIds.CompanyA, TestCompanyIds.CompanyA,
                new("OSM", 1));
            Assert.Single(first.Items);
            Assert.True(first.SearchWindowFull);
            Assert.Equal("OSM-H0", first.Items[0].LocationCode);
            Assert.False(string.IsNullOrWhiteSpace(first.Items[0].SiteCode));
            Assert.False(string.IsNullOrWhiteSpace(first.Items[0].SiteName));

            // Supplier depots exist but are external and therefore never candidates.
            Assert.Empty((await SearchAsync(factory, TestCompanyIds.CompanyA, TestCompanyIds.CompanyA,
                new("EXT-", 100))).Items);

            var companyB = await SearchAsync(factory, TestCompanyIds.CompanyB, TestCompanyIds.CompanyB,
                new("OSM-H0", 10));
            Assert.Equal("OSM-H0", Assert.Single(companyB.Items).LocationCode);
            Assert.NotEqual(first.Items[0].LocationId, companyB.Items[0].LocationId);

            await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(
                () => SearchAsync(factory, TestCompanyIds.CompanyB, TestCompanyIds.CompanyA, new(null, 1)));

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
                (await db.Locations.SingleAsync(location => location.Code == "OSM-H0")).SetActive(false);
                await db.SaveChangesAsync();
            }

            Assert.Empty((await SearchAsync(factory, TestCompanyIds.CompanyA, TestCompanyIds.CompanyA,
                new("OSM-H0", 10))).Items);
        });
    }

    private static async Task<OrganizationLocationCandidatesV1> SearchAsync(
        SqlServerRelationalApiFactory factory,
        Guid resolvedCompany,
        Guid askedCompany,
        OrganizationLocationCandidateQueryV1 query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(resolvedCompany);
        return await scope.ServiceProvider.GetRequiredService<IOrganizationLocationCandidatesV1>()
            .SearchAsync(askedCompany, query);
    }

    private static async Task WithDatabaseAsync(Func<SqlServerRelationalApiFactory, Task> test)
    {
        var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"GoymenERP_Organization_LocationCandidates_{Guid.NewGuid():N}";
        await using var factory = new SqlServerRelationalApiFactory(connection, databaseName: databaseName);
        await using var cleanupScope = factory.Services.CreateAsyncScope();
        var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
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
