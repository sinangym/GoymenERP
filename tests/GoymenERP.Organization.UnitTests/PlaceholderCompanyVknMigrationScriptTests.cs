using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Organization.UnitTests;

/// <summary>
/// KIM-D35: the data migration clears only the stand-in "VKN" the fiscal seed once wrote on the placeholder company,
/// so the company is "VKN girilmemiş"; it changes no schema, never touches a real VKN and does not write the stand-in back
/// on rollback.
/// </summary>
public sealed class PlaceholderCompanyVknMigrationScriptTests
{
    private const string ClearMigration = "ClearPlaceholderCompanyVkn";
    private const string PreviousMigration = "AllowWorkCenterOvernightShifts";

    [Fact]
    public void Up_clears_only_the_stand_in_on_a_placeholder_company_that_issues_no_invoice_and_moves_its_version()
    {
        var migration = new ClearPlaceholderCompanyVkn();

        var update = Assert.IsType<SqlOperation>(Assert.Single(migration.UpOperations));
        Assert.Contains("UPDATE [organization].[Companies]", update.Sql, StringComparison.Ordinal);
        Assert.Contains("SET [Vkn] = NULL", update.Sql, StringComparison.Ordinal);
        Assert.Contains("[Version] = [Version] + 1", update.Sql, StringComparison.Ordinal);
        Assert.Contains("[IsPlaceholder] = 1", update.Sql, StringComparison.Ordinal);
        Assert.Contains("[InvoiceCapable] = 0", update.Sql, StringComparison.Ordinal);
        Assert.Contains("[Vkn] = N'1111111111'", update.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE", update.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Down_writes_nothing_back_and_changes_no_schema()
    {
        var migration = new ClearPlaceholderCompanyVkn();

        // The cleared value was not a VKN; a rollback neither restores it nor drops a VKN approved since then.
        Assert.Empty(migration.DownOperations);
    }

    [Fact]
    public void The_generated_script_has_the_update_and_no_constraint_or_column_change()
    {
        using var dbContext = CreateDbContext();
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            fromMigration: PreviousMigration,
            toMigration: ClearMigration);

        Assert.Contains("SET [Vkn] = NULL", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER TABLE", script, StringComparison.Ordinal);
        Assert.DoesNotContain("THROW", script, StringComparison.Ordinal);

        var rollback = dbContext.GetService<IMigrator>().GenerateScript(
            fromMigration: ClearMigration,
            toMigration: PreviousMigration);
        Assert.DoesNotContain("1111111111", rollback, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE [organization].[Companies]", rollback, StringComparison.Ordinal);
    }

    private static OrganizationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=GoymenErpOrganizationPlaceholderVknScript;Trusted_Connection=True;")
            .Options;
        return new OrganizationDbContext(options, new CompanyContext(), new OrganizationBootstrapWriteScope());
    }
}
