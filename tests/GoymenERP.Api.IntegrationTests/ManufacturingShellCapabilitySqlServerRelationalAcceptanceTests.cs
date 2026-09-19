using System.Net.Http.Json;
using GoymenERP.Api.Security;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Manufacturing's published shell capability provider through the real host on a real SQL Server
/// (MFG-UI-F, the S03-c pattern): the order screen and its fact-form control follow the Manufacturing
/// permissions of the actor in the context company. The grants are synthetic, isolated role assignments in
/// this test's own database; they are not a Sinan or production grant and are never seeded.
/// <list type="bullet">
/// <item>no grant opens nothing;</item>
/// <item><c>facts.record</c> alone opens nothing: the control needs its parent's read;</item>
/// <item>read + record opens both; read alone opens the order screen, not the forms;</item>
/// <item>the grant of company A opens nothing in company B for the same actor (who holds a
/// non-Manufacturing assignment there, so the context resolves and the answer is measured, not a 403);</item>
/// <item>a site-scoped grant opens nothing (the declarations are company-wide, K-1), and a site-scoped
/// record beside a company-wide read still opens only the order screen.</item>
/// </list>
/// </summary>
public sealed class ManufacturingShellCapabilitySqlServerRelationalAcceptanceTests
{
    private const string Detail = "manufacturing.orders.detail";
    private const string Facts = "manufacturing.orders.facts";

    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid SyntheticSiteOfB = Guid.Parse("5e11e000-0000-4000-8000-0000000000b2");

    [SqlServerFact]
    public async Task The_order_screen_and_its_fact_forms_follow_the_actors_Manufacturing_permissions_in_the_context_company()
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnection);
        try
        {
            using var startup = factory.CreateClient();
            using var health = await startup.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();

            // No Manufacturing grant: the published declarations open nothing.
            using (var ungranted = factory.CreateClientFor(TestUsers.CompanyA, CompanyA))
            {
                Assert.Empty(ManufacturingOnly(await ReadAsync(ungranted)));
            }

            // The write permission alone: nothing — the forms are a control of the order screen and keep its read.
            await GrantAsync(factory, TestUsers.CompanyA, CompanyA, ErpPermissions.ManufacturingFactsRecord, AccessScopeType.Company, CompanyA);
            using (var recorderOnly = factory.CreateClientFor(TestUsers.CompanyA, CompanyA))
            {
                Assert.Empty(ManufacturingOnly(await ReadAsync(recorderOnly)));
            }

            // + read: the order screen and, with the record permission already held, its forms.
            await GrantAsync(factory, TestUsers.CompanyA, CompanyA, ErpPermissions.ManufacturingOrdersRead, AccessScopeType.Company, CompanyA);
            using (var recorder = factory.CreateClientFor(TestUsers.CompanyA, CompanyA))
            {
                Assert.Equal(new[] { Detail, Facts }, ManufacturingOnly(await ReadAsync(recorder)));
            }

            // The grants are company A's. The same actor gets a non-Manufacturing assignment in company B, so the
            // company-B context resolves and the answer is measured: no Manufacturing screen there.
            await GrantAsync(factory, TestUsers.CompanyA, CompanyB, ErpPermissions.OrganizationLocationsRead, AccessScopeType.Company, CompanyB);
            using (var elsewhere = factory.CreateClientFor(TestUsers.CompanyA, CompanyB))
            {
                var snapshot = await ReadAsync(elsewhere);
                Assert.Contains("organization.sites.list", snapshot.Capabilities);
                Assert.Empty(ManufacturingOnly(snapshot));
            }

            // Company-wide is the rule (K-1): site-scoped read + record open nothing. The actor also holds a
            // company-scoped non-Manufacturing assignment, so the context resolves and the answer is measured.
            await GrantAsync(factory, TestUsers.CompanyB, CompanyB, ErpPermissions.OrganizationLocationsRead, AccessScopeType.Company, CompanyB);
            await GrantAsync(factory, TestUsers.CompanyB, CompanyB, ErpPermissions.ManufacturingOrdersRead, AccessScopeType.Site, SyntheticSiteOfB);
            await GrantAsync(factory, TestUsers.CompanyB, CompanyB, ErpPermissions.ManufacturingFactsRecord, AccessScopeType.Site, SyntheticSiteOfB);
            using (var siteScoped = factory.CreateClientFor(TestUsers.CompanyB, CompanyB))
            {
                var snapshot = await ReadAsync(siteScoped);
                Assert.Contains("organization.sites.list", snapshot.Capabilities);
                Assert.Empty(ManufacturingOnly(snapshot));
            }

            // + a company-wide read: the order screen only. Read alone never opens the forms, and the record
            // permission this actor holds is site-scoped, so it does not count.
            await GrantAsync(factory, TestUsers.CompanyB, CompanyB, ErpPermissions.ManufacturingOrdersRead, AccessScopeType.Company, CompanyB);
            using (var reader = factory.CreateClientFor(TestUsers.CompanyB, CompanyB))
            {
                Assert.Equal(new[] { Detail }, ManufacturingOnly(await ReadAsync(reader)));
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

    private static string[] ManufacturingOnly(ShellCapabilitySnapshot snapshot) =>
        [.. snapshot.Capabilities.Where(capability => capability.StartsWith("manufacturing.", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

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
            dbContext.Users.Add(new UserAccount(actor, "Synthetic Manufacturing shell user", null));
            dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
        }
        else
        {
            actor = link.UserId;
        }

        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"MFG-SHELL-{roleId:N}", "Synthetic Manufacturing shell test role", true);
        role.AddPermission(permission);
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null,
            approvedByUserId: null);
        assignment.AddScope(scopeType, scopeId);
        dbContext.Roles.Add(role);
        dbContext.RoleAssignments.Add(assignment);
        await dbContext.SaveChangesAsync();
    }
}
