using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Contracts;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class OrganizationLocationLifecycleSqlServerRelationalAcceptanceTests
{
    private const string LocationsRoute = "/api/v1/organization/locations";

    [SqlServerFact]
    public async Task ListDetailDeactivateReactivateAndDelayedReplay_ReturnFreshStateAndSingleAuditPerCommand()
    {
        await WithDatabaseAsync(async factory =>
        {
            using var client = CreateClient(factory, TestUsers.CompanyA, TestCompanyIds.CompanyA);
            var sites = await client.GetFromJsonAsync<SiteSummary[]>("/api/v1/organization/sites");
            Assert.NotNull(sites);
            var initialPage = await ReadPageAsync(client);
            var initial = Assert.Single(initialPage.Items, item => item.Code == "OSM-H0");
            Assert.Contains(sites, site => site.Id == initial.SiteId && site.IsActive);
            Assert.True(initial.IsActive);
            Assert.Equal(initial, await ReadDetailAsync(client, initial.Id));
            var baseline = await ReadEffectsAsync(factory, TestCompanyIds.CompanyA);

            const string deactivateReason = "Bakım için pasife alma";
            var deactivate = new SetLocationActiveState(false, initial.Version, deactivateReason);
            var deactivateKey = Guid.NewGuid().ToString("N");
            var deactivateCorrelation = Guid.NewGuid().ToString("N");
            using (var response = await SendCommandAsync(
                client, initial.Id, deactivate, deactivateKey, deactivateCorrelation))
            {
                await AssertNoContentAsync(response, replay: false);
            }

            using var freshClient = CreateClient(factory, TestUsers.CompanyA, TestCompanyIds.CompanyA);
            var inactive = await AssertFreshStateAsync(freshClient, initial, false, initial.Version + 1);
            var inactivePage = await ReadPageAsync(freshClient);
            Assert.Equal(initialPage.TotalCount, inactivePage.TotalCount);
            Assert.Equal(initialPage.Items.Select(item => item.Id), inactivePage.Items.Select(item => item.Id));

            var stale = new SetLocationActiveState(true, initial.Version, "Eski sürümle açma");
            var staleKey = Guid.NewGuid().ToString("N");
            var staleCorrelation = Guid.NewGuid().ToString("N");
            using (var response = await SendCommandAsync(
                freshClient, initial.Id, stale, staleKey, staleCorrelation))
            {
                await AssertVersionConflictAsync(response, initial.Version, inactive.Version);
                Assert.False(response.Headers.Contains("Idempotency-Replayed"));
            }
            Assert.Equal(inactive, await ReadDetailAsync(freshClient, initial.Id));

            const string activateReason = "Bakım tamamlandı";
            var activate = new SetLocationActiveState(true, inactive.Version, activateReason);
            var activateKey = Guid.NewGuid().ToString("N");
            var activateCorrelation = Guid.NewGuid().ToString("N");
            using (var response = await SendCommandAsync(
                freshClient, initial.Id, activate, activateKey, activateCorrelation))
            {
                await AssertNoContentAsync(response, replay: false);
            }
            var active = await AssertFreshStateAsync(freshClient, initial, true, initial.Version + 2);
            var afterActivation = await ReadEffectsAsync(factory, TestCompanyIds.CompanyA);
            Assert.Equal(baseline with { Audits = baseline.Audits + 2, Executions = baseline.Executions + 3 }, afterActivation);

            // A delayed 204 replay acknowledges the old command, not the current active state.
            using (var response = await SendCommandAsync(
                freshClient, initial.Id, deactivate, deactivateKey, Guid.NewGuid().ToString("N")))
            {
                await AssertNoContentAsync(response, replay: true);
                Assert.Equal(deactivateCorrelation, response.Headers.GetValues("Idempotency-Original-Correlation-Id").Single());
            }
            using (var response = await SendCommandAsync(
                freshClient, initial.Id, stale, staleKey, Guid.NewGuid().ToString("N")))
            {
                // The durable conflict snapshot is replayed even after the current version advanced.
                await AssertVersionConflictAsync(response, initial.Version, inactive.Version);
                Assert.Equal("true", response.Headers.GetValues("Idempotency-Replayed").Single());
                Assert.Equal(staleCorrelation, response.Headers.GetValues("Idempotency-Original-Correlation-Id").Single());
            }
            Assert.Equal(active, await AssertFreshStateAsync(freshClient, initial, true, active.Version));
            Assert.Equal(afterActivation, await ReadEffectsAsync(factory, TestCompanyIds.CompanyA));

            var noChangeCorrelation = Guid.NewGuid().ToString("N");
            using (var response = await SendCommandAsync(
                freshClient, initial.Id, new SetLocationActiveState(true, active.Version, "Aktif durum doğrulandı"),
                Guid.NewGuid().ToString("N"), noChangeCorrelation))
            {
                await AssertNoContentAsync(response, replay: false);
            }
            Assert.Equal(active, await AssertFreshStateAsync(freshClient, initial, true, active.Version));
            Assert.Equal(afterActivation with { Audits = afterActivation.Audits + 1, Executions = afterActivation.Executions + 1 },
                await ReadEffectsAsync(factory, TestCompanyIds.CompanyA));
            Assert.Equal(sites, await freshClient.GetFromJsonAsync<SiteSummary[]>("/api/v1/organization/sites"));

            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            var audits = await db.Set<AuditEvent>().AsNoTracking()
                .Where(item => item.EntityId == initial.Id.ToString()).ToListAsync();
            Assert.Equal(3, audits.Count);
            AssertAudit(Assert.Single(audits, item => item.CorrelationId == deactivateCorrelation),
                initial, inactive, "Deactivate", deactivateReason);
            AssertAudit(Assert.Single(audits, item => item.CorrelationId == activateCorrelation),
                inactive, active, "Activate", activateReason);
            AssertAudit(Assert.Single(audits, item => item.CorrelationId == noChangeCorrelation),
                active, active, "NoChange", "Aktif durum doğrulandı");
            Assert.All(await db.CommandExecutions.AsNoTracking().ToListAsync(), execution =>
                Assert.Equal(TestCompanyIds.CompanyA, execution.CompanyId));
        });
    }

    [SqlServerFact]
    public async Task ScopedPagingAndRejectedCommands_PreserveLocationAndExposeNoForeignState()
    {
        await WithDatabaseAsync(async factory =>
        {
            using var client = CreateClient(factory, TestUsers.CompanyA, TestCompanyIds.CompanyA);
            var page = await ReadPageAsync(client);
            var location = Assert.Single(page.Items, item => item.Code == "OSM-H0");
            var baseline = await ReadEffectsAsync(factory, TestCompanyIds.CompanyA);
            using var viewer = CreateClient(factory, TestUsers.CompanyAViewer, TestCompanyIds.CompanyA);
            var sites = await viewer.GetFromJsonAsync<SiteSummary[]>("/api/v1/organization/sites");
            var site = Assert.Single(Assert.IsType<SiteSummary[]>(sites));
            var first = await ReadPageAsync(viewer, "?page=1&pageSize=3");
            var second = await ReadPageAsync(viewer, "?page=2&pageSize=3");
            var last = await ReadPageAsync(viewer, "?page=3&pageSize=3");
            var all = first.Items.Concat(second.Items).Concat(last.Items).ToArray();
            Assert.Equal(8, first.TotalCount);
            Assert.Equal(first.TotalCount, second.TotalCount);
            Assert.Equal(first.TotalCount, last.TotalCount);
            Assert.Equal(8, all.Select(item => item.Id).Distinct().Count());
            Assert.All(all, item => Assert.Equal(site.Id, item.SiteId));
            Assert.Equal(all.OrderBy(item => item.Code, StringComparer.Ordinal), all);
            var empty = await ReadPageAsync(viewer, "?page=4&pageSize=3");
            Assert.Empty(empty.Items);
            Assert.Equal(8, empty.TotalCount);
            var normalized = await ReadPageAsync(viewer, "?page=0&pageSize=101");
            Assert.Equal(1, normalized.Page);
            Assert.Equal(100, normalized.PageSize);
            Assert.Equal(all, normalized.Items);

            var hidden = Assert.Single(page.Items, item => item.Code == "GAZ-DEP");
            using (var response = await viewer.GetAsync($"{LocationsRoute}/{hidden.Id}"))
            {
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.DoesNotContain(hidden.Id.ToString(), await response.Content.ReadAsStringAsync());
            }
            var command = new SetLocationActiveState(false, location.Version, "Yetki sınırı kontrolü");
            using (var response = await viewer.PutAsJsonAsync($"{LocationsRoute}/{location.Id}/active-state", command))
            {
                await AssertPlatformErrorAsync(response, HttpStatusCode.Forbidden, "forbidden");
            }
            using var anonymous = factory.CreateClient();
            using (var response = await anonymous.PutAsJsonAsync($"{LocationsRoute}/{location.Id}/active-state", command))
            {
                await AssertPlatformErrorAsync(response, HttpStatusCode.Unauthorized, "authentication_required");
            }
            using var wrongCompany = CreateClient(factory, TestUsers.CompanyA, TestCompanyIds.CompanyB);
            using (var response = await wrongCompany.GetAsync(LocationsRoute))
            {
                await AssertPlatformErrorAsync(response, HttpStatusCode.Forbidden, "forbidden");
            }
            using var foreign = CreateClient(factory, TestUsers.CompanyB, TestCompanyIds.CompanyB);
            Assert.Empty((await ReadPageAsync(foreign)).Items);
            using (var response = await foreign.GetAsync($"{LocationsRoute}/{location.Id}"))
            {
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.DoesNotContain(location.Id.ToString(), await response.Content.ReadAsStringAsync());
            }
            using (var response = await client.PutAsJsonAsync($"{LocationsRoute}/{location.Id}/active-state",
                command with { Reason = "   " }))
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }
            Assert.Equal(location, await ReadDetailAsync(client, location.Id));
            Assert.Equal(baseline, await ReadEffectsAsync(factory, TestCompanyIds.CompanyA));
        });
    }

    private static HttpClient CreateClient(SqlServerRelationalApiFactory factory, string user, Guid company)
    {
        var client = factory.CreateClientFor(user, company);
        // The shared factory's Cari handshake is not an Organization contract.
        client.DefaultRequestHeaders.Remove(CounterpartyClientContract.HeaderName);
        return client;
    }

    private static async Task<PageResult<LocationSummary>> ReadPageAsync(HttpClient client, string query = "?pageSize=100") =>
        Assert.IsType<PageResult<LocationSummary>>(await client.GetFromJsonAsync<PageResult<LocationSummary>>(LocationsRoute + query));

    private static async Task<LocationSummary> ReadDetailAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"{LocationsRoute}/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Number, body.RootElement.GetProperty("version").ValueKind);
        return Assert.IsType<LocationSummary>(body.RootElement.Deserialize<LocationSummary>(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static async Task<LocationSummary> AssertFreshStateAsync(HttpClient client, LocationSummary initial, bool active, long version)
    {
        var detail = await ReadDetailAsync(client, initial.Id);
        Assert.Equal(initial with { IsActive = active, Version = version }, detail);
        Assert.Equal(detail, Assert.Single((await ReadPageAsync(client)).Items, item => item.Id == initial.Id));
        return detail;
    }

    private static async Task<HttpResponseMessage> SendCommandAsync(HttpClient client, Guid id,
        SetLocationActiveState command, string key, string correlation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{LocationsRoute}/{id}/active-state");
        request.Content = JsonContent.Create(command);
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", correlation);
        return await client.SendAsync(request);
    }

    private static async Task AssertNoContentAsync(HttpResponseMessage response, bool replay)
    {
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(replay, response.Headers.Contains("Idempotency-Replayed"));
        if (replay)
        {
            Assert.Equal("true", response.Headers.GetValues("Idempotency-Replayed").Single());
        }
    }

    private static async Task AssertVersionConflictAsync(HttpResponseMessage response, long expected, long current)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var conflict = body.RootElement.GetProperty("conflict");
        Assert.Equal(expected, conflict.GetProperty("expectedVersion").GetInt64());
        Assert.Equal(current, conflict.GetProperty("currentVersion").GetInt64());
    }

    private static async Task AssertPlatformErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    private static void AssertAudit(AuditEvent audit, LocationSummary before, LocationSummary after, string operation, string reason)
    {
        Assert.Equal(TestCompanyIds.CompanyA, audit.CompanyId);
        Assert.Equal(ErpPermissions.OrganizationLocationsStatusUpdate, audit.PermissionCode);
        Assert.Equal(operation, audit.Operation);
        Assert.Equal(reason, audit.Reason);
        Assert.Equal(before.Version, audit.EntityVersionBefore);
        Assert.Equal(after.Version, audit.EntityVersionAfter);
        Assert.NotEmpty(audit.ActorId);
        Assert.NotEmpty(JsonSerializer.Deserialize<string[]>(audit.RoleCodesJson)!);
        using var beforeBody = JsonDocument.Parse(audit.BeforeJson);
        using var afterBody = JsonDocument.Parse(audit.AfterJson);
        Assert.Equal(before.IsActive, beforeBody.RootElement.GetProperty("IsActive").GetBoolean());
        Assert.Equal(after.IsActive, afterBody.RootElement.GetProperty("IsActive").GetBoolean());
    }

    private static async Task<Effects> ReadEffectsAsync(SqlServerRelationalApiFactory factory, Guid company)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
        var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        Assert.True(db.Database.IsSqlServer());
        return new Effects(await db.Locations.CountAsync(), await db.Sites.CountAsync(),
            await db.Set<AuditEvent>().CountAsync(), await db.CommandExecutions.CountAsync());
    }

    private static async Task WithDatabaseAsync(Func<SqlServerRelationalApiFactory, Task> test)
    {
        var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"GoymenERP_Organization_Locations_{Guid.NewGuid():N}";
        await using var factory = new SqlServerRelationalApiFactory(connection,
            applyTestCompanyBTopologyTemplates: false, databaseName: databaseName);
        try
        {
            await test(factory);
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            Assert.Equal(databaseName, db.Database.GetDbConnection().Database);
            await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed record Effects(int Locations, int Sites, int Audits, int Executions);
}
