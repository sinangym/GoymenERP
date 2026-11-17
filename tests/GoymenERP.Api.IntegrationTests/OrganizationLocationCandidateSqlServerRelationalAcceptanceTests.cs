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
    public async Task Transfer_destinations_keep_the_real_external_operator_and_location_version_without_a_report_grant()
    {
        await WithDatabaseAsync(async factory =>
        {
            var operatorId = Guid.NewGuid(); var externalId = Guid.NewGuid();
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                var database = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
                var site = await database.Sites.FirstAsync();
                var external = new GoymenERP.Modules.Organization.Domain.Locations.Location(externalId, TestCompanyIds.CompanyA,
                    site.Id, "SYN-TRANSFER-EXT", "SENTETIK harici hedef", true);
                external.AssignOperator(operatorId); database.Locations.Add(external); await database.SaveChangesAsync();
            }
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                var owner = scope.ServiceProvider.GetRequiredService<IOrganizationTransferDestinationCandidatesV1>();
                var item = Assert.Single((await owner.SearchAsync(TestCompanyIds.CompanyA, new("SYN-TRANSFER-EXT", 1))).Items);
                Assert.Equal((externalId, true, operatorId), (item.LocationId, item.IsExternal, item.OperatorPartyId));
                Assert.True(item.LocationVersion >= 1);
                await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(() => owner.SearchAsync(TestCompanyIds.CompanyB, new(null, 1)));
            }
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyB);
                Assert.Empty((await scope.ServiceProvider.GetRequiredService<IOrganizationTransferDestinationCandidatesV1>()
                    .SearchAsync(TestCompanyIds.CompanyB, new("SYN-TRANSFER-EXT", 1))).Items);
            }
        });
    }
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
