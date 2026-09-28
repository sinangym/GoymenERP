using System.Net;
using System.Text.Json;
using GoymenERP.Api.Contracts;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AT-004 read-only access view ("Kullanıcılar ve yetkiler") through the real host on a real SQL Server. Two
/// administrators hold the SAME read, company-wide, in different companies, so company is the only variable that
/// can explain what each one sees: the fence is measured, not the permission. Every body is parsed as JSON, and the
/// whole text is also searched for what must never leave the module (e-mail, issuer, external subject).
/// <para>
/// The grants are synthetic, isolated role assignments in this test's own database; they are not a Sinan or
/// production grant and are never seeded. Runs only once the central catalogue registers
/// <c>identity.assignments.read</c> (the policy is registered from the catalogue).
/// </para>
/// </summary>
public sealed class IdentityAccessViewSqlServerRelationalAcceptanceTests
{
    private const string UsersRoute = "/api/v1/identity/access/users";
    private const string RolesRoute = "/api/v1/identity/access/roles";

    // The built-in development seed (Seed/identity-seed.json): only ids, never a subject string.
    private static readonly Guid CompanyAUser = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid CompanyBUser = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid GroupAdmin = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid OrganizationOperatorRole = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid CompanyAOperatorAssignment = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid GroupAdminCompanyAAssignment = Guid.Parse("40000000-0000-0000-0000-000000000003");
    private static readonly Guid GroupAdminCompanyBAssignment = Guid.Parse("40000000-0000-0000-0000-000000000004");
    private static readonly Guid GroupAdminCompanyCAssignment = Guid.Parse("40000000-0000-0000-0000-000000000007");

    private static readonly string[] ForbiddenPropertyNames =
        ["email", "issuer", "subject", "externalIdentities", "externalIdentity"];

