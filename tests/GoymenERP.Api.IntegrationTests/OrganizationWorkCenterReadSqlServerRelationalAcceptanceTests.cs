using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Contracts;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The ORG-03 read journey against real SQL Server: the authorized work-center list,
/// the step from a work-center row to the location it names, and the return.
///
/// It adds NO endpoint. <c>GET /api/v1/organization/work-centers</c> and
/// <c>GetWorkCentersAsync</c> are merged and unchanged; what was never measured is that
/// this read is company- and scope-bound, that its <c>locationId</c> is not a key past
/// the caller's own location authority, and that the whole journey writes nothing.
/// </summary>
public sealed class OrganizationWorkCenterReadSqlServerRelationalAcceptanceTests
{
    private const string WorkCentersRoute = "/api/v1/organization/work-centers";
    private const string LocationsRoute = "/api/v1/organization/locations";

    [SqlServerFact]
    public async Task AuthorizedListAndLocationStep_ReturnScopedRowsAndChangeNothing()
    {
        await WithDatabaseAsync(async factory =>
        {
            using var client = CreateClient(factory, TestUsers.CompanyA, TestCompanyIds.CompanyA);
            var baseline = await ReadEffectsAsync(factory, TestCompanyIds.CompanyA);

            var workCenters = await ReadWorkCentersAsync(client);
            Assert.Equal(baseline.WorkCenters, workCenters.Length);
            // Every declared field is present and usable, not merely non-null.
            Assert.All(workCenters, item =>
            {
                Assert.NotEqual(Guid.Empty, item.Id);
                Assert.NotEqual(Guid.Empty, item.LocationId);
                Assert.False(string.IsNullOrWhiteSpace(item.Code));
                Assert.False(string.IsNullOrWhiteSpace(item.Name));
                Assert.False(string.IsNullOrWhiteSpace(item.Kind));
            });
            Assert.Equal(workCenters.Length, workCenters.Select(item => item.Id).Distinct().Count());
            // Server order is Code, and the client paginates nothing here — the endpoint
            // is unpaged, so the array IS the authorized set.
            Assert.Equal(workCenters.OrderBy(item => item.Code, StringComparer.Ordinal), workCenters);
            Assert.Contains(workCenters, item => item.Code == "WC-PAINT" && item.Kind == "Paint");

            // The step the slice exists for: each row's locationId opens the location the
            // caller is already entitled to, and the two reads agree on the same record.
            var locations = await ReadPageAsync(client);
            foreach (var workCenter in workCenters)
            {
                var listed = Assert.Single(locations.Items, item => item.Id == workCenter.LocationId);
                Assert.Equal(listed, await ReadLocationAsync(client, workCenter.LocationId));
            }

            // ...and back: the list is unchanged by having been left.
            Assert.Equal(workCenters, await ReadWorkCentersAsync(client));
            Assert.Equal(baseline, await ReadEffectsAsync(factory, TestCompanyIds.CompanyA));
        });
    }

