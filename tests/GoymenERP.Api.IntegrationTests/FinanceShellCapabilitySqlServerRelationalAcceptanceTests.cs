using System.Net.Http.Json;
using GoymenERP.Api.Security;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Finance's PUBLISHED shell capability provider through the real host on a real SQL Server (FIN-UI-1c,
/// the S03-c pattern): the F-02 and F-04 screens and the approval control follow the Finance permissions
/// of the actor in the context company, each read its own narrow name (Sinan's persona rule). The grants
/// are synthetic, isolated role assignments in this test's own database; they are not a Sinan or
/// production grant and are never seeded. No grant opens nothing; the grant of company A opens nothing in
/// company B for the same actor (who holds a non-Finance assignment there, so the answer is measured, not a
/// 403); a site-scoped grant opens nothing (the declarations are company-wide).
/// </summary>
public sealed class FinanceShellCapabilitySqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid SyntheticSiteOfB = Guid.Parse("f1e11000-0000-4000-8000-0000000000b1");

    [SqlServerFact]
    public async Task Finance_screens_follow_the_actors_Finance_permissions_in_the_context_company_through_the_real_provider()
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnection);
        try
        {
            using var startup = factory.CreateClient();
            using var health = await startup.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();

            // No Finance grant: the published declarations open nothing.
            using (var ungranted = factory.CreateClientFor(TestUsers.CompanyA, CompanyA))
            {
                Assert.Empty(FinanceOnly(await ReadAsync(ungranted)));
            }

            // The draft read alone: the F-02 list and detail, not the new draft, and no bank-account screen.
            await GrantAsync(factory, TestUsers.CompanyA, CompanyA, FinancePermissionNames.DraftRead, AccessScopeType.Company, CompanyA);
            await AssertFinanceAsync(factory, CompanyA, "finance.receipts.detail", "finance.receipts.list");

            // The bank-account read: the F-04 list, still no new draft (it needs the save name too).
            await GrantAsync(factory, TestUsers.CompanyA, CompanyA, ErpPermissions.FinanceBankAccountRead, AccessScopeType.Company, CompanyA);
            await AssertFinanceAsync(factory, CompanyA,
                "finance.bank-accounts.list", "finance.receipts.detail", "finance.receipts.list");

            // + the draft save: the new-draft screen.
            await GrantAsync(factory, TestUsers.CompanyA, CompanyA, FinancePermissionNames.DraftSave, AccessScopeType.Company, CompanyA);
            await AssertFinanceAsync(factory, CompanyA,
                "finance.bank-accounts.list", "finance.receipts.detail", "finance.receipts.draft", "finance.receipts.list");

            // + propose, + approve: the F-04 propose screen and the approval control (Ö4 is still the server's).
            await GrantAsync(factory, TestUsers.CompanyA, CompanyA, ErpPermissions.FinanceBankAccountPropose, AccessScopeType.Company, CompanyA);
            await GrantAsync(factory, TestUsers.CompanyA, CompanyA, ErpPermissions.FinanceBankAccountApprove, AccessScopeType.Company, CompanyA);
            await AssertFinanceAsync(factory, CompanyA,
                "finance.bank-accounts.approve", "finance.bank-accounts.list", "finance.bank-accounts.propose",
                "finance.receipts.detail", "finance.receipts.draft", "finance.receipts.list");

            // The grants are company A's. The same actor gets a non-Finance assignment in company B, so the
            // company-B context resolves and the answer is measured: no Finance screen there.
            await GrantAsync(factory, TestUsers.CompanyA, CompanyB, ErpPermissions.OrganizationLocationsRead, AccessScopeType.Company, CompanyB);
            using (var elsewhere = factory.CreateClientFor(TestUsers.CompanyA, CompanyB))
            {
                var snapshot = await ReadAsync(elsewhere);
                Assert.Contains("organization.sites.list", snapshot.Capabilities);
                Assert.Empty(FinanceOnly(snapshot));
            }

            // Company-wide is the rule: a site-scoped Finance read opens no Finance screen.
            await GrantAsync(factory, TestUsers.CompanyB, CompanyB, ErpPermissions.OrganizationLocationsRead, AccessScopeType.Company, CompanyB);
            await GrantAsync(factory, TestUsers.CompanyB, CompanyB, FinancePermissionNames.DraftRead, AccessScopeType.Site, SyntheticSiteOfB);
            await GrantAsync(factory, TestUsers.CompanyB, CompanyB, ErpPermissions.FinanceBankAccountRead, AccessScopeType.Site, SyntheticSiteOfB);
            using (var siteScoped = factory.CreateClientFor(TestUsers.CompanyB, CompanyB))
            {
                var snapshot = await ReadAsync(siteScoped);
                Assert.Contains("organization.sites.list", snapshot.Capabilities);
                Assert.Empty(FinanceOnly(snapshot));
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

    private static async Task AssertFinanceAsync(SqlServerRelationalApiFactory factory, Guid company, params string[] expected)
    {
        using var client = factory.CreateClientFor(TestUsers.CompanyA, company);
        Assert.Equal(expected, FinanceOnly(await ReadAsync(client)));
    }

    private static async Task<ShellCapabilitySnapshot> ReadAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ShellCapabilitySnapshot>("/api/v1/shell/capabilities"))!;

    private static string[] FinanceOnly(ShellCapabilitySnapshot snapshot) =>
        [.. snapshot.Capabilities.Where(capability => capability.StartsWith("finance.", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

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
            dbContext.Users.Add(new UserAccount(actor, "Synthetic Finance shell user", null));
            dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
        }
        else
        {
            actor = link.UserId;
        }

        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"FIN-SHELL-{roleId:N}", "Synthetic Finance shell test role", true);
        role.AddPermission(permission);
        var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null,
            approvedByUserId: null);
        assignment.AddScope(scopeType, scopeId);
        dbContext.Roles.Add(role);
        dbContext.RoleAssignments.Add(assignment);
        await dbContext.SaveChangesAsync();
    }
}
