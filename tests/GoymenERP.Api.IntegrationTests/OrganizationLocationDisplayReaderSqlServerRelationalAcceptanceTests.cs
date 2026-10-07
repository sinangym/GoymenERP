using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The Organization-owned location display batch read against a REAL SQL Server and the real host
/// registration. The consuming module remains responsible for authorizing its response; this reader
/// only returns immutable display identity from the resolved company.
/// </summary>
public sealed class OrganizationLocationDisplayReaderSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_module_batch_reads_real_display_identity_for_its_company_only()
    {
        await WithDatabaseAsync(async factory =>
        {
            var hallOfA = await LocationAsync(factory, TestCompanyIds.CompanyA, "OSM-H0");
            var depotOfA = await LocationAsync(factory, TestCompanyIds.CompanyA, "EXT-BASTUG");
            var hallOfB = await LocationAsync(factory, TestCompanyIds.CompanyB, "OSM-H0");
            Assert.NotEqual(hallOfA.Id, hallOfB.Id);

            // Display identity remains available after deactivation; historical Inventory rows must not
            // lose their real Organization-owned name. The seeded supplier depot exercises the same rule
            // for an external location.
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
                (await db.Locations.SingleAsync(location => location.Id == hallOfA.Id)).SetActive(false);
                await db.SaveChangesAsync();
            }

            var result = await ReadAsync(
                factory,
                TestCompanyIds.CompanyA,
                TestCompanyIds.CompanyA,
                [depotOfA.Id, hallOfB.Id, hallOfA.Id, Guid.NewGuid()]);

            var expected = new[]
            {
                new OrganizationLocationDisplayV1(hallOfA.Id, hallOfA.Code, hallOfA.Name),
                new OrganizationLocationDisplayV1(depotOfA.Id, depotOfA.Code, depotOfA.Name)
            }.OrderBy(location => location.LocationId).ToArray();

            Assert.Equal(expected, result);
            Assert.DoesNotContain(result, location => location.LocationId == hallOfB.Id);

            // A caller cannot choose a company different from the live resolved context.
            await Assert.ThrowsAsync<OrganizationCompanyContextMismatchException>(() => ReadAsync(
                factory,
                TestCompanyIds.CompanyB,
                TestCompanyIds.CompanyA,
                [hallOfA.Id]));
        });
    }

    private static async Task<IReadOnlyList<OrganizationLocationDisplayV1>> ReadAsync(
        SqlServerRelationalApiFactory factory,
        Guid resolvedCompany,
        Guid askedCompany,
        IReadOnlyCollection<Guid> locationIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(resolvedCompany);
        return await scope.ServiceProvider.GetRequiredService<IOrganizationLocationDisplayReaderV1>()
            .ReadAsync(askedCompany, locationIds);
    }

    private static async Task<LocationIdentity> LocationAsync(
        SqlServerRelationalApiFactory factory,
        Guid companyId,
        string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(companyId);
        return await scope.ServiceProvider.GetRequiredService<OrganizationDbContext>().Locations.AsNoTracking()
            .Where(location => location.Code == code)
            .Select(location => new LocationIdentity(location.Id, location.Code, location.Name))
            .SingleAsync();
    }

    private static async Task WithDatabaseAsync(Func<SqlServerRelationalApiFactory, Task> test)
    {
        var connection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"GoymenERP_Organization_LocationDisplay_{Guid.NewGuid():N}";
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

    private sealed record LocationIdentity(Guid Id, string Code, string Name);
}
