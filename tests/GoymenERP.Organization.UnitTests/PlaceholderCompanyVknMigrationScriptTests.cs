using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Organization.UnitTests;

/// <summary>
/// KIM-D35, superseded by KIM-D41 + KIM-D48: the placeholder VKN migration no longer rewrites the company directly. It
/// stays in the history as an empty step; the stand-in "VKN" is staged by StageCompanyTaxIdCorrections and moves only
/// through an approved, audited apply.
/// </summary>
public sealed class PlaceholderCompanyVknMigrationScriptTests
{
    private const string ClearMigration = "ClearPlaceholderCompanyVkn";
    private const string PreviousMigration = "AllowWorkCenterOvernightShifts";

    [Fact]
    public void Up_no_longer_rewrites_the_company_and_Down_writes_nothing_back()
    {
        var migration = new ClearPlaceholderCompanyVkn();

        Assert.Empty(migration.UpOperations);
        Assert.Empty(migration.DownOperations);
    }

    [Fact]
    public void The_generated_script_touches_no_company_row_in_either_direction()
    {
        using var dbContext = CreateDbContext();
        var script = dbContext.GetService<IMigrator>().GenerateScript(
            fromMigration: PreviousMigration,
            toMigration: ClearMigration);

        Assert.DoesNotContain("UPDATE [organization].[Companies]", script, StringComparison.Ordinal);
        Assert.DoesNotContain("1111111111", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER TABLE", script, StringComparison.Ordinal);

        var rollback = dbContext.GetService<IMigrator>().GenerateScript(
            fromMigration: ClearMigration,
            toMigration: PreviousMigration);
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
