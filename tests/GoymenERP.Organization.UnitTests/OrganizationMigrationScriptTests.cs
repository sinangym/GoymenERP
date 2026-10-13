using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Organization.UnitTests;

public sealed class OrganizationMigrationScriptTests
{
    [Fact]
    public void FiscalRuntimeMigration_PreservesLegacyIdsAndFailsClosedOnCodeCollision()
    {
        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\mssqllocaldb;Database=GoymenErpOrganizationScript;Trusted_Connection=True;")
            .Options;
        using var dbContext = new OrganizationDbContext(
            options,
            new CompanyContext(),
            new OrganizationBootstrapWriteScope());
        var migrator = dbContext.GetService<IMigrator>();

        var script = migrator.GenerateScript(
            options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Contains("AddCompanyFiscalRuntimeSeed", script, StringComparison.Ordinal);
        Assert.Contains("10000000-0000-0000-0000-000000000001", script, StringComparison.Ordinal);
        Assert.Contains("10000000-0000-0000-0000-000000000002", script, StringComparison.Ordinal);
        Assert.Contains("10000000-0000-0000-0000-000000000003", script, StringComparison.Ordinal);
        Assert.Contains("TBD-GOY-01", script, StringComparison.Ordinal);
        Assert.Contains("TBD-GOY-02", script, StringComparison.Ordinal);
        Assert.Contains("THROW 51020", script, StringComparison.Ordinal);
        Assert.Contains("THROW 51021", script, StringComparison.Ordinal);
        Assert.Contains("THROW 51024", script, StringComparison.Ordinal);
        Assert.Contains("THROW 51025", script, StringComparison.Ordinal);
        Assert.Contains("THROW 51026", script, StringComparison.Ordinal);
        Assert.Contains("THROW 51027", script, StringComparison.Ordinal);
        Assert.Contains("N'4100046586'", script, StringComparison.Ordinal);
        Assert.Contains("N'4101507252'", script, StringComparison.Ordinal);
        Assert.Contains("N'1111111111'", script, StringComparison.Ordinal);
        Assert.Contains("[organization].[WorkCenters]", script, StringComparison.Ordinal);
        Assert.Contains("[organization].[Locations]", script, StringComparison.Ordinal);
        Assert.Contains("[organization].[Sites]", script, StringComparison.Ordinal);
        Assert.Contains(
            "81122f47-389b-e671-7134-8c7ceda1a775",
            script,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "eadfa1e8-ba24-d0ab-8a9d-95673e805de4",
            script,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "0655829a-27ee-8582-901c-0961d85263d7",
            script,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CK_Companies_Vkn_Format", script, StringComparison.Ordinal);
        Assert.Contains("Latin1_General_100_BIN2", script, StringComparison.Ordinal);
        Assert.Contains("CK_Companies_Placeholder_InvoiceDisabled", script, StringComparison.Ordinal);
        Assert.DoesNotContain("IX_Companies_Vkn", script, StringComparison.Ordinal);
        Assert.Contains("AddLocationOperatorParty", script, StringComparison.Ordinal);
        Assert.Contains("[OperatorPartyId]", script, StringComparison.Ordinal);
        Assert.Contains("CK_Locations_InternalOperatorParty", script, StringComparison.Ordinal);
        Assert.Contains("CK_Locations_OperatorParty_NotEmpty", script, StringComparison.Ordinal);
        var rollback = migrator.GenerateScript(
            "20261013155012_AddLocationOperatorParty",
            "20260831142045_AddCompanyFiscalRuntimeSeed");
        Assert.Contains("DROP COLUMN [OperatorPartyId]", rollback, StringComparison.Ordinal);
    }
}
