using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Contracts;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
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
/// The first-release outcome "create and edit sites, locations and work centres", through the real host on a real
/// SQL Server: save, close, reopen, and the same record is what the next module's own read answers. Replayed keys
/// apply nothing twice, a stale version is a 409 with its conflict, a used code is 422, another company's record is a
/// code-less 404, and every applied command writes one Web-channel audit row. The grants are synthetic, isolated role
/// assignments in this test's own database; they are not a Sinan or production grant and are never seeded.
/// </summary>
public sealed class OrganizationMasterDataSqlServerRelationalAcceptanceTests
{
    private const string Root = "/api/v1/organization";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly string[] EditorPermissions =
    [
        ErpPermissions.OrganizationSitesRead,
        ErpPermissions.OrganizationLocationsRead,
        ErpPermissions.OrganizationWorkCentersRead,
        OrganizationPermissionManifestV1.SitesWrite,
        OrganizationPermissionManifestV1.LocationsWrite,
        OrganizationPermissionManifestV1.WorkCentersWrite,
    ];

    [SqlServerFact]
    public async Task Create_edit_close_reopen_and_the_next_modules_read_the_same_records()
    {
        await WithDatabaseAsync(async factory =>
        {
            await GrantAsync(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA, EditorPermissions);
            using var editor = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            var baseline = await ReadEffectsAsync(factory, TestCompanyIds.CompanyA);

            // Site: created once, replayed without a second row.
            var siteBody = new CreateSite("SYN-TESIS-9", "Sentetik Tesis 9", "Warehouse", "Yeni depo tesisi");
            var siteKey = Key();
            var siteCorrelation = Key();
            OrganizationCreatedRecord site;
            using (var response = await SendAsync(editor, HttpMethod.Post, $"{Root}/sites", siteBody, siteKey, siteCorrelation))
            {
                site = await AssertCreatedAsync(response, replay: false);
            }

            using (var response = await SendAsync(editor, HttpMethod.Post, $"{Root}/sites", siteBody, siteKey, Key()))
            {
                Assert.Equal(site, await AssertCreatedAsync(response, replay: true));
                Assert.Equal(siteCorrelation, response.Headers.GetValues("Idempotency-Original-Correlation-Id").Single());
            }

            // Location under the new site, then renamed; a stale version is refused with its conflict.
            var locationBody = new CreateLocation(site.Id, "SYN-DEPO-9", "Sentetik Depo 9", "Yeni depo alanı");
            OrganizationCreatedRecord location;
            using (var response = await SendAsync(editor, HttpMethod.Post, $"{Root}/locations", locationBody, Key(), Key()))
            {
                location = await AssertCreatedAsync(response, replay: false);
                Assert.Equal($"/api/v1/organization/locations/{location.Id}", response.Headers.Location?.OriginalString);
            }

            var renameKey = Key();
            var rename = new UpdateLocation("SYN-DEPO-9A", "Sentetik Depo 9 A", location.Version, "Ad düzeltildi");
            using (var response = await SendAsync(editor, HttpMethod.Put, $"{Root}/locations/{location.Id}", rename, renameKey, Key()))
            {
                await AssertNoContentAsync(response, replay: false);
            }

            using (var response = await SendAsync(editor, HttpMethod.Put, $"{Root}/locations/{location.Id}", rename, renameKey, Key()))
            {
                await AssertNoContentAsync(response, replay: true);
            }

            using (var response = await SendAsync(editor, HttpMethod.Put, $"{Root}/locations/{location.Id}",
                rename with { Name = "Eski ekrandan", Reason = "Eski sürüm" }, Key(), Key()))
            {
                await AssertVersionConflictAsync(response, location.Version, location.Version + 1);
            }

            using (var response = await SendAsync(editor, HttpMethod.Post, $"{Root}/locations",
                locationBody with { Code = "SYN-DEPO-9A" }, Key(), Key()))
            {
                await AssertCodeInUseAsync(response);
            }

            // Work centre on the new location, then renamed.
            OrganizationCreatedRecord workCenter;
            using (var response = await SendAsync(editor, HttpMethod.Post, $"{Root}/work-centers",
                new CreateWorkCenter(location.Id, "SYN-HAT-9", "Sentetik Dilme 9", "Slitting", "Yeni hat"), Key(), Key()))
            {
                workCenter = await AssertCreatedAsync(response, replay: false);
            }

            using (var response = await SendAsync(editor, HttpMethod.Put, $"{Root}/work-centers/{workCenter.Id}",
                new UpdateWorkCenter("SYN-HAT-9", "Sentetik Dilme Hattı 9", workCenter.Version, "Ad netleşti"), Key(), Key()))
            {
                await AssertNoContentAsync(response, replay: false);
            }

            // Close and reopen: a fresh client reads back exactly what was saved.
            using (var reopened = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA))
            {
                var sites = await reopened.GetFromJsonAsync<SiteSummary[]>($"{Root}/sites", Json);
                Assert.Contains(new SiteSummary(site.Id, "SYN-TESIS-9", "Sentetik Tesis 9", "Warehouse", true, 1), sites!);
                var card = await reopened.GetFromJsonAsync<LocationSummary>($"{Root}/locations/{location.Id}", Json);
                Assert.Equal(new LocationSummary(location.Id, site.Id, "SYN-DEPO-9A", "Sentetik Depo 9 A", false, true, 2), card);
                var wc = await reopened.GetFromJsonAsync<WorkCenterDetail>($"{Root}/work-centers/{workCenter.Id}", Json);
                Assert.Equal(
                    (location.Id, "SYN-HAT-9", "Sentetik Dilme Hattı 9", "Slitting", true, 2L, true),
                    (wc!.LocationId, wc.Code, wc.Name, wc.Kind, wc.IsActive, wc.Version, wc.CanOpenLocationDetail));
            }

            // The next modules' own Organization reads (Inventory's location selector, Manufacturing's planning
            // reference) answer the new records without any Organization user grant.
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                var candidates = await scope.ServiceProvider.GetRequiredService<IOrganizationLocationCandidatesV1>()
                    .SearchAsync(TestCompanyIds.CompanyA, new OrganizationLocationCandidateQueryV1("SYN-DEPO-9A"));
                var candidate = Assert.Single(candidates.Items);
                Assert.Equal((location.Id, site.Id, "SYN-TESIS-9"), (candidate.LocationId, candidate.SiteId, candidate.SiteCode));
                Assert.Equal(
                    new OrganizationWorkCenterPlanningReferenceV1(workCenter.Id, "Slitting"),
                    await scope.ServiceProvider.GetRequiredService<IOrganizationWorkCenterPlanningReferenceV1>()
                        .ReadAsync(TestCompanyIds.CompanyA, workCenter.Id));
            }

            // Five applied commands (3 creates, 2 edits): five audit rows; seven stored answers (the 409 and 422 too, the replays none).
            var after = await ReadEffectsAsync(factory, TestCompanyIds.CompanyA);
            Assert.Equal(
                baseline with
                {
                    Sites = baseline.Sites + 1,
                    Locations = baseline.Locations + 1,
                    LocationVersions = baseline.LocationVersions + 2,
                    WorkCenters = baseline.WorkCenters + 1,
                    Audits = baseline.Audits + 5,
                    Executions = baseline.Executions + 7,
                },
                after);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
                var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
                var ids = new[] { site.Id, location.Id, workCenter.Id }.Select(id => id.ToString("D")).ToArray();
                var audits = await db.Set<AuditEvent>().AsNoTracking().Where(x => ids.Contains(x.EntityId)).ToListAsync();
                Assert.Equal(5, audits.Count);
                Assert.All(audits, audit =>
                {
                    Assert.Equal(TestCompanyIds.CompanyA, audit.CompanyId);
                    Assert.Equal(SourceChannels.Web, audit.SourceChannel);
                    Assert.NotEmpty(audit.Reason);
                    Assert.Contains(audit.PermissionCode, OrganizationPermissionManifestV1.Names);
                });
                Assert.Equal(
                    ["organization.location.created.v1", "organization.location.updated.v1", "organization.site.created.v1",
                        "organization.work-center.created.v1", "organization.work-center.updated.v1"],
                    audits.Select(x => x.EventType).Order(StringComparer.Ordinal));
                var locationUpdate = Assert.Single(audits, x => x.EventType == "organization.location.updated.v1");
                Assert.Equal(("Update", 1L, 2L), (locationUpdate.Operation, locationUpdate.EntityVersionBefore, locationUpdate.EntityVersionAfter));
            }
        });
    }

    [SqlServerFact]
    public async Task Another_company_and_a_caller_without_the_write_change_nothing()
    {
        await WithDatabaseAsync(async factory =>
        {
            using var operatorClient = CreateClient(factory, TestUsers.CompanyA, TestCompanyIds.CompanyA);
            var page = await operatorClient.GetFromJsonAsync<PageResult<LocationSummary>>($"{Root}/locations?pageSize=100", Json);
            var target = Assert.Single(page!.Items, item => item.Code == "OSM-H0");

            // The same synthetic writer holds the location write in company B only.
            await GrantAsync(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyB,
                [ErpPermissions.OrganizationLocationsRead, OrganizationPermissionManifestV1.LocationsWrite]);
            var baseline = await ReadEffectsAsync(factory, TestCompanyIds.CompanyA);

            using (var inB = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyB))
            using (var response = await SendAsync(inB, HttpMethod.Put, $"{Root}/locations/{target.Id}",
                new UpdateLocation("SYN-X", "Yabancı şirket", target.Version, "Deneme"), Key(), Key()))
            {
                await AssertCodeLessNotFoundAsync(response, target.Id);
            }

            // Naming company A without an assignment there is refused at the gate.
            using (var intoA = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA))
            using (var response = await SendAsync(intoA, HttpMethod.Put, $"{Root}/locations/{target.Id}",
                new UpdateLocation("SYN-X", "Yabancı şirket", target.Version, "Deneme"), Key(), Key()))
            {
                await AssertPlatformErrorAsync(response, HttpStatusCode.Forbidden, "forbidden");
            }

            // A reader without the write grant cannot create or edit.
            using (var viewer = CreateClient(factory, TestUsers.CompanyAViewer, TestCompanyIds.CompanyA))
            {
                using (var response = await SendAsync(viewer, HttpMethod.Post, $"{Root}/sites",
                    new CreateSite("SYN-V", "Salt okuma", "Factory", "Deneme"), Key(), Key()))
                {
                    await AssertPlatformErrorAsync(response, HttpStatusCode.Forbidden, "forbidden");
                }

                using (var response = await SendAsync(viewer, HttpMethod.Put, $"{Root}/locations/{target.Id}",
                    new UpdateLocation("SYN-V", "Salt okuma", target.Version, "Deneme"), Key(), Key()))
                {
                    await AssertPlatformErrorAsync(response, HttpStatusCode.Forbidden, "forbidden");
                }
            }

            // Missing key, bad shapes: 400 before anything runs.
            await GrantAsync(factory, TestUsers.Unprovisioned, TestCompanyIds.CompanyA, EditorPermissions);
            using (var editor = CreateClient(factory, TestUsers.Unprovisioned, TestCompanyIds.CompanyA))
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, $"{Root}/sites"))
                {
                    request.Content = JsonContent.Create(new CreateSite("SYN-K", "Anahtarsız", "Factory", "Deneme"), options: Json);
                    request.Headers.Add("X-Correlation-Id", Key());
                    using var response = await editor.SendAsync(request);
                    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                }

                foreach (var (path, method, body) in new (string, HttpMethod, object)[]
                {
                    ($"{Root}/sites", HttpMethod.Post, new CreateSite("SYN-K", "X", "factory", "Deneme")),
                    ($"{Root}/sites", HttpMethod.Post, new CreateSite("   ", "X", "Factory", "Deneme")),
                    ($"{Root}/locations", HttpMethod.Post, new CreateLocation(target.SiteId, "SYN-K", "X", " ")),
                    ($"{Root}/work-centers", HttpMethod.Post, new CreateWorkCenter(target.Id, "SYN-K", "X", "Welding", "Deneme")),
                    ($"{Root}/locations/{target.Id}", HttpMethod.Put, new UpdateLocation("SYN-K", "X", 0, "Deneme")),
                })
                {
                    using var response = await SendAsync(editor, method, path, body, Key(), Key());
                    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                }
            }

            Assert.Equal(target, await operatorClient.GetFromJsonAsync<LocationSummary>($"{Root}/locations/{target.Id}", Json));
            Assert.Equal(baseline, await ReadEffectsAsync(factory, TestCompanyIds.CompanyA));
        });
    }

    private static string Key() => Guid.NewGuid().ToString("N");

    private static HttpClient CreateClient(SqlServerRelationalApiFactory factory, string user, Guid company)
    {
        var client = factory.CreateClientFor(user, company);
        // The shared factory's Cari handshake is not an Organization contract.
        client.DefaultRequestHeaders.Remove(CounterpartyClientContract.HeaderName);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        return client;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string path, object body, string key, string correlation)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", correlation);
        return await client.SendAsync(request);
    }

    private static async Task<OrganizationCreatedRecord> AssertCreatedAsync(HttpResponseMessage response, bool replay)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(replay, response.Headers.Contains("Idempotency-Replayed"));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["id", "version"], body.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(JsonValueKind.Number, body.RootElement.GetProperty("version").ValueKind);
        var created = Assert.IsType<OrganizationCreatedRecord>(body.RootElement.Deserialize<OrganizationCreatedRecord>(Json));
        Assert.Equal(1, created.Version);
        return created;
    }

    private static async Task AssertNoContentAsync(HttpResponseMessage response, bool replay)
    {
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(replay, response.Headers.Contains("Idempotency-Replayed"));
    }

    private static async Task AssertVersionConflictAsync(HttpResponseMessage response, long expected, long current)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.TryGetProperty("code", out _));
        var conflict = body.RootElement.GetProperty("conflict");
        Assert.Equal(expected, conflict.GetProperty("expectedVersion").GetInt64());
        Assert.Equal(current, conflict.GetProperty("currentVersion").GetInt64());
    }

    private static async Task AssertCodeInUseAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(OrganizationProblemCodesV1.CodeInUse, body.RootElement.GetProperty("code").GetString());
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

    private static async Task<Effects> ReadEffectsAsync(SqlServerRelationalApiFactory factory, Guid company)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
        var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        Assert.True(db.Database.IsSqlServer());
        return new Effects(
            await db.Sites.CountAsync(),
            await db.Locations.CountAsync(),
            await db.Locations.SumAsync(location => location.Version),
            await db.WorkCenters.CountAsync(),
            await db.Set<AuditEvent>().CountAsync(),
            await db.CommandExecutions.CountAsync());
    }

    /// <summary>A synthetic role with exactly these permissions in one company, assigned company-wide to a development user.</summary>
    private static async Task GrantAsync(
        SqlServerRelationalApiFactory factory, string developmentUser, Guid company, IReadOnlyCollection<string> permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
            x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
        Guid actor;
        if (link is null)
        {
            actor = Guid.NewGuid();
            dbContext.Users.Add(new UserAccount(actor, "Synthetic Organization master editor", null));
            dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
        }
        else
        {
            actor = link.UserId;
        }

        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"ORG-MASTER-{roleId:N}", "Synthetic Organization master edit role", true);
        foreach (var permission in permissions)
        {
            role.AddPermission(permission);
        }

        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null,
            approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Company, company);
        dbContext.Roles.Add(role);
        dbContext.RoleAssignments.Add(assignment);
        await dbContext.SaveChangesAsync();
    }

    private static async Task WithDatabaseAsync(Func<SqlServerRelationalApiFactory, Task> test)
    {
        var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"GoymenERP_Organization_MasterData_{Guid.NewGuid():N}";
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

    private sealed record Effects(int Sites, int Locations, long LocationVersions, int WorkCenters, int Audits, int Executions);
}
