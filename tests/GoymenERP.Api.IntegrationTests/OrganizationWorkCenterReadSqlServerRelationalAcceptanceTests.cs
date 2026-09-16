using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Contracts;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
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
            //
            // This subset check alone cannot prove the filter runs: every seeded work
            // center sits under this viewer's own SITE-OSM, so `scoped` equals `all` here
            // by topology. The excluding case is
            // `LocationScopedReader_SeesExactlyThatLocationsWorkCenters` below.
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

    /// <summary>
    /// The scope filter with a scope that genuinely EXCLUDES records.
    ///
    /// The seeded `company-a-viewer` is scoped to SITE-OSM and every seeded work center
    /// sits under that site, so its list equals the company-wide one and no assertion
    /// built on it can fail when the filter is removed. This case grants a fresh role
    /// carrying ONLY <c>organization.work-centers.read</c>, scoped to the OSM-H3
    /// LOCATION, to an actor that holds no Organization permission of its own
    /// (`site-audit-reader` is seeded with SECURITY_APPROVER alone, so the grant under
    /// test is the only one that can answer this read).
    ///
    /// The expected set comes from the seed rather than from the endpoint:
    /// <c>Seed/organization-seed.json</c> places exactly WC-SLIT and WC-CR on OSM-H3
    /// (Hol 3 — Dilme / Soğuk Hadde), WC-PAINT on OSM-H0, WC-TUBE on OSM-H1 and WC-CUT on
    /// OSM-H6. So the authorized answer is those two, in server (Code) order, and the
    /// other three must not appear in the body at all.
    /// </summary>
    [SqlServerFact]
    public async Task LocationScopedReader_SeesExactlyThatLocationsWorkCenters()
    {
        await WithDatabaseAsync(async factory =>
        {
            using var companyWide = CreateClient(factory, TestUsers.CompanyA, TestCompanyIds.CompanyA);
            var all = await ReadWorkCentersAsync(companyWide);
            var baseline = await ReadEffectsAsync(factory, TestCompanyIds.CompanyA);
            var locationId = await ResolveLocationIdAsync(factory, "OSM-H3");
            await GrantWorkCenterReadOnLocationAsync(factory, TestUsers.SiteAuditReader, locationId);

            using var scoped = CreateClient(factory, TestUsers.SiteAuditReader, TestCompanyIds.CompanyA);
            var rows = await ReadWorkCentersAsync(scoped);

            Assert.Equal(new[] { "WC-CR", "WC-SLIT" }, rows.Select(item => item.Code).ToArray());
            Assert.All(rows, item => Assert.Equal(locationId, item.LocationId));
            Assert.All(rows, item => Assert.Contains(all, row => row == item));

            // The excluded rows are absent as VALUES, not merely dropped by a shape.
            using var response = await scoped.GetAsync(WorkCentersRoute);
            var body = await response.Content.ReadAsStringAsync();
            foreach (var hidden in all.Where(item => item.LocationId != locationId))
            {
                Assert.DoesNotContain(hidden.Id.ToString(), body);
                Assert.DoesNotContain(hidden.Code, body);
            }

            // A read stays a read, including the grant this case added.
            Assert.Equal(baseline, await ReadEffectsAsync(factory, TestCompanyIds.CompanyA));
        });
    }

    private static async Task<Guid> ResolveLocationIdAsync(
        SqlServerRelationalApiFactory factory,
        string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
        var database = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        return (await database.Locations.AsNoTracking()
            .SingleAsync(location => location.Code == code)).Id;
    }

    /// <summary>
    /// Grants one permission on one LOCATION scope. It adds a role rather than reusing a
    /// seeded one so the actor's only work-center authority is this scope; a seeded
    /// Organization role would carry the site scope as well and the union would answer
    /// with the whole site.
    /// </summary>
    private static async Task GrantWorkCenterReadOnLocationAsync(
        SqlServerRelationalApiFactory factory,
        string subject,
        Guid locationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var userId = await identity.ExternalIdentities
            .Where(link => link.Subject == subject)
            .Select(link => link.UserId)
            .SingleAsync();
        var role = new RoleDefinition(
            Guid.NewGuid(),
            $"SQL_WORKCENTER_LOCATION_SCOPE_{Guid.NewGuid():N}",
            "SQL ORG-03 Lokasyon Kapsamlı Okuyucu",
            isSystem: false);
        role.AddPermission(ErpPermissions.OrganizationWorkCentersRead);
        var assignment = new RoleAssignment(
            Guid.NewGuid(),
            userId,
            role.Id,
            TestCompanyIds.CompanyA,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            null,
            null);
        assignment.AddScope(AccessScopeType.Location, locationId);
        identity.Roles.Add(role);
        identity.RoleAssignments.Add(assignment);
        await identity.SaveChangesAsync();
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