    [SqlServerFact]
    public async Task NarrowScopeForeignCompanyAndUnauthenticated_ExposeNoForeignWorkCenter()
    {
        await WithDatabaseAsync(async factory =>
        {
            using var client = CreateClient(factory, TestUsers.CompanyA, TestCompanyIds.CompanyA);
            var all = await ReadWorkCentersAsync(client);
            Assert.NotEmpty(all);
            var baseline = await ReadEffectsAsync(factory, TestCompanyIds.CompanyA);

            // A site-scoped reader gets its own site's work centers, from the SAME rows —
            // a narrower authority must not produce a different or invented record.
            using var viewer = CreateClient(factory, TestUsers.CompanyAViewer, TestCompanyIds.CompanyA);
            var scoped = await ReadWorkCentersAsync(viewer);
            Assert.NotEmpty(scoped);
            Assert.All(scoped, item => Assert.Contains(all, row => row == item));
            var scopedLocations = await ReadPageAsync(viewer);
            Assert.All(scoped, item =>
                Assert.Contains(scopedLocations.Items, location => location.Id == item.LocationId));

            // Company B carries no work centers of its own and must not borrow A's.
            using var foreign = CreateClient(factory, TestUsers.CompanyB, TestCompanyIds.CompanyB);
            Assert.Empty(await ReadWorkCentersAsync(foreign));

            // A company header the caller may not use is refused, not silently narrowed.
            using var wrongCompany = CreateClient(factory, TestUsers.CompanyA, TestCompanyIds.CompanyB);
            using (var response = await wrongCompany.GetAsync(WorkCentersRoute))
            {
                await AssertPlatformErrorAsync(response, HttpStatusCode.Forbidden, "forbidden");
                Assert.DoesNotContain(all[0].Id.ToString(), await response.Content.ReadAsStringAsync());
            }

            // No company context at all is refused, never answered with an unscoped list.
            using var contextless = factory.CreateClient();
            contextless.DefaultRequestHeaders.Add("X-Development-User", TestUsers.CompanyA);
            using (var response = await contextless.GetAsync(WorkCentersRoute))
            {
                Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
                Assert.DoesNotContain(all[0].Id.ToString(), await response.Content.ReadAsStringAsync());
            }

            using var anonymous = factory.CreateClient();
            using (var response = await anonymous.GetAsync(WorkCentersRoute))
            {
                await AssertPlatformErrorAsync(
                    response, HttpStatusCode.Unauthorized, "authentication_required");
            }

            // Every refusal above is a read refusal: nothing was written by any of them.
            Assert.Equal(baseline, await ReadEffectsAsync(factory, TestCompanyIds.CompanyA));
        });
    }

    private static HttpClient CreateClient(
        SqlServerRelationalApiFactory factory,
        string user,
        Guid company)
    {
        var client = factory.CreateClientFor(user, company);
        // The shared factory's Cari handshake is not an Organization contract.
        client.DefaultRequestHeaders.Remove(CounterpartyClientContract.HeaderName);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        return client;
    }

    private static async Task<WorkCenterSummary[]> ReadWorkCentersAsync(HttpClient client)
    {
        using var response = await client.GetAsync(WorkCentersRoute);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        // `kind` reaches the wire as the enum NAME; a numeric value would silently become
        // a label the client cannot show, so the shape is asserted, not assumed.
        Assert.All(body.RootElement.EnumerateArray(), item =>
            Assert.Equal(JsonValueKind.String, item.GetProperty("kind").ValueKind));
        return Assert.IsType<WorkCenterSummary[]>(body.RootElement.Deserialize<WorkCenterSummary[]>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static async Task<PageResult<LocationSummary>> ReadPageAsync(HttpClient client) =>
        Assert.IsType<PageResult<LocationSummary>>(
            await client.GetFromJsonAsync<PageResult<LocationSummary>>(
                LocationsRoute + "?page=1&pageSize=100"));

    private static async Task<LocationSummary> ReadLocationAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"{LocationsRoute}/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<LocationSummary>(
            await response.Content.ReadFromJsonAsync<LocationSummary>());
    }

    private static async Task AssertPlatformErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    private static async Task<Effects> ReadEffectsAsync(
        SqlServerRelationalApiFactory factory,
        Guid company)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
        var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        Assert.True(db.Database.IsSqlServer());
        return new Effects(
            await db.WorkCenters.CountAsync(),
            await db.Locations.CountAsync(),
            await db.Sites.CountAsync(),
            await db.Set<AuditEvent>().CountAsync(),
            await db.CommandExecutions.CountAsync());
    }

    private static async Task WithDatabaseAsync(Func<SqlServerRelationalApiFactory, Task> test)
    {
        var connection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"GoymenERP_Organization_WorkCenters_{Guid.NewGuid():N}";
        await using var factory = new SqlServerRelationalApiFactory(
            connection,
            applyTestCompanyBTopologyTemplates: false,
            databaseName: databaseName);
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

    private sealed record Effects(
        int WorkCenters,
        int Locations,
        int Sites,
        int Audits,
        int Executions);
}