    [SqlServerFact]
    public async Task Each_administrator_sees_only_the_assignments_of_the_company_they_hold_the_read_in()
    {
        await WithDatabaseAsync(async factory =>
        {
            await GrantAsync(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            await GrantAsync(factory, TestUsers.Unprovisioned, TestCompanyIds.CompanyB);
            var baseline = await ReadEffectsAsync(factory);

            using var adminA = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            using var adminB = CreateClient(factory, TestUsers.Unprovisioned, TestCompanyIds.CompanyB);

            var groupAdminListedCount = -1;

            // Users list: A's holder is there, B's is not; the group admin is listed with its A assignment only.
            using (var body = await ReadJsonAsync(adminA, UsersRoute))
            {
                var users = body.RootElement.EnumerateArray().ToArray();
                Assert.All(users, user => Assert.Equal(
                    ["assignmentCount", "displayName", "effectiveAssignmentCount", "isActive", "userId"],
                    user.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)));
                var ids = users.Select(user => user.GetProperty("userId").GetGuid()).ToArray();
                Assert.Contains(CompanyAUser, ids);
                Assert.Contains(GroupAdmin, ids);
                Assert.DoesNotContain(CompanyBUser, ids);
                groupAdminListedCount = users.Single(user => user.GetProperty("userId").GetGuid() == GroupAdmin)
                    .GetProperty("assignmentCount").GetInt32();
            }

            using (var body = await ReadJsonAsync(adminB, UsersRoute))
            {
                var ids = body.RootElement.EnumerateArray().Select(user => user.GetProperty("userId").GetGuid()).ToArray();
                Assert.Contains(CompanyBUser, ids);
                Assert.DoesNotContain(CompanyAUser, ids);
            }

            // One user: every assignment and scope is company A's, the approver is named, nothing external leaks.
            using (var body = await ReadJsonAsync(adminA, $"{UsersRoute}/{CompanyAUser}"))
            {
                var root = body.RootElement;
                Assert.Equal(CompanyAUser, root.GetProperty("userId").GetGuid());
                var assignment = root.GetProperty("assignments").EnumerateArray()
                    .Single(candidate => candidate.GetProperty("assignmentId").GetGuid() == CompanyAOperatorAssignment);
                Assert.Equal(OrganizationOperatorRole, assignment.GetProperty("roleId").GetGuid());
                Assert.Equal("ORGANIZATION_OPERATOR", assignment.GetProperty("roleCode").GetString());
                Assert.Equal(IdentityAccessValidity.Effective, assignment.GetProperty("validity").GetString());
                Assert.Equal(JsonValueKind.Null, assignment.GetProperty("validUntilUtc").ValueKind);
                Assert.Equal(GroupAdmin, assignment.GetProperty("approvedBy").GetProperty("userId").GetGuid());
                var scope = Assert.Single(assignment.GetProperty("scopes").EnumerateArray().ToArray());
                Assert.Equal("Company", scope.GetProperty("type").GetString());
                Assert.Equal(TestCompanyIds.CompanyA, scope.GetProperty("id").GetGuid());
                var permissions = root.GetProperty("effectivePermissions").EnumerateArray()
                    .Select(permission => permission.GetProperty("code").GetString())
                    .ToArray();
                Assert.Contains(ErpPermissions.OrganizationLocationsStatusUpdate, permissions);
            }

            using (var body = await ReadJsonAsync(adminA, $"{UsersRoute}/{GroupAdmin}"))
            {
                Assert.All(body.RootElement.GetProperty("assignments").EnumerateArray(), assignment =>
                    Assert.All(assignment.GetProperty("scopes").EnumerateArray(), scope =>
                        Assert.Equal(TestCompanyIds.CompanyA, scope.GetProperty("id").GetGuid())));
                // The seeded B and C assignments of the same person are not here, and the list counted exactly these.
                var assignmentIds = body.RootElement.GetProperty("assignments").EnumerateArray()
                    .Select(assignment => assignment.GetProperty("assignmentId").GetGuid()).ToArray();
                Assert.Contains(GroupAdminCompanyAAssignment, assignmentIds);
                Assert.DoesNotContain(GroupAdminCompanyBAssignment, assignmentIds);
                Assert.DoesNotContain(GroupAdminCompanyCAssignment, assignmentIds);
                Assert.Equal(groupAdminListedCount, assignmentIds.Length);
            }

            // Another company's user and an unknown id are one code-less 404, never a 403 that confirms the user.
            await AssertCodeLessNotFoundAsync(adminA, $"{UsersRoute}/{CompanyBUser}", CompanyBUser);
            await AssertCodeLessNotFoundAsync(adminB, $"{UsersRoute}/{CompanyAUser}", CompanyAUser);
            var unknown = Guid.NewGuid();
            await AssertCodeLessNotFoundAsync(adminA, $"{UsersRoute}/{unknown}", unknown);

            var operatorHoldersInA = -1;

            // One role: the same group-wide definition, but each administrator sees only their company's holders.
            using (var body = await ReadJsonAsync(adminA, $"{RolesRoute}/{OrganizationOperatorRole}"))
            {
                var holders = body.RootElement.GetProperty("holders").EnumerateArray()
                    .Select(holder => holder.GetProperty("userId").GetGuid()).ToArray();
                Assert.Contains(CompanyAUser, holders);
                Assert.DoesNotContain(CompanyBUser, holders);
                operatorHoldersInA = holders.Distinct().Count();
                Assert.Contains(body.RootElement.GetProperty("permissions").EnumerateArray(), permission =>
                    permission.GetProperty("code").GetString() == ErpPermissions.OrganizationLocationsStatusUpdate);
            }

            using (var body = await ReadJsonAsync(adminB, $"{RolesRoute}/{OrganizationOperatorRole}"))
            {
                var holders = body.RootElement.GetProperty("holders").EnumerateArray()
                    .Select(holder => holder.GetProperty("userId").GetGuid()).ToArray();
                Assert.Contains(CompanyBUser, holders);
                Assert.DoesNotContain(CompanyAUser, holders);
            }

            using (var body = await ReadJsonAsync(adminA, RolesRoute))
            {
                var role = body.RootElement.EnumerateArray()
                    .Single(candidate => candidate.GetProperty("roleId").GetGuid() == OrganizationOperatorRole);
                // The list counts the same holders the card shows, in this company only.
                Assert.Equal(operatorHoldersInA, role.GetProperty("holderCount").GetInt32());
            }

            var unknownRole = Guid.NewGuid();
            await AssertCodeLessNotFoundAsync(adminA, $"{RolesRoute}/{unknownRole}", unknownRole);

            // Holding the read in B does not open A: naming A as the context is refused before anything is read.
            using var bNamingA = CreateClient(factory, TestUsers.Unprovisioned, TestCompanyIds.CompanyA);
            foreach (var route in AllRoutes())
            {
                using var response = await bNamingA.GetAsync(route);
                await AssertPlatformErrorAsync(response, HttpStatusCode.Forbidden, "forbidden");
            }

            // A read: no Identity row, audit event or command record changed.
            Assert.Equal(baseline, await ReadEffectsAsync(factory));
        });
    }

