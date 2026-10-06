using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Counterparty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Counterparty.UnitTests;

public sealed class CounterpartyMigrationScriptTests
{
    [Fact]
    public void Migrations_GenerateIdempotentSqlWithOwnedWriteSupportOnly()
    {
        var options = new DbContextOptionsBuilder<CounterpartyDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=GoymenErpCounterpartyScript;Trusted_Connection=True;")
            .Options;
        using var dbContext = new CounterpartyDbContext(options, new UnresolvedCompanyContext());
        var migrator = dbContext.GetService<IMigrator>();

        var script = migrator.GenerateScript(
            options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Contains("20260828123500_InitialCounterparty", script, StringComparison.Ordinal);
        Assert.Contains("20260828150000_AddCounterpartyWriteSupport", script, StringComparison.Ordinal);
        Assert.Contains("[counterparty].[Parties]", script, StringComparison.Ordinal);
        Assert.Contains("[counterparty].[CompanyPartyAccounts]", script, StringComparison.Ordinal);
        Assert.Contains("[counterparty].[PartyCodeSequence]", script, StringComparison.Ordinal);
        Assert.Contains("CK_Parties_Code_Format", script, StringComparison.Ordinal);
        Assert.Contains("FK_CompanyPartyAccounts_Companies_CompanyId", script, StringComparison.Ordinal);
        // AT-009: explicit index filters (the refused rollback is the SQL acceptance test's).
        Assert.Contains("20260930150000_AddPartyTaxIdentity", script, StringComparison.Ordinal);
        Assert.Contains("[counterparty].[PartyTaxIdentityChangeRequests]", script, StringComparison.Ordinal);
        Assert.Contains(
            "CREATE UNIQUE INDEX [UX_PartyTaxIdentityChangeRequests_Pending] ON [counterparty].[PartyTaxIdentityChangeRequests] ([CompanyId], [PartyId]) WHERE [Status] = N''Pending''",
            script, StringComparison.Ordinal);
        Assert.Contains(
            "CREATE INDEX [IX_Parties_TaxIdentityNumber] ON [counterparty].[Parties] ([TaxIdentityNumber]) WHERE [TaxIdentityNumber] IS NOT NULL",
            script, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE UNIQUE INDEX [IX_Parties_TaxIdentityNumber]", script, StringComparison.Ordinal);

        Assert.DoesNotContain("CREATE TABLE [audit].[AuditEvents]", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "CREATE TABLE [idempotency].[CommandExecutions]",
            script,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERSIS", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IBAN", script, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class UnresolvedCompanyContext : ICompanyContext
    {
        public bool IsResolved => false;

        public Guid CompanyId => Guid.Empty;

        public void Set(Guid companyId) =>
            throw new InvalidOperationException("Migration script testi company context çözmez.");
    }
}
