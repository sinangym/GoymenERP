using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Organization.UnitTests;

/// <summary>
/// KIM-D41 + KIM-D48: the VKN data fix stages one pending correction per affected company instead of rewriting the
/// company, and its Down refuses while an applied correction (the approval trail) would be lost (forward fix).
/// </summary>
public sealed class CompanyTaxIdCorrectionMigrationScriptTests
{
    private const string StageMigration = "StageCompanyTaxIdCorrections";
    private const string PreviousMigration = "ClearPlaceholderCompanyVkn";

    [Fact]
    public void Up_creates_the_staging_table_then_stages_only_the_stand_in_on_a_placeholder_company()
    {
        var migration = new StageCompanyTaxIdCorrections();

        Assert.IsType<CreateTableOperation>(migration.UpOperations[0]);
        var stage = Assert.IsType<SqlOperation>(migration.UpOperations[^1]);
        Assert.Single(migration.UpOperations.OfType<SqlOperation>());
        Assert.Contains("INSERT INTO [organization].[CompanyTaxIdCorrections]", stage.Sql, StringComparison.Ordinal);
        Assert.Contains("[IsPlaceholder] = 1", stage.Sql, StringComparison.Ordinal);
        Assert.Contains("[InvoiceCapable] = 0", stage.Sql, StringComparison.Ordinal);
        Assert.Contains("[Vkn] = N'1111111111'", stage.Sql, StringComparison.Ordinal);
        Assert.Contains("N'organization.migration.stage-company-tax-id-corrections'", stage.Sql, StringComparison.Ordinal);
        Assert.Contains("Yer tutucu VKN işaretlendi", stage.Sql, StringComparison.Ordinal);
        Assert.Contains("N'Pending'", stage.Sql, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS", stage.Sql, StringComparison.Ordinal);
        // The company row itself is never rewritten by the migration; only an approved apply moves the VKN.
        Assert.DoesNotContain("UPDATE", stage.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", stage.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Down_starts_with_the_applied_row_guard_before_the_table_is_dropped()
    {
        var migration = new StageCompanyTaxIdCorrections();

        var guard = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Contains("THROW 51877", guard.Sql, StringComparison.Ordinal);
        Assert.Contains("[Status] = N'Applied'", guard.Sql, StringComparison.Ordinal);
        Assert.Single(migration.DownOperations.OfType<SqlOperation>());
        Assert.IsType<DropTableOperation>(migration.DownOperations[^1]);
    }

    [Fact]
    public void The_generated_scripts_keep_the_stage_after_the_table_and_the_refusal_before_the_drop()
    {
        using var dbContext = CreateDbContext();
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            fromMigration: PreviousMigration,
            toMigration: StageMigration);

        var create = script.IndexOf("CREATE TABLE [organization].[CompanyTaxIdCorrections]", StringComparison.Ordinal);
        var stage = script.IndexOf("INSERT INTO [organization].[CompanyTaxIdCorrections]", StringComparison.Ordinal);
        Assert.True(create >= 0);
        Assert.True(stage > create, "The stage must run after the staging table exists.");
        Assert.Contains("IX_CompanyTaxIdCorrections_CompanyId_Pending", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ON DELETE CASCADE", script, StringComparison.Ordinal);

        var rollback = dbContext.GetService<IMigrator>().GenerateScript(
            fromMigration: StageMigration,
            toMigration: PreviousMigration);
        var refusal = rollback.IndexOf("THROW 51877", StringComparison.Ordinal);
        var drop = rollback.IndexOf("DROP TABLE [organization].[CompanyTaxIdCorrections]", StringComparison.Ordinal);
        Assert.True(refusal >= 0, "Down must refuse while an applied correction exists (forward fix).");
        Assert.True(drop > refusal, "The refusal must run before the staging table is dropped.");
    }

    private static OrganizationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=GoymenErpOrganizationTaxIdCorrectionScript;Trusted_Connection=True;")
            .Options;
        return new OrganizationDbContext(options, new CompanyContext(), new OrganizationBootstrapWriteScope());
    }
}
