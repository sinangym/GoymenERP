using System.Net;
using System.Net.Http.Json;
using GoymenERP.Api.Endpoints;
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
/// <para>
/// The first actor's grants ACCUMULATE, which is how a person really gains them; a cumulative matrix alone
/// would however pass even if the registry ignored one side of an AND (ORG #348 P3-1). So the isolated
/// section below measures each side on its OWN actor, in its own company context, and asks the real API for
/// what the shell did not offer: a screen that is not published is a screen whose reads are refused too.
/// </para>
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

            // D1: Record stays hidden until the same persona has both its money command and the
            // caller-scoped Accounting period read. Neither is inferred from draft access.
            await GrantAsync(factory, TestUsers.CompanyA, CompanyA, FinancePermissionNames.Record, AccessScopeType.Company, CompanyA);
            await AssertFinanceAsync(factory, CompanyA,
                "finance.bank-accounts.approve", "finance.bank-accounts.list", "finance.bank-accounts.propose",
                "finance.receipts.detail", "finance.receipts.draft", "finance.receipts.list");
            await GrantAsync(factory, TestUsers.CompanyA, CompanyA, ErpPermissions.AccountingPeriodsRead, AccessScopeType.Company, CompanyA);
            await AssertFinanceAsync(factory, CompanyA,
                "finance.bank-accounts.approve", "finance.bank-accounts.list", "finance.bank-accounts.propose",
                "finance.receipts.detail", "finance.receipts.draft", "finance.receipts.list", "finance.receipts.record");

            // ---- ORG #348 P3-1: every side of an AND, alone ------------------------------------------
            // Each case is ONE actor with exactly the grants it names. The context company differs between a
            // user's two cases, so an earlier grant of that user can never carry into the next case.

            // The approval alone: it is a CONTROL of the F-04 list (its AND names the read too), so nothing opens
            // — and the reads the shell did not offer are refused by the server, each under its own name.
            await GrantAsync(factory, TestUsers.CompanyAViewer, CompanyA, ErpPermissions.FinanceBankAccountApprove,
                AccessScopeType.Company, CompanyA);
            using (var approveAlone = factory.CreateClientFor(TestUsers.CompanyAViewer, CompanyA))
            {
                Assert.Empty(FinanceOnly(await ReadAsync(approveAlone)));
                Assert.Equal(HttpStatusCode.Forbidden, (await approveAlone.GetAsync(FinanceEndpoints.BankAccountsPath)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await approveAlone.GetAsync(FinanceEndpoints.ManualReceiptsPath)).StatusCode);
            }

            await GrantAsync(factory, TestUsers.CompanyAViewer, CompanyA,
                FinanceOfficialInvoiceImportPermissionNamesV1.Approve, AccessScopeType.Company, CompanyA);
            using (var importApprove = factory.CreateClientFor(TestUsers.CompanyAViewer, CompanyA))
            {
                Assert.Equal(new[] { "finance.official-invoice-import.approve" },
                    FinanceOnly(await ReadAsync(importApprove)));
                Assert.Equal(HttpStatusCode.Forbidden,
                    (await importApprove.GetAsync(FinanceEndpoints.ManualReceiptsPath)).StatusCode);
            }

            // The draft save alone: the new-draft screen's AND names the bank-account read too, so nothing opens.
            // Saving is not reading (the persona rule): the F-02 list stays refused.
            await GrantAsync(factory, TestUsers.CompanyAViewer, CompanyB, FinancePermissionNames.DraftSave,
                AccessScopeType.Company, CompanyB);
            using (var saveAlone = factory.CreateClientFor(TestUsers.CompanyAViewer, CompanyB))
            {
                Assert.Empty(FinanceOnly(await ReadAsync(saveAlone)));
                Assert.Equal(HttpStatusCode.Forbidden, (await saveAlone.GetAsync(FinanceEndpoints.ManualReceiptsPath)).StatusCode);
            }

            await GrantAsync(factory, TestUsers.CompanyAViewer, CompanyB,
                FinanceOfficialInvoiceImportPermissionNamesV1.Upload, AccessScopeType.Company, CompanyB);
            using (var importUpload = factory.CreateClientFor(TestUsers.CompanyAViewer, CompanyB))
            {
                Assert.Equal(new[] { "finance.official-invoice-import.upload" },
                    FinanceOnly(await ReadAsync(importUpload)));
                Assert.Equal(HttpStatusCode.Forbidden,
                    (await importUpload.GetAsync(FinanceEndpoints.ManualReceiptsPath)).StatusCode);
            }

            // The propose alone: the F-04 propose screen is its own single name, so it DOES open — and it opens
            // nothing else: proposing does not imply reading the bank accounts.
            await GrantAsync(factory, TestUsers.SiteAuditReader, CompanyA, ErpPermissions.FinanceBankAccountPropose,
                AccessScopeType.Company, CompanyA);
            using (var proposeAlone = factory.CreateClientFor(TestUsers.SiteAuditReader, CompanyA))
            {
                Assert.Equal(new[] { "finance.bank-accounts.propose" }, FinanceOnly(await ReadAsync(proposeAlone)));
                Assert.Equal(HttpStatusCode.Forbidden, (await proposeAlone.GetAsync(FinanceEndpoints.BankAccountsPath)).StatusCode);
            }

            // The bank-account read alone: the F-04 list, and NOT the approval control — the other side of the
            // same AND. The list's own read answers, the F-02 list does not.
            await GrantAsync(factory, TestUsers.SiteAuditReader, CompanyB, ErpPermissions.FinanceBankAccountRead,
                AccessScopeType.Company, CompanyB);
            using (var readAlone = factory.CreateClientFor(TestUsers.SiteAuditReader, CompanyB))
            {
                Assert.Equal(new[] { "finance.bank-accounts.list" }, FinanceOnly(await ReadAsync(readAlone)));
                Assert.Equal(HttpStatusCode.OK, (await readAlone.GetAsync(FinanceEndpoints.BankAccountsPath)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await readAlone.GetAsync(FinanceEndpoints.ManualReceiptsPath)).StatusCode);
            }

            // Both sides of the new-draft AND, without the draft read: the new-draft screen and the F-04 list
            // open; the F-02 list does not, because reading drafts is a third name.
            await GrantAsync(factory, TestUsers.NoAssignment, CompanyA, FinancePermissionNames.DraftSave,
                AccessScopeType.Company, CompanyA);
            await GrantAsync(factory, TestUsers.NoAssignment, CompanyA, ErpPermissions.FinanceBankAccountRead,
                AccessScopeType.Company, CompanyA);
            using (var saveAndRead = factory.CreateClientFor(TestUsers.NoAssignment, CompanyA))
            {
                Assert.Equal(new[] { "finance.bank-accounts.list", "finance.receipts.draft" },
                    FinanceOnly(await ReadAsync(saveAndRead)));
                Assert.Equal(HttpStatusCode.Forbidden, (await saveAndRead.GetAsync(FinanceEndpoints.ManualReceiptsPath)).StatusCode);
            }

            // Customer-instrument reading is an independent company-wide list. It neither implies receipt access
            // nor borrows that permission, and the actual list endpoint accepts the same actor/company boundary.
            await GrantAsync(factory, TestUsers.NoAssignment, CompanyB, ErpPermissions.FinanceCustomerInstrumentRead,
                AccessScopeType.Company, CompanyB);
            using (var customerInstrumentRead = factory.CreateClientFor(TestUsers.NoAssignment, CompanyB))
            {
                Assert.Equal(new[] { "finance.customer-instruments.list" },
                    FinanceOnly(await ReadAsync(customerInstrumentRead)));
                Assert.Equal(HttpStatusCode.OK,
                    (await customerInstrumentRead.GetAsync(FinanceEndpoints.CustomerInstrumentsPath)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden,
                    (await customerInstrumentRead.GetAsync(FinanceEndpoints.ManualReceiptsPath)).StatusCode);
            }

            // FI04 metadata read opens both list and detail routes. The separate amount name is endpoint
            // disclosure only: by itself it opens no shell route and cannot imply metadata access.
            const string openItemReader = "finance-open-items-reader";
            await GrantAsync(factory, openItemReader, CompanyA, FinancePermissionNames.OpenItemRead,
                AccessScopeType.Company, CompanyA);
            using (var openItems = factory.CreateClientFor(openItemReader, CompanyA))
            {
                Assert.Equal(new[] { "finance.open-items.detail", "finance.open-items.list" },
                    FinanceOnly(await ReadAsync(openItems)));
                Assert.Equal(HttpStatusCode.OK,
                    (await openItems.GetAsync(FinanceOpenItemReadEndpoints.Path)).StatusCode);
            }

            const string amountOnlyReader = "finance-open-items-amount-only";
            await GrantAsync(factory, amountOnlyReader, CompanyA, FinancePermissionNames.OpenItemAmountRead,
                AccessScopeType.Company, CompanyA);
            using (var amountOnly = factory.CreateClientFor(amountOnlyReader, CompanyA))
            {
                Assert.Empty(FinanceOnly(await ReadAsync(amountOnly)));
                Assert.Equal(HttpStatusCode.Forbidden,
                    (await amountOnly.GetAsync(FinanceOpenItemReadEndpoints.Path)).StatusCode);
            }

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
            await GrantAsync(factory, TestUsers.CompanyB, CompanyB, ErpPermissions.FinanceCustomerInstrumentRead, AccessScopeType.Site, SyntheticSiteOfB);
            await GrantAsync(factory, TestUsers.CompanyB, CompanyB,
                FinanceOfficialInvoiceImportPermissionNamesV1.Upload, AccessScopeType.Site, SyntheticSiteOfB);
            await GrantAsync(factory, TestUsers.CompanyB, CompanyB,
                FinanceOfficialInvoiceImportPermissionNamesV1.Approve, AccessScopeType.Site, SyntheticSiteOfB);
            await GrantAsync(factory, TestUsers.CompanyB, CompanyB,
                FinancePermissionNames.OpenItemRead, AccessScopeType.Site, SyntheticSiteOfB);
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
