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

            // Read alone: the list and the detail (and, Sales 1.9, the S-07 dispatch list on the same read), not the create screen.
            await GrantAsync(factory, TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, AccessScopeType.Company, CompanyA);
            using (var reader = factory.CreateClientFor(TestUsers.CompanyA, CompanyA))
            {
                Assert.Equal(new[] { "sales.dispatches.list", "sales.drafts.detail", "sales.drafts.list" }, SalesOnly(await ReadAsync(reader)));
            }

            // Read + create: all three order screens; the S-08 invoice candidates still need their own name.
            await GrantAsync(factory, TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsCreate, AccessScopeType.Company, CompanyA);
            using (var maker = factory.CreateClientFor(TestUsers.CompanyA, CompanyA))
            {
                Assert.Equal(new[] { "sales.dispatches.list", "sales.drafts.create", "sales.drafts.detail", "sales.drafts.list" },
                    SalesOnly(await ReadAsync(maker)));
            }

            // + sales.invoices.read (API-31): exactly the S-08 list joins; no other screen or control.
            await GrantAsync(factory, TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.InvoicesRead, AccessScopeType.Company, CompanyA);
            using (var invoicer = factory.CreateClientFor(TestUsers.CompanyA, CompanyA))
            {
                Assert.Equal(
                    new[] { "sales.dispatches.list", "sales.drafts.create", "sales.drafts.detail", "sales.drafts.list", "sales.invoice-candidates.list" },
                    SalesOnly(await ReadAsync(invoicer)));
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

    /// <summary>
    /// UI-20 (shell contract 1.8): the S-01 request and quote screens and their controls follow exactly the request / quote
    /// names of the actor in the context company — each name alone opens only its own screens, a control needs its detail's
    /// read beside its own name, the conversion needs the quote write name AND the order create name, a grant of company A
    /// opens nothing in company B, and a site-scoped grant opens nothing (the declarations are company-wide). All grants are
    /// synthetic, isolated assignments in this test's own database; no role is seeded with these names.
    /// </summary>
    [SqlServerFact]
    public async Task Sales_pool_screens_follow_the_request_and_quote_permissions_in_the_context_company_through_the_real_provider()
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnection);
        try
        {
            using var startup = factory.CreateClient();
            using var health = await startup.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();

            using (var ungranted = factory.CreateClientFor(TestUsers.CompanyA, CompanyA))
            {
                Assert.Empty(SalesOnly(await ReadAsync(ungranted)));
            }

            // The decision name alone: the control needs the detail's read beside it, so nothing opens.
            await GrantAsync(factory, TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.RequestsApprove, AccessScopeType.Company, CompanyA);
            using (var approverOnly = factory.CreateClientFor(TestUsers.CompanyA, CompanyA))
            {
                Assert.Empty(SalesOnly(await ReadAsync(approverOnly)));
            }

            // + the request read: the list, the detail and the decision control — not the create screen, no quote, no order.
            await GrantAsync(factory, TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.RequestsRead, AccessScopeType.Company, CompanyA);
            using (var approver = factory.CreateClientFor(TestUsers.CompanyA, CompanyA))
            {
                Assert.Equal(new[] { "sales.requests.approve", "sales.requests.detail", "sales.requests.list" }, SalesOnly(await ReadAsync(approver)));
            }

            // + the request write: the create screen joins.
            await GrantAsync(factory, TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.RequestsWrite, AccessScopeType.Company, CompanyA);
            using (var requester = factory.CreateClientFor(TestUsers.CompanyA, CompanyA))
            {
                Assert.Equal(new[] { "sales.requests.approve", "sales.requests.create", "sales.requests.detail", "sales.requests.list" },
                    SalesOnly(await ReadAsync(requester)));
            }

            // A second actor: the quote write name alone opens only the quote create screen (the write control needs the read).
            await GrantAsync(factory, TestUsers.CompanyAViewer, CompanyA, SalesPermissionManifestV1.QuotesWrite, AccessScopeType.Company, CompanyA);
            using (var quoteWriter = factory.CreateClientFor(TestUsers.CompanyAViewer, CompanyA))
            {
                Assert.Equal(new[] { "sales.quotes.create" }, SalesOnly(await ReadAsync(quoteWriter)));
            }

            // + the quote read: list, detail and the revision/loss control; the conversion still needs the order create name.
            await GrantAsync(factory, TestUsers.CompanyAViewer, CompanyA, SalesPermissionManifestV1.QuotesRead, AccessScopeType.Company, CompanyA);
            using (var quoteEditor = factory.CreateClientFor(TestUsers.CompanyAViewer, CompanyA))
            {
                Assert.Equal(new[] { "sales.quotes.create", "sales.quotes.detail", "sales.quotes.list", "sales.quotes.write" },
                    SalesOnly(await ReadAsync(quoteEditor)));
            }

            // + the order create name: the conversion control and the order create screen; still no request screen.
            await GrantAsync(factory, TestUsers.CompanyAViewer, CompanyA, SalesPermissionManifestV1.DraftsCreate, AccessScopeType.Company, CompanyA);
            using (var converter = factory.CreateClientFor(TestUsers.CompanyAViewer, CompanyA))
            {
                Assert.Equal(
                    new[] { "sales.drafts.create", "sales.quotes.convert", "sales.quotes.create", "sales.quotes.detail", "sales.quotes.list", "sales.quotes.write" },
                    SalesOnly(await ReadAsync(converter)));
            }

            // The grants are company A's: the same actor, measured in company B through a non-Sales assignment, sees no pool screen.
            await GrantAsync(factory, TestUsers.CompanyA, CompanyB, ErpPermissions.OrganizationLocationsRead, AccessScopeType.Company, CompanyB);
            using (var elsewhere = factory.CreateClientFor(TestUsers.CompanyA, CompanyB))
            {
                var snapshot = await ReadAsync(elsewhere);
                Assert.Contains("organization.sites.list", snapshot.Capabilities);
                Assert.Empty(SalesOnly(snapshot));
            }

            // Company-wide is the rule: site-scoped request and quote reads open nothing.
            await GrantAsync(factory, TestUsers.CompanyB, CompanyB, ErpPermissions.OrganizationLocationsRead, AccessScopeType.Company, CompanyB);
            await GrantAsync(factory, TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.RequestsRead, AccessScopeType.Site, SyntheticSiteOfB);
            await GrantAsync(factory, TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.QuotesRead, AccessScopeType.Site, SyntheticSiteOfB);
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
