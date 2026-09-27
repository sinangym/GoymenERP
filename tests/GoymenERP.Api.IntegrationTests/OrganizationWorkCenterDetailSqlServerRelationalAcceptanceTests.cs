using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Contracts;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// ORG-04, one work center's read-only card (W1-ORG / AV-01, Business #66 5702892195), through the real host on a
/// real SQL Server. The card is the ORG-03 row's own record: the same scope predicate answers both, so a listed row
/// always opens and an unlisted one is the same code-less 404 as an unknown or another company's id. The location
/// flag is the list's flag by the same rule, and the read writes nothing. The grants are synthetic, isolated role
/// assignments in this test's own database; they are not a Sinan or production grant and are never seeded.
/// </summary>
public sealed class OrganizationWorkCenterDetailSqlServerRelationalAcceptanceTests
{
    private const string WorkCentersRoute = "/api/v1/organization/work-centers";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [SqlServerFact]
    public async Task The_card_is_the_listed_row_with_its_version_only_for_the_context_company_and_writes_nothing()
    {
        await WithDatabaseAsync(async factory =>
        {
            using var client = CreateClient(factory, TestUsers.CompanyA, TestCompanyIds.CompanyA);
            var rows = await ReadListAsync(client);
            Assert.NotEmpty(rows);
            var baseline = await ReadEffectsAsync(factory, TestCompanyIds.CompanyA);

            foreach (var row in rows)
            {
                using var response = await client.GetAsync($"{WorkCentersRoute}/{row.Id}");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                // Exactly the contract's fields; the version is a JSON number, lossless and never a string.
                Assert.Equal(
                    ["canOpenLocationDetail", "code", "id", "isActive", "kind", "locationId", "name", "version"],
                    body.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
                Assert.Equal(JsonValueKind.Number, body.RootElement.GetProperty("version").ValueKind);
                var card = Assert.IsType<WorkCenterDetail>(body.RootElement.Deserialize<WorkCenterDetail>(Json));
                Assert.True(card.Version >= 1);

                // The card is the row: every shared field, the location flag included, answered by the same predicates.
                Assert.Equal(
                    (row.Id, row.LocationId, row.Code, row.Name, row.Kind, row.IsActive, row.CanOpenLocationDetail),
                    (card.Id, card.LocationId, card.Code, card.Name, card.Kind, card.IsActive, card.CanOpenLocationDetail));
                var stored = await ReadStoredAsync(factory, TestCompanyIds.CompanyA, row.Id);
                Assert.Equal((stored.Version, stored.IsActive), (card.Version, card.IsActive));
            }

            // An unknown id in the caller's own company is the code-less 404 another company's id gets below.
            var unknown = Guid.NewGuid();
            using (var response = await client.GetAsync($"{WorkCentersRoute}/{unknown}"))
            {
                await AssertCodeLessNotFoundAsync(response, unknown);
            }

            // Company B holds the read in its OWN company and still cannot see A's work center: not found, never forbidden.
            var target = rows[0];
            using var companyB = CreateClient(factory, TestUsers.CompanyB, TestCompanyIds.CompanyB);
            using (var response = await companyB.GetAsync($"{WorkCentersRoute}/{target.Id}"))
            {
                await AssertCodeLessNotFoundAsync(response, target.Id);
            }

            // B's user naming company A is refused before anything is read.
            using var wrongCompany = CreateClient(factory, TestUsers.CompanyB, TestCompanyIds.CompanyA);
            using (var response = await wrongCompany.GetAsync($"{WorkCentersRoute}/{target.Id}"))
            {
                await AssertPlatformErrorAsync(response, HttpStatusCode.Forbidden, "forbidden");
            }

            // Without a company context nothing runs, and without authentication nothing is read.
            using (var noContext = factory.CreateClient())
            {
                noContext.DefaultRequestHeaders.Add("X-Development-User", TestUsers.CompanyA);
                using var response = await noContext.GetAsync($"{WorkCentersRoute}/{target.Id}");
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }

            using (var anonymous = factory.CreateClient())
            {
                using var response = await anonymous.GetAsync($"{WorkCentersRoute}/{target.Id}");
                await AssertPlatformErrorAsync(response, HttpStatusCode.Unauthorized, "authentication_required");
            }

            // A read: no Organization row, audit event or command record changed.
            Assert.Equal(baseline, await ReadEffectsAsync(factory, TestCompanyIds.CompanyA));
        });
    }

    [SqlServerFact]
    public async Task A_work_center_scoped_reader_opens_only_its_card_and_the_location_link_follows_the_location_read()
    {
        await WithDatabaseAsync(async factory =>
        {
            using var operatorClient = CreateClient(factory, TestUsers.CompanyA, TestCompanyIds.CompanyA);
            var rows = await ReadListAsync(operatorClient);
            Assert.True(rows.Count >= 2, "the runtime seed publishes more than one work center");
            var granted = rows[0];
            var other = rows.First(row => row.Id != granted.Id);

            // organization.work-centers.read on ONE work center, and no location read at all.
            await GrantAsync(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA,
                ErpPermissions.OrganizationWorkCentersRead, AccessScopeType.WorkCenter, granted.Id);
            using (var reader = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA))
            {
                var listed = await ReadListAsync(reader);
                Assert.Equal(granted.Id, Assert.Single(listed).Id);

                var card = await ReadCardAsync(reader, granted.Id);
                Assert.Equal(granted.Id, card.Id);
                // No location grant: the card never offers the location, exactly as the row does not.
                Assert.False(card.CanOpenLocationDetail);
                Assert.Equal(Assert.Single(listed).CanOpenLocationDetail, card.CanOpenLocationDetail);

                // A work center outside the scope is the same 404 as an unknown id: the row is not disclosed.
                using var response = await reader.GetAsync($"{WorkCentersRoute}/{other.Id}");
                await AssertCodeLessNotFoundAsync(response, other.Id);

                // The location card itself stays closed to this reader.
                using var location = await reader.GetAsync($"/api/v1/organization/locations/{granted.LocationId}");
                Assert.Equal(HttpStatusCode.Forbidden, location.StatusCode);
            }

            // + a location read that reaches exactly this work center's location: now the card offers it, and the
            // location card answers 200 for the same caller.
            await GrantAsync(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA,
                ErpPermissions.OrganizationLocationsRead, AccessScopeType.Location, granted.LocationId);
            using (var reader = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA))
            {
                var card = await ReadCardAsync(reader, granted.Id);
                Assert.True(card.CanOpenLocationDetail);
                Assert.Equal(card.CanOpenLocationDetail, Assert.Single(await ReadListAsync(reader)).CanOpenLocationDetail);
                using var location = await reader.GetAsync($"/api/v1/organization/locations/{granted.LocationId}");
                Assert.Equal(HttpStatusCode.OK, location.StatusCode);
            }

            // Every other Organization read does not add up to the work-centre read.
            await GrantAsync(factory, TestUsers.Unprovisioned, TestCompanyIds.CompanyA,
                ErpPermissions.OrganizationLocationsRead, AccessScopeType.Company, TestCompanyIds.CompanyA);
            using (var locationsOnly = CreateClient(factory, TestUsers.Unprovisioned, TestCompanyIds.CompanyA))
            {
                using var response = await locationsOnly.GetAsync($"{WorkCentersRoute}/{granted.Id}");
                await AssertPlatformErrorAsync(response, HttpStatusCode.Forbidden, "forbidden");
            }
        });
    }

    private static HttpClient CreateClient(SqlServerRelationalApiFactory factory, string user, Guid company)
    {
        var client = factory.CreateClientFor(user, company);
        // The shared factory's Cari handshake is not an Organization contract.
        client.DefaultRequestHeaders.Remove(CounterpartyClientContract.HeaderName);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        return client;
    }

    private static async Task<IReadOnlyList<WorkCenterSummary>> ReadListAsync(HttpClient client)
    {
        using var response = await client.GetAsync(WorkCentersRoute);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<List<WorkCenterSummary>>(await response.Content.ReadFromJsonAsync<List<WorkCenterSummary>>(Json));
    }

    private static async Task<WorkCenterDetail> ReadCardAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"{WorkCentersRoute}/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<WorkCenterDetail>(await response.Content.ReadFromJsonAsync<WorkCenterDetail>(Json));
    }

    private static async Task AssertCodeLessNotFoundAsync(HttpResponseMessage response, Guid id)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(id.ToString(), text, StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(text);
        Assert.False(body.RootElement.TryGetProperty("code", out _));
    }

    private static async Task AssertPlatformErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    private static async Task<(long Version, bool IsActive)> ReadStoredAsync(
        SqlServerRelationalApiFactory factory, Guid company, Guid workCenterId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
        var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        var stored = await db.WorkCenters.AsNoTracking().SingleAsync(workCenter => workCenter.Id == workCenterId);
        return (stored.Version, stored.IsActive);
    }

    private static async Task<Effects> ReadEffectsAsync(SqlServerRelationalApiFactory factory, Guid company)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
        var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        Assert.True(db.Database.IsSqlServer());
        return new Effects(
            await db.WorkCenters.CountAsync(),
            await db.WorkCenters.SumAsync(workCenter => workCenter.Version),
            await db.Locations.CountAsync(),
            await db.Set<AuditEvent>().CountAsync(),
            await db.CommandExecutions.CountAsync());
    }

    /// <summary>A synthetic role with exactly this permission in one company, assigned to a configured development user under one scope.</summary>
    private static async Task GrantAsync(SqlServerRelationalApiFactory factory, string developmentUser, Guid company,
        string permission, AccessScopeType scopeType, Guid scopeId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
            x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
        Guid actor;
        if (link is null)
        {
            actor = Guid.NewGuid();
            dbContext.Users.Add(new UserAccount(actor, "Synthetic Organization work-centre user", null));
            dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
        }
        else
        {
            actor = link.UserId;
        }

        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"ORG-WC-CARD-{roleId:N}", "Synthetic Organization work-centre card role", true);
        role.AddPermission(permission);
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null,
            approvedByUserId: null);
        assignment.AddScope(scopeType, scopeId);
        dbContext.Roles.Add(role);
        dbContext.RoleAssignments.Add(assignment);
        await dbContext.SaveChangesAsync();
    }

    private static async Task WithDatabaseAsync(Func<SqlServerRelationalApiFactory, Task> test)
    {
        var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"GoymenERP_Organization_WorkCenterCard_{Guid.NewGuid():N}";
        await using var factory = new SqlServerRelationalApiFactory(connection,
            applyTestCompanyBTopologyTemplates: false, databaseName: databaseName);
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

    private sealed record Effects(int WorkCenters, long WorkCenterVersions, int Locations, int Audits, int Executions);
}
