using System.Net.Http.Json;
using GoymenERP.Api.Security;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Sales's published shell capability provider through the real host on a real SQL Server (S03-c):
/// the three draft screens follow the Sales permissions of the actor in the context company. The
/// grants are synthetic, isolated role assignments in this test's own database; they are not a Sinan
/// or production grant and are never seeded. Read alone opens the list and the detail; read + create
/// opens all three; no grant opens nothing; the grant of company A opens nothing in company B for the
/// same actor (who holds a non-Sales assignment there, so the context resolves and the answer is
/// measured, not a 403); a site-scoped read grant opens nothing (the declarations are company-wide).
/// </summary>
public sealed class SalesShellCapabilitySqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid SyntheticSiteOfB = Guid.Parse("5e11e000-0000-4000-8000-0000000000b1");

    [SqlServerFact]
    public async Task Sales_screens_follow_the_actors_Sales_permissions_in_the_context_company_through_the_real_provider()
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnection);
        try
        {
            using var startup = factory.CreateClient();
            using var health = await startup.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();

            // No Sales grant: the published declarations open nothing.
            using (var ungranted = factory.CreateClientFor(TestUsers.CompanyA, CompanyA))
            {
                Assert.Empty(SalesOnly(await ReadAsync(ungranted)));
            }

            // Read alone: the list and the detail, not the create screen.
            await GrantAsync(factory, TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, AccessScopeType.Company, CompanyA);
            using (var reader = factory.CreateClientFor(TestUsers.CompanyA, CompanyA))
            {
                Assert.Equal(new[] { "sales.drafts.detail", "sales.drafts.list" }, SalesOnly(await ReadAsync(reader)));
            }

            // Read + create: all three.
            await GrantAsync(factory, TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsCreate, AccessScopeType.Company, CompanyA);
            using (var maker = factory.CreateClientFor(TestUsers.CompanyA, CompanyA))
            {
                Assert.Equal(new[] { "sales.drafts.create", "sales.drafts.detail", "sales.drafts.list" }, SalesOnly(await ReadAsync(maker)));
            }

            // The grant is company A's. The same actor gets a non-Sales assignment in company B, so the
            // company-B context resolves and the answer is measured (ORG #328 P2-1): no Sales screen there.
            await GrantAsync(factory, TestUsers.CompanyA, CompanyB, ErpPermissions.OrganizationLocationsRead, AccessScopeType.Company, CompanyB);
            using (var elsewhere = factory.CreateClientFor(TestUsers.CompanyA, CompanyB))
            {
                var snapshot = await ReadAsync(elsewhere);
                Assert.Contains("organization.sites.list", snapshot.Capabilities);
                Assert.Empty(SalesOnly(snapshot));
            }

            // Company-wide is the rule (ORG #328 P3-1): a site-scoped read grant opens no Sales screen. The
            // actor also holds a company-scoped non-Sales assignment, so the context resolves and the
            // answer is measured.
            await GrantAsync(factory, TestUsers.CompanyB, CompanyB, ErpPermissions.OrganizationLocationsRead, AccessScopeType.Company, CompanyB);
            await GrantAsync(factory, TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.DraftsRead, AccessScopeType.Site, SyntheticSiteOfB);
            using (var siteScoped = factory.CreateClientFor(TestUsers.CompanyB, CompanyB))
            {
                var snapshot = await ReadAsync(siteScoped);
                Assert.Contains("organization.sites.list", snapshot.Capabilities);
                Assert.Empty(SalesOnly(snapshot));
            }
        }
        finally
        {
            await factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnection);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{factory.DatabaseName}];
                END
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<ShellCapabilitySnapshot> ReadAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ShellCapabilitySnapshot>("/api/v1/shell/capabilities"))!;

    private static string[] SalesOnly(ShellCapabilitySnapshot snapshot) =>
        [.. snapshot.Capabilities.Where(capability => capability.StartsWith("sales.", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    /// <summary>A synthetic role with exactly this permission in one company, assigned to a configured development user under one scope.</summary>
    private static async Task GrantAsync(SqlServerRelationalApiFactory factory, string developmentUser, Guid company, string permission,
        AccessScopeType scopeType, Guid scopeId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
            x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
        Guid actor;
        if (link is null)
        {
            actor = Guid.NewGuid();
            dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales shell user", null));
            dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
        }
        else
        {
            actor = link.UserId;
        }

        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"SALES-SHELL-{roleId:N}", "Synthetic Sales shell test role", true);
        role.AddPermission(permission);
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null,
            approvedByUserId: null);
        assignment.AddScope(scopeType, scopeId);
        dbContext.Roles.Add(role);
        dbContext.RoleAssignments.Add(assignment);
        await dbContext.SaveChangesAsync();
    }
}