    [SqlServerFact]
    public async Task Nothing_is_disclosed_without_the_company_wide_read_and_nothing_external_ever_leaves()
    {
        await WithDatabaseAsync(async factory =>
        {
            // The name is registered company-wide only, so a site-scoped holder is refused by the policy itself.
            Assert.True(ErpPermissions.IsKnown(IdentityPermissionManifestV1.AssignmentsRead));
            Assert.True(ErpPermissions.RequiresCompanyWideScope(IdentityPermissionManifestV1.AssignmentsRead));
            await GrantAsync(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA,
                AccessScopeType.Site, Guid.NewGuid());
            using (var siteScoped = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA))
            {
                foreach (var route in AllRoutes())
                {
                    using var response = await siteScoped.GetAsync(route);
                    await AssertPlatformErrorAsync(response, HttpStatusCode.Forbidden, "forbidden");
                }
            }

            // A company user without the read is refused on every route. So is the seeded group administrator:
            // SYSTEM_ADMINISTRATOR carries identity.roles.manage and identity.audit.read, and neither implies this
            // read. No seeded role carries it — granting it is a human gate, not a seed default.
            foreach (var actor in new[] { TestUsers.CompanyA, TestUsers.GroupAdmin })
            {
                using var client = CreateClient(factory, actor, TestCompanyIds.CompanyA);
                foreach (var route in AllRoutes())
                {
                    using var response = await client.GetAsync(route);
                    await AssertPlatformErrorAsync(response, HttpStatusCode.Forbidden, "forbidden");
                }
            }

            using (var anonymous = factory.CreateClient())
            {
                using var response = await anonymous.GetAsync(UsersRoute);
                await AssertPlatformErrorAsync(response, HttpStatusCode.Unauthorized, "authentication_required");
            }

            await GrantAsync(factory, TestUsers.Unprovisioned, TestCompanyIds.CompanyA);
            using (var noContext = factory.CreateClient())
            {
                noContext.DefaultRequestHeaders.Add("X-Development-User", TestUsers.Unprovisioned);
                using var response = await noContext.GetAsync(UsersRoute);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            }

            // Every successful body, parsed: no e-mail, issuer or external subject anywhere, and never cached.
            using var admin = CreateClient(factory, TestUsers.Unprovisioned, TestCompanyIds.CompanyA);
            string[] readable =
            [
                UsersRoute,
                $"{UsersRoute}/{CompanyAUser}",
                $"{UsersRoute}/{GroupAdmin}",
                RolesRoute,
                $"{RolesRoute}/{OrganizationOperatorRole}",
            ];
            foreach (var route in readable)
            {
                using var response = await admin.GetAsync(route);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty,
                    StringComparison.Ordinal);
                var text = await response.Content.ReadAsStringAsync();
                using var body = JsonDocument.Parse(text);
                AssertNoForbiddenProperty(body.RootElement, route);
                Assert.DoesNotContain(BootstrapIdentityOptions.Issuer, text, StringComparison.Ordinal);
                Assert.DoesNotContain(TestUsers.CompanyA, text, StringComparison.Ordinal);
                Assert.DoesNotContain(TestUsers.GroupAdmin, text, StringComparison.Ordinal);
                Assert.DoesNotContain("@", text, StringComparison.Ordinal);
            }
        });
    }

    private static IEnumerable<string> AllRoutes() =>
    [
        UsersRoute,
        $"{UsersRoute}/{CompanyAUser}",
        RolesRoute,
        $"{RolesRoute}/{OrganizationOperatorRole}",
    ];

    private static void AssertNoForbiddenProperty(JsonElement element, string route)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Assert.False(
                        ForbiddenPropertyNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase),
                        $"{route} carries '{property.Name}'");
                    AssertNoForbiddenProperty(property.Value, route);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AssertNoForbiddenProperty(item, route);
                }

                break;
        }
    }

    private static HttpClient CreateClient(SqlServerRelationalApiFactory factory, string user, Guid company)
    {
        var client = factory.CreateClientFor(user, company);
        // The shared factory's Cari handshake is not an Identity contract.
        client.DefaultRequestHeaders.Remove(CounterpartyClientContract.HeaderName);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        return client;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task AssertCodeLessNotFoundAsync(HttpClient client, string route, Guid id)
    {
        using var response = await client.GetAsync(route);
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

    private static async Task<Effects> ReadEffectsAsync(SqlServerRelationalApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True(identity.Database.IsSqlServer());
        var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return new Effects(
            await identity.Users.CountAsync(),
            await identity.Roles.CountAsync(),
            await identity.RolePermissions.CountAsync(),
            await identity.RoleAssignments.CountAsync(),
            await identity.RoleAssignments.SumAsync(assignment => assignment.Version),
            await identity.AccessGrantRequests.CountAsync(),
            await audit.AuditEvents.IgnoreQueryFilters().CountAsync());
    }

    /// <summary>A synthetic role holding only the read, assigned to a configured development user in one company.</summary>
    private static async Task GrantAsync(
        SqlServerRelationalApiFactory factory,
        string developmentUser,
        Guid company,
        AccessScopeType scopeType = AccessScopeType.Company,
        Guid? scopeId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
            x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
        Guid actor;
        if (link is null)
        {
            actor = Guid.NewGuid();
            dbContext.Users.Add(new UserAccount(actor, "Synthetic Identity access-view administrator", null));
            dbContext.ExternalIdentities.Add(
                new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
        }
        else
        {
            actor = link.UserId;
        }

        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"IDENTITY-ACCESS-VIEW-{roleId:N}", "Synthetic Identity access-view role", true);
        role.AddPermission(IdentityPermissionManifestV1.AssignmentsRead);
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company,
            DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
        // Company-wide by default; a narrower grant carries ONLY its own scope, never the company one beside it.
        assignment.AddScope(scopeType, scopeType == AccessScopeType.Company ? company : scopeId ?? Guid.NewGuid());
        dbContext.Roles.Add(role);
        dbContext.RoleAssignments.Add(assignment);
        await dbContext.SaveChangesAsync();
    }

    private static async Task WithDatabaseAsync(Func<SqlServerRelationalApiFactory, Task> test)
    {
        var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"GoymenERP_Identity_AccessView_{Guid.NewGuid():N}";
        await using var factory = new SqlServerRelationalApiFactory(connection,
            applyTestCompanyBTopologyTemplates: false, databaseName: databaseName);
        await using var cleanupScope = factory.Services.CreateAsyncScope();
        var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
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
        int Users,
        int Roles,
        int RolePermissions,
        int Assignments,
        long AssignmentVersions,
        int GrantRequests,
        int Audits);
}
